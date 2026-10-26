// Watches ONE queued scan until it has left the offline queue, and says how it left.
//
// The result card after an offline save reads «hələ serverə çatmayıb». That sentence was true for
// hours on 24.10.2026: at two large sites the building Wi-Fi was too slow for the selfie, every
// check-in timed out into the queue, and the queue is only drained when the app is reopened, comes
// back to the foreground, or the 60-second heartbeat fires while it happens to be open. Twenty-four
// people stood on the board as absent for 30–156 minutes, and their managers typed the records in
// by hand. Nothing was lost — but nobody standing at the poster could tell.
//
// So, for as long as the card is on screen and the page is visible, this asks the SHARED drain
// (offlineSync.syncOfflineScans — the same one the heartbeat runs; it refuses to run twice at once,
// so the two can never send one tap in parallel) to try again every fifteen seconds, and the moment
// the tap is no longer queued it reports the verdict the drain archived it with. The card then says
// what the server actually did — recorded, already had it, or refused it — instead of «saxlanıldı».
// Ten minutes, then it stops quietly and the heartbeat is on its own, exactly as before.
import { archivedScans, scansFor, type ArchivedScan } from './offlineQueue'
import { syncOfflineScans } from './offlineSync'

export const QUEUE_WATCH_INTERVAL_MS = 15_000
export const QUEUE_WATCH_MAX_MS = 10 * 60_000

/** How the watched scan left the queue — the drain's own verdict (see offlineQueue.ScanOutcome). */
export type QueueVerdict =
  /** The server recorded it; `action` says as what, when the server said. */
  | { outcome: 'sent'; action?: 'CheckIn' | 'CheckOut'; atMs: number }
  /** The server already had the day and took this tap as a retry that changed nothing. */
  | { outcome: 'ignored'; code?: string; atMs: number }
  /** The server refused it — `code` is its reason. NOT a success, and the card must not say it is. */
  | { outcome: 'rejected'; code?: string; atMs: number }

function verdictOf(kept: ArchivedScan | undefined): QueueVerdict {
  // Left the queue, but not through the archive. Every exit in this codebase goes through settleScan,
  // which writes both stores in one transaction, so this is a stale read at worst — and "no longer
  // queued" is the one fact the card was waiting for.
  if (!kept) return { outcome: 'sent', atMs: Date.now() }
  switch (kept.outcome) {
    case 'sent': return { outcome: 'sent', action: kept.action, atMs: kept.settledAtMs }
    case 'ignored': return { outcome: 'ignored', code: kept.code, atMs: kept.settledAtMs }
    case 'rejected': return { outcome: 'rejected', code: kept.code, atMs: kept.settledAtMs }
    // Cannot happen inside ten minutes of a thirty-day window — named anyway, as a refusal.
    case 'expired': return { outcome: 'rejected', code: 'OfflineExpired', atMs: kept.settledAtMs }
  }
}

/**
 * Starts watching; returns the function that stops it. `onSettled` is called at most once, and never
 * after stop. `employeeId` is the owner the scan was queued under — the queue is per device, and the
 * lookup must read it as the drain does, as that person's.
 */
export function watchQueuedScan(
  clientScanId: string,
  employeeId: string | null,
  onSettled: (verdict: QueueVerdict) => void,
  opts: { intervalMs?: number; maxMs?: number; isVisible?: () => boolean } = {},
): () => void {
  const intervalMs = opts.intervalMs ?? QUEUE_WATCH_INTERVAL_MS
  const maxMs = opts.maxMs ?? QUEUE_WATCH_MAX_MS
  const isVisible = opts.isVisible
    ?? (() => typeof document === 'undefined' || document.visibilityState === 'visible')
  const startedAt = Date.now()
  let stopped = false
  let timer: ReturnType<typeof setTimeout> | undefined

  // A timeout chain rather than setInterval: a drain stuck on a slow replay must finish before the
  // next tick is even scheduled, so ticks never pile up behind it.
  const schedule = () => {
    if (!stopped) timer = setTimeout(() => void tick(), intervalMs)
  }

  async function tick() {
    if (stopped) return
    if (Date.now() - startedAt > maxMs) return
    try {
      // Hidden pages do nothing: the phone is in a pocket, and the global sync already runs the
      // moment it comes back to the foreground.
      if (isVisible()) {
        await syncOfflineScans()
        const queued = (await scansFor(employeeId)).some((s) => s.clientScanId === clientScanId)
        if (!queued) {
          const kept = (await archivedScans(employeeId)).find((s) => s.clientScanId === clientScanId)
          if (stopped) return
          stopped = true
          onSettled(verdictOf(kept))
          return
        }
      }
    } catch {
      /* an IndexedDB hiccup — the next tick simply reads again */
    }
    schedule()
  }

  schedule()
  return () => {
    stopped = true
    if (timer !== undefined) clearTimeout(timer)
  }
}
