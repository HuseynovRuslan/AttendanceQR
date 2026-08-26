// Drains the offline scan queue (offlineQueue.ts) back to the server. Called when the app loads and
// whenever the connection returns. Each item carries the client id it was first sent with, so the
// server de-duplicates replays — a scan is never double-recorded even if this runs twice.
//
// Two rules this file exists to keep:
//   • Every scan is replayed AS THE PERSON WHO MADE IT, never as whoever is signed in. The queue
//     lives on the device, so on a crew phone — where thirty workers take turns at one handset — the
//     naive drain files A's scan, and A's selfie, under B. Each item carries its owner and each is
//     sent with that owner's own saved token.
//   • A queued scan never disappears in silence. If it cannot be replayed honestly, the employee is
//     told and the admin sees it on the Problems screen — because the person tapped the button, saw
//     a green card, and would otherwise just be marked absent.
import { apiRequest, getToken } from '../api/client'
import { decodeJwt } from './jwt'
import { listProfiles } from './profiles'
import { reportFailure } from './scanFailures'
import { addReject } from './offlineRejects'
import { allScans, removeScan, scansFor, isTooOldToReplay, type QueuedScan } from './offlineQueue'
import { flushFailures } from './scanFailures'

let syncing = false

/** One account this device can speak for, and the token to speak with. */
interface Identity {
  employeeId: string | null
  /**
   * Undefined for the ACTIVE session — it goes through the ordinary path, so a 401 there correctly
   * ends the session of the person holding the phone. A saved profile carries its own token instead,
   * and its 401 is contained to that profile.
   */
  token?: string
}

/**
 * Everyone whose queued scans this device may send: the signed-in employee first, then every other
 * saved profile. Order matters only in that the holder's own scans go first, which is the queue most
 * likely to be watched.
 */
function identities(activeToken: string): Identity[] {
  const me = decodeJwt(activeToken)?.sub ?? null
  const out: Identity[] = [{ employeeId: me }]
  for (const p of listProfiles()) if (p.employeeId !== me) out.push({ employeeId: p.employeeId, token: p.token })
  return out
}

function toBody(item: QueuedScan) {
  return {
    qrToken: item.qrToken,
    deviceFingerprint: item.deviceFingerprint,
    latitude: item.latitude,
    longitude: item.longitude,
    ...(item.photoBase64 ? { photoBase64: item.photoBase64 } : {}),
    clientScanId: item.clientScanId,
    clientTimestampUtc: item.clientTimestampUtc,
    offline: true,
  }
}

/**
 * A definitive 4xx that means the attendance is ALREADY RECORDED, not lost. An offline batch produces
 * these routinely — two morning scans arrive together and the second is refused because the first
 * already closed the shift. Reporting them would raise a red "your day was lost" banner and a
 * BLOCKING row on the Problems screen for an ordinary, harmless outcome, and admins who see that a
 * few times stop trusting the colour that actually matters.
 */
const ALREADY_RECORDED = new Set(['AlreadyCompleted', 'DuplicateCheckIn', 'TooSoonToCheckOut'])

/** The server's error code, when the body carries one. */
function errorCode(data: unknown): string | undefined {
  return data && typeof data === 'object' && 'error' in data && typeof data.error === 'string'
    ? data.error
    : undefined
}

/**
 * Replays every queued scan this device can still speak for — the signed-in employee's, and every
 * saved profile's. Safe to call repeatedly; re-entrant is a no-op.
 */
export async function syncOfflineScans(): Promise<void> {
  if (syncing) return
  if (typeof navigator !== 'undefined' && navigator.onLine === false) return
  // A signed-in session is still the precondition: with nobody signed in there is no active account
  // to bound the pass, and replaying saved profiles from a logged-out phone would be a background
  // job nobody asked for.
  const token = getToken()
  if (!token) return

  syncing = true
  try {
    const who = identities(token)
    const known = new Set(who.map((i) => i.employeeId))

    // Age out every stale item belonging to somebody this device can NO LONGER speak for — a profile
    // that was removed, or a scan left behind by a previous holder of the phone. Such an item is
    // skipped by every drain below and would otherwise sit here for ever carrying a few hundred kB of
    // selfie. Dropped locally and NOT reported: the report would be attributed to whoever is signed
    // in now, which is the very misattribution this file exists to prevent.
    const now = Date.now()
    for (const item of await allScans()) {
      if (isTooOldToReplay(item, now) && item.employeeId !== undefined && !known.has(item.employeeId))
        await removeScan(item.clientScanId)
    }

    for (const identity of who) if (!(await drainFor(identity))) break
  } finally {
    syncing = false
  }
}

