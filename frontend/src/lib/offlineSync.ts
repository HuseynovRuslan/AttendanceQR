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
import { removeScan, scansFor, isTooOldToReplay, type QueuedScan } from './offlineQueue'

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
    const items = await scansFor(me)
    for (const item of items) {
      // Too old to replay honestly: past the server's 18-hour trust window it stops using the phone's
      // clock and stamps SERVER time, so this would not be recorded late — it would be recorded on
      // the wrong DAY, and if they have already checked in today it would be read as their check-out
      // and close a live shift. Drop it, but loudly: the employee sees a banner and the admin gets a
      // Problems row, which is what makes a manual correction possible.
      if (isTooOldToReplay(item, Date.now())) {
        reportFailure('OfflineExpired')
        addReject({ kind: 'OfflineExpired', scanAtIso: item.clientTimestampUtc, atMs: Date.now() })
        await removeScan(item.clientScanId)
        continue
      }

      try {
        const { status, data } = await apiRequest('/api/attendance/scan', { method: 'POST', body: toBody(item) })
        // 401 → the session needs attention (apiRequest already bounced to login); keep the queue and
        // stop, so nothing is lost. 5xx → transient server issue; keep it and stop too.
        if (status === 401 || status >= 500) break

        // A definitive 4xx (OutsideRadius, AlreadyCompleted, …) can never succeed on a retry, so the
        // item goes — but it is NOT a silent drop. The employee was shown a green "saved" card when
        // they tapped; without this they are simply absent that day and nobody knows why.
        if (status >= 400) {
          const code = errorCode(data)
          reportFailure('OfflineRejected')
          addReject({ kind: 'OfflineRejected', code, scanAtIso: item.clientTimestampUtc, atMs: Date.now() })
        }
        await removeScan(item.clientScanId)
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
  const run = () => void syncOfflineScans()
  run()
  window.addEventListener('online', run)
  return () => window.removeEventListener('online', run)
}
