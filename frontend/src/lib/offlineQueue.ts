// A tiny IndexedDB queue for scans made with no connection. Each item is one check-in/out tap that
// couldn't reach the server; the sync drainer (offlineSync.ts) replays them when the app is online.
// Kept dependency-free and separate from the device-id store so neither can corrupt the other.

const DB_NAME = 'qrlog-offline'
const STORE = 'scans'
/**
 * Where a scan goes when it leaves the queue — sent, ignored, refused or too old. NEVER deleted: the
 * owner's rule (2026-09-18) is that an offline scan is never lost, and the phone is the one place that
 * still holds the tap, the time, the position and the selfie when somebody asks «I scanned — where is
 * my day?». It used to be thrown away at 18 hours: ninety scans by forty-two people in one month.
 */
const ARCHIVE = 'archive'
const VERSION = 2

/**
 * How long the SERVER still accepts a queued scan — its OfflineTrustWindowHours, thirty days. Past it
 * the scan is not replayed (the server would refuse it) but it is not deleted either: it moves to the
 * archive with the verdict, and the employee and the admin are told.
 */
export const MAX_QUEUED_AGE_MS = 30 * 24 * 60 * 60 * 1000

/** One queued scan — everything the /api/attendance/scan call needs, captured at tap time. */
export interface QueuedScan {
  clientScanId: string
  qrToken: string
  deviceFingerprint: string
  latitude: number
  longitude: number
  photoBase64?: string
  /** The phone's clock (ISO) when the scan was taken — the server uses this as the record time. */
  clientTimestampUtc: string
  queuedAtMs: number
  /**
   * WHOSE scan this is, from the JWT at tap time. The queue lives on the device, not in the session,
   * so without this a shared site phone replays whatever is queued under whoever happens to be signed
   * in next: employee A's scan becomes B's check-in, with A's selfie filed under B and face-matched
   * against B's reference. Undefined only for items queued before this field existed.
   */
  employeeId?: string
  /**
   * The employee answered «bəli, çıxıram» before this was queued. A check-out within two hours of
   * arriving is otherwise read by the server as a «did it work?» retry and changes nothing — see
   * EarlyCheckOutRules on the server. Absent on everything queued before the question existed.
   */
  confirmEarlyCheckOut?: boolean
}

/**
 * Statuses that mean the SERVER is temporarily unreachable — a deploy window, a crashed backend
 * behind a live proxy, an overloaded gateway — not that the scan was judged and refused. These are
 * the "your tap must not be lost" cases: the scan goes to the queue exactly as if the network had
 * dropped, and the replay is safe because every tap carries an idempotency id from the start.
 *
 * Deliberately NOT 500: a deterministic server bug answers 500 to the same payload every time, so
 * queueing it would retry a scan that can never succeed and hide the bug behind a green card. And
 * never any 4xx — those are real answers (wrong device, outside the fence, session expired), and
 * treating them as outages would replay scans the server has already refused for a reason.
 *
 * A pure rule on purpose, so it can be tested without a browser.
 */
export function isServerUnavailable(status: number): boolean {
  return status === 502 || status === 503 || status === 504
}

/** Fired on the window whenever the queue changes, so any badge can refresh its count. */
export const QUEUE_CHANGED = 'qrlog:queue-changed'

function notifyChanged() {
  if (typeof window !== 'undefined') window.dispatchEvent(new Event(QUEUE_CHANGED))
}

function openDb(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const req = indexedDB.open(DB_NAME, VERSION)
    req.onupgradeneeded = () => {
      if (!req.result.objectStoreNames.contains(STORE)) {
        req.result.createObjectStore(STORE, { keyPath: 'clientScanId' })
      }
      if (!req.result.objectStoreNames.contains(ARCHIVE)) {
        req.result.createObjectStore(ARCHIVE, { keyPath: 'clientScanId' })
      }
    }
    req.onsuccess = () => resolve(req.result)
    req.onerror = () => reject(req.error)
  })
}

export async function enqueueScan(item: QueuedScan): Promise<void> {
  const db = await openDb()
  await new Promise<void>((resolve, reject) => {
    const tx = db.transaction(STORE, 'readwrite')
    tx.objectStore(STORE).put(item)
    tx.oncomplete = () => resolve()
    tx.onerror = () => reject(tx.error)
  })
  db.close()
  notifyChanged()
}

/**
 * May THIS employee replay this queued scan? Their own, plus any legacy item queued before employeeId
 * existed. Legacy items are still sent rather than dropped — the overwhelmingly common case is the
 * same person on their own phone, and dropping would cost them the attendance record this queue
 * exists to protect. That window closes on its own: nothing new is written without an owner, and
 * anything left is cleared by the age rule below.
 *
 * A pure rule on purpose, so it can be tested without a browser — the cost of getting it wrong is
 * one person's attendance recorded against another person's name.
 */
