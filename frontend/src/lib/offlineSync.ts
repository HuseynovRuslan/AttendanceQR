// Drains the offline scan queue (offlineQueue.ts) back to the server. Called when the app loads and
// whenever the connection returns. Each item carries the client id it was first sent with, so the
// server de-duplicates replays — a scan is never double-recorded even if this runs twice.
//
// Two rules this file exists to keep:
//   • Only replay the SIGNED-IN employee's own scans. The queue lives on the device; a shared site
//     phone would otherwise send A's scan under B's session.
//   • A queued scan never disappears in silence. If it cannot be replayed honestly, the employee is
//     told and the admin sees it on the Problems screen — because the person tapped the button, saw
//     a green card, and would otherwise just be marked absent.
import { apiRequest, getToken } from '../api/client'
import { decodeJwt } from './jwt'
import { reportFailure } from './scanFailures'
import { addReject } from './offlineRejects'
import { allScans, removeScan, scansFor, isTooOldToReplay, type QueuedScan } from './offlineQueue'
import { flushFailures } from './scanFailures'

let syncing = false

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

/** Replays every queued scan for the signed-in employee. Safe to call repeatedly; re-entrant is a no-op. */
export async function syncOfflineScans(): Promise<void> {
  if (syncing) return
  if (typeof navigator !== 'undefined' && navigator.onLine === false) return
  // Only sync while signed in — otherwise the replay would 401 and bounce a logged-out user to /login.
  const token = getToken()
  if (!token) return
  const me = decodeJwt(token)?.sub ?? null

  syncing = true
  try {
    // Age out EVERY stale item first, including one belonging to somebody else: a foreign item is
    // skipped by the owner filter below and would otherwise sit on this phone for ever, carrying a
    // few hundred kB of selfie. It is dropped locally and NOT reported — the audit row would be
    // attributed to whoever is signed in now, which is the very misattribution this change fixed.
    const now = Date.now()
    for (const item of await allScans()) {
      if (isTooOldToReplay(item, now) && item.employeeId !== undefined && item.employeeId !== me)
        await removeScan(item.clientScanId)
    }

    const items = await scansFor(me)
    for (const item of items) {
      // Too old to replay honestly: past the server's 18-hour trust window it stops using the phone's
      // clock and stamps SERVER time, so this would not be recorded late — it would be recorded on
      // the wrong DAY, and if they have already checked in today it would be read as their check-out
      // and close a live shift. Drop it, but loudly: the employee sees a banner and the admin gets a
      // Problems row, which is what makes a manual correction possible.
      if (isTooOldToReplay(item, Date.now())) {
        // Dropped FIRST: if the report threw, "reported but still queued" would report it again on
        // every drain, and the throw would escape and abandon the remaining items.
        await removeScan(item.clientScanId)
        reportFailure('OfflineExpired', undefined, item.clientTimestampUtc)
        addReject({ kind: 'OfflineExpired', scanAtIso: item.clientTimestampUtc, atMs: Date.now(), employeeId: me })
        continue
      }

      try {
        const { status, data } = await apiRequest('/api/attendance/scan', { method: 'POST', body: toBody(item) })
        // 401 → the session needs attention (apiRequest already bounced to login); keep the queue and
        // stop, so nothing is lost. 5xx → transient server issue; keep it and stop too.
        //
        // 403 as well, and it is the interesting one: an account still on a temporary PIN is refused
        // everything but the "set your PIN" endpoint, so a scan queued before the reset would be
        // thrown away for a condition that clears the moment they pick a PIN. Every 403 the scan can
        // draw is a state of the ACCOUNT, not of the scan, so none of them is a reason to drop it.
        if (status === 401 || status === 403 || status >= 500) break

        // A definitive 4xx (OutsideRadius, AlreadyCompleted, …) can never succeed on a retry, so the
        // item goes — but it is NOT a silent drop. The employee was shown a green "saved" card when
        // they tapped; without this they are simply absent that day and nobody knows why.
        const code = errorCode(data)
        await removeScan(item.clientScanId)
        if (status >= 400 && !ALREADY_RECORDED.has(code ?? '')) {
          reportFailure('OfflineRejected', undefined, item.clientTimestampUtc)
          addReject({ kind: 'OfflineRejected', code, scanAtIso: item.clientTimestampUtc, atMs: Date.now(), employeeId: me })
        }
      } catch {
        // Network dropped mid-drain — stop; the rest stays queued for the next attempt.
        break
      }
    }
  } finally {
    syncing = false
  }
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