/**
 * Replays one account's queued scans. Returns false when the drain should stop entirely — the network
 * dropped or the server is unwell, so trying the next account would only burn battery on a phone that
 * has just come back from a day in a pocket.
 *
 * A 401 or 403 returns TRUE: those are states of one ACCOUNT (its PIN was reset, it is still on a
 * temporary PIN), and the other twenty-nine people on a crew phone have nothing to do with it.
 */
async function drainFor({ employeeId: me, token }: Identity): Promise<boolean> {
  const as = token ? { token } : {}
  // Which account a failure report should be filed under. Undefined for the active session — the
  // report then goes the ordinary way, as the signed-in employee — and the profile's own id when we
  // are draining somebody else's queue, so scanFailures can send it with THEIR token.
  const reportAs = token ? me ?? undefined : undefined

  for (const item of await scansFor(me)) {
    // Too old to replay honestly: past the server's 18-hour trust window it stops using the phone's
    // clock and stamps SERVER time, so this would not be recorded late — it would be recorded on the
    // wrong DAY, and if they have already checked in today it would be read as their check-out and
    // close a live shift. Drop it, but loudly: the employee sees a banner and the admin gets a
    // Problems row, which is what makes a manual correction possible.
    if (isTooOldToReplay(item, Date.now())) {
      // Dropped FIRST: if the report threw, "reported but still queued" would report it again on
      // every drain, and the throw would escape and abandon the remaining items.
      await removeScan(item.clientScanId)
      reportFailure('OfflineExpired', undefined, item.clientTimestampUtc, reportAs)
      addReject({ kind: 'OfflineExpired', scanAtIso: item.clientTimestampUtc, atMs: Date.now(), employeeId: me })
      continue
    }

    try {
      const { status, data } = await apiRequest('/api/attendance/scan', { method: 'POST', body: toBody(item), ...as })

      // The server is unwell — a deploy window, an overloaded gateway. Stop the whole pass; nothing
      // is dropped and the heartbeat tries again shortly.
      if (status >= 500) return false

      // 401 and 403 are states of one ACCOUNT, not of the scan and not of the server: its PIN was
      // reset, or it is still on a temporary PIN and refused everything but the "set your PIN"
      // endpoint. Its queue keeps — a scan must never be thrown away for a condition that clears the
      // moment somebody picks a PIN — and the other accounts on this phone still get their turn.
      if (status === 401 || status === 403) return true

      // A definitive 4xx (OutsideRadius, AlreadyCompleted, …) can never succeed on a retry, so the
      // item goes — but it is NOT a silent drop. The employee was shown a green "saved" card when
      // they tapped; without this they are simply absent that day and nobody knows why.
      const code = errorCode(data)
      await removeScan(item.clientScanId)
      if (status >= 400 && !ALREADY_RECORDED.has(code ?? '')) {
        reportFailure('OfflineRejected', undefined, item.clientTimestampUtc, reportAs)
        addReject({ kind: 'OfflineRejected', code, scanAtIso: item.clientTimestampUtc, atMs: Date.now(), employeeId: me })
      }
    } catch {
      // Network dropped mid-drain — stop everything; the rest stays queued for the next attempt.
      return false
    }
  }
  return true
}

/** Wire the app so the queue drains on load and whenever the connection returns. Idempotent. */
export function startOfflineSync(): () => void {
  const run = () => {
    void syncOfflineScans()
    // The drain is now a PRODUCER of failure reports, so flush any that could not be sent.
    void flushFailures()
  }
  run()
  window.addEventListener('online', run)
  // 'online' never fires on a one-bar link where navigator.onLine stayed true the whole time, and
  // it never fires when the app is simply reopened. Coming back to the foreground is the moment a
  // phone that has been in a pocket all morning actually has signal again.
  document.addEventListener('visibilitychange', onVisible)
  // And NEITHER of those fires when the network was fine all along but the SERVER was down — a scan
  // queued during a deploy window, with the app left open on the result card, would wait for a
  // signal that never comes. A slow heartbeat is the recovery path for exactly that case; with an
  // empty queue it is a couple of local IndexedDB reads and no network, so idling costs nothing.
  const heartbeat = window.setInterval(run, 60_000)
  return () => {
    window.removeEventListener('online', run)
    document.removeEventListener('visibilitychange', onVisible)
    window.clearInterval(heartbeat)
  }

  function onVisible() {
    if (document.visibilityState === 'visible') run()
  }
}
