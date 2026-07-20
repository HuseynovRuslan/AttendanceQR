// Drains the offline scan queue (offlineQueue.ts) back to the server. Called when the app loads and
// whenever the connection returns. Each item carries the client id it was first sent with, so the
// server de-duplicates replays — a scan is never double-recorded even if this runs twice.
import { apiRequest, getToken } from '../api/client'
import { allScans, removeScan, type QueuedScan } from './offlineQueue'

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

/** Replays every queued scan. Safe to call repeatedly; a re-entrant call is a no-op. */
export async function syncOfflineScans(): Promise<void> {
  if (syncing) return
  if (typeof navigator !== 'undefined' && navigator.onLine === false) return
  // Only sync while signed in — otherwise the replay would 401 and bounce a logged-out user to /login.
  if (!getToken()) return
  syncing = true
  try {
    const items = await allScans()
    for (const item of items) {
      try {
        const { status } = await apiRequest('/api/attendance/scan', { method: 'POST', body: toBody(item) })
        // 401 → the session needs attention (apiRequest already bounced to login); keep the queue and
        // stop, so nothing is lost. 5xx → transient server issue; keep it and stop too. Anything else
        // (2xx success, or a definitive 4xx like OutsideRadius/AlreadyCompleted that a retry can't fix)
        // → drop it.
        if (status === 401 || status >= 500) break
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
