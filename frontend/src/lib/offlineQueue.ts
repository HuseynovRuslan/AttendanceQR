// A tiny IndexedDB queue for scans made with no connection. Each item is one check-in/out tap that
// couldn't reach the server; the sync drainer (offlineSync.ts) replays them when the app is online.
// Kept dependency-free and separate from the device-id store so neither can corrupt the other.

const DB_NAME = 'qrlog-offline'
const STORE = 'scans'
const VERSION = 1

/**
 * How long a queued scan is still worth replaying. The server trusts a client timestamp only within
 * 18 hours (AttendanceController); past that it silently falls back to SERVER time — so a Thursday
 * scan syncing on Monday is not written late, it is written as a MONDAY check-in, and if the person
 * already checked in that morning it is taken as their check-OUT and closes a live shift. Dropping it
 * with a visible warning is the lesser harm, and the only one the employee can act on.
 */
export const MAX_QUEUED_AGE_MS = 18 * 60 * 60 * 1000

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