export function mayReplay(item: QueuedScan, employeeId: string | null): boolean {
  return item.employeeId === undefined || item.employeeId === employeeId
}

/**
 * Past this age the phone's clock is no longer trusted by the server, so replaying would write the
 * WRONG DAY rather than a late one. See MAX_QUEUED_AGE_MS.
 */
export function isTooOldToReplay(item: QueuedScan, nowMs: number): boolean {
  return nowMs - item.queuedAtMs > MAX_QUEUED_AGE_MS
}

/** The queued scans this employee may replay, oldest first. */
export async function scansFor(employeeId: string | null): Promise<QueuedScan[]> {
  const all = await allScans()
  return all.filter((s) => mayReplay(s, employeeId))
}

export async function allScans(): Promise<QueuedScan[]> {
  const db = await openDb()
  const items = await new Promise<QueuedScan[]>((resolve, reject) => {
    const req = db.transaction(STORE, 'readonly').objectStore(STORE).getAll()
    req.onsuccess = () => resolve(req.result as QueuedScan[])
    req.onerror = () => reject(req.error)
  })
  db.close()
  // Oldest first — replay in the order the taps happened.
  return items.sort((a, b) => a.queuedAtMs - b.queuedAtMs)
}

/**
 * Deletes a queued scan outright. Nothing in the app calls this any more — a scan leaves the queue
 * through settleScan, into the archive. Kept only so an older caller fails loudly in review rather
 * than silently losing a tap; do not use it for anything a person scanned.
 * @deprecated use settleScan
 */
export async function removeScan(clientScanId: string): Promise<void> {
  const db = await openDb()
  await new Promise<void>((resolve, reject) => {
    const tx = db.transaction(STORE, 'readwrite')
    tx.objectStore(STORE).delete(clientScanId)
    tx.oncomplete = () => resolve()
    tx.onerror = () => reject(tx.error)
  })
  db.close()
  notifyChanged()
}

/** How a scan left the queue. */
export type ScanOutcome =
  /** The server recorded it — `action` says as what. */
  | { outcome: 'sent'; action?: 'CheckIn' | 'CheckOut' }
  /** The server already had it, or took it as a retry that changes nothing (`code` says which). */
  | { outcome: 'ignored'; code?: string }
  /** The server refused it — `code` is its reason. Kept with the selfie: it is the employee's evidence. */
  | { outcome: 'rejected'; code?: string }
  /** Older than the server accepts, or left by an account no longer on this phone. */
  | { outcome: 'expired' }

/** A scan that has left the queue, kept on the phone for good. */
export type ArchivedScan = QueuedScan & ScanOutcome & { settledAtMs: number }

/**
 * Take a scan out of the queue — into the archive, never into the bin. One transaction, so a scan is
 * always in exactly one of the two. A sent one drops its selfie (the server has it; the phone's space
 * is better kept for the ones that were refused, where the photo is the proof).
 */
export async function settleScan(item: QueuedScan, result: ScanOutcome): Promise<void> {
  const db = await openDb()
  const kept: ArchivedScan = { ...item, ...result, settledAtMs: Date.now() }
  if (result.outcome === 'sent' || result.outcome === 'ignored') delete kept.photoBase64
  await new Promise<void>((resolve, reject) => {
    const tx = db.transaction([STORE, ARCHIVE], 'readwrite')
    tx.objectStore(ARCHIVE).put(kept)
    tx.objectStore(STORE).delete(item.clientScanId)
    tx.oncomplete = () => resolve()
    tx.onerror = () => reject(tx.error)
  })
  db.close()
  notifyChanged()
}

/** This employee's archived scans, newest first — what the «Oflayn skanlarım» list shows. */
export async function archivedScans(employeeId: string | null): Promise<ArchivedScan[]> {
  const db = await openDb()
  const items = await new Promise<ArchivedScan[]>((resolve, reject) => {
    const req = db.transaction(ARCHIVE, 'readonly').objectStore(ARCHIVE).getAll()
    req.onsuccess = () => resolve(req.result as ArchivedScan[])
    req.onerror = () => reject(req.error)
  })
  db.close()
  return items
    .filter((s) => mayReplay(s, employeeId))
    .sort((a, b) => (a.clientTimestampUtc < b.clientTimestampUtc ? 1 : -1))
}

export async function scanCount(): Promise<number> {
  try {
    const db = await openDb()
    const n = await new Promise<number>((resolve, reject) => {
      const req = db.transaction(STORE, 'readonly').objectStore(STORE).count()
      req.onsuccess = () => resolve(req.result)
      req.onerror = () => reject(req.error)
    })
    db.close()
    return n
  } catch {
    return 0
  }
}
