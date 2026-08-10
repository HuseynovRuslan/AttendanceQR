// A queued offline scan that the server refused, or that sat in the queue too long to be replayed
// honestly, kept until the employee has actually seen it.
//
// Why this exists: the drainer used to delete such an item and say nothing. The employee had already
// been shown a green "yadda saxlanıldı" card at tap time, so from their side the day was recorded —
// and then the tabel said Qayıb. Neither they nor the admin had any way to connect the two. The
// record is gone either way; what this buys is that somebody KNOWS, in time to fix it. The remedy is
// a MANUAL attendance entry for that day — not /admin/open-records, which only lists days that DO
// have a check-in ("çıxışı unudulan günlər"); a lost check-in appears there not at all. That is why
// the report has to carry the DAY.
//
// localStorage, not IndexedDB: it is a couple of short strings and it must be readable synchronously
// on the first paint of the home screen.

const KEY = 'qrlog:offlineRejects'
const MAX = 20

export interface OfflineReject {
  /** "OfflineRejected" (the server refused it) or "OfflineExpired" (too old to replay). */
  kind: 'OfflineRejected' | 'OfflineExpired'
  /** The server's error code, when there was one — e.g. OutsideRadius, AlreadyCompleted. */
  code?: string
  /** The phone's clock when the scan was originally taken, for "which day was this?". */
  scanAtIso: string
  atMs: number
  /**
   * WHOSE lost day this is. A site phone is shared: without this, Ali's warning is shown to Nigar,
   * and her "Anladım" clears the key — destroying the only notice Ali would ever have got.
   */
  employeeId?: string | null
}

/** Everything stored, for any employee. Callers filter — see readRejectsFor. */
function readAll(): OfflineReject[] {
  try {
    const raw = localStorage.getItem(KEY)
    const parsed = raw ? JSON.parse(raw) : []
    // A corrupted value must not white-screen the home page: `length` on a non-array is undefined,
    // the empty guard passes, and .map throws on the one screen the employee needs.
    return Array.isArray(parsed) ? (parsed as OfflineReject[]) : []
  } catch {
    return []
  }
}

/** This employee's lost days (plus any recorded before the owner stamp existed). */
export function readRejectsFor(employeeId: string | null): OfflineReject[] {
  return readAll().filter((r) => r.employeeId == null || r.employeeId === employeeId)
}

export function addReject(item: OfflineReject): void {
  try {
    // Newest first, capped — one stuck phone must not grow this without bound.
    localStorage.setItem(KEY, JSON.stringify([item, ...readAll()].slice(0, MAX)))
    window.dispatchEvent(new Event(REJECTS_CHANGED))
  } catch {
    /* private mode — the banner is best-effort, the failure report still went to the server */
  }
}

/** Dismiss only THIS employee's notices — never anyone else's unread warning. */
export function clearRejectsFor(employeeId: string | null): void {
  try {
    const keep = readAll().filter((r) => !(r.employeeId == null || r.employeeId === employeeId))
    if (keep.length === 0) localStorage.removeItem(KEY)
    else localStorage.setItem(KEY, JSON.stringify(keep))
    window.dispatchEvent(new Event(REJECTS_CHANGED))
  } catch {
    /* ignore */
  }
}

/** Fired when the list changes, so a banner already on screen can update itself. */
export const REJECTS_CHANGED = 'qrlog:offline-rejects-changed'
