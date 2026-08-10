// A queued offline scan that the server refused, or that sat in the queue too long to be replayed
// honestly, kept until the employee has actually seen it.
//
// Why this exists: the drainer used to delete such an item and say nothing. The employee had already
// been shown a green "yadda saxlanıldı" card at tap time, so from their side the day was recorded —
// and then the tabel said Qayıb. Neither they nor the admin had any way to connect the two. The
// record is gone either way; what this buys is that somebody KNOWS, in time to fix it from
// /admin/open-records or with a manual entry.
//
// localStorage, not IndexedDB: it is a couple of short strings and it must be readable synchronously
// on the first paint of the home screen.

const KEY = 'qrlog:offlineRejects'
const MAX = 5

export interface OfflineReject {
  /** "OfflineRejected" (the server refused it) or "OfflineExpired" (too old to replay). */
  kind: 'OfflineRejected' | 'OfflineExpired'
  /** The server's error code, when there was one — e.g. OutsideRadius, AlreadyCompleted. */
  code?: string
  /** The phone's clock when the scan was originally taken, for "which day was this?". */
  scanAtIso: string
  atMs: number
}

export function readRejects(): OfflineReject[] {
  try {
    const raw = localStorage.getItem(KEY)
    return raw ? (JSON.parse(raw) as OfflineReject[]) : []
  } catch {
    return []
  }
}

export function addReject(item: OfflineReject): void {
  try {
    // Newest first, capped — one stuck phone must not grow this without bound.
    localStorage.setItem(KEY, JSON.stringify([item, ...readRejects()].slice(0, MAX)))
    window.dispatchEvent(new Event(REJECTS_CHANGED))
  } catch {
    /* private mode — the banner is best-effort, the failure report still went to the server */
  }
}

export function clearRejects(): void {
  try {
    localStorage.removeItem(KEY)
    window.dispatchEvent(new Event(REJECTS_CHANGED))
  } catch {
    /* ignore */
  }
}

/** Fired when the list changes, so a banner already on screen can update itself. */
export const REJECTS_CHANGED = 'qrlog:offline-rejects-changed'
