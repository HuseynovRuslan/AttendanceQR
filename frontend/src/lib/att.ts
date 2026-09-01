import type { AttendanceRecord } from '../api/attendance'
import { COMPANY_TZ } from './format'

// Attendance-domain helpers. The time/date formatters that used to live here moved to lib/format.ts,
// where someone looking for a time formatter can actually find them.

/**
 * Today's date AS THE COMPANY COUNTS IT, not as UTC does.
 *
 * `attendanceDate` is stamped in company time by the backend, so a date derived from `toISOString()`
 * disagrees with it for the four hours after midnight in Baku (UTC+4): at 01:00 the phone would look
 * up yesterday's row and find the open night shift sitting where today's should be. Nobody noticed
 * because day-shift staff are asleep then — the people awake at that hour are exactly the night
 * workers this file now has to get right.
 */
export function companyDate(now: Date = new Date()): string {
  // en-CA formats as YYYY-MM-DD, which is the shape the API uses.
  return new Intl.DateTimeFormat('en-CA', { timeZone: COMPANY_TZ }).format(now)
}

export function todayStr(): string {
  return companyDate()
}

/** The hour 0–23 in company time. Same clock the server checks its noon pivot against. */
function companyHour(now: Date): number {
  const parts = new Intl.DateTimeFormat('en-GB', { timeZone: COMPANY_TZ, hour: 'numeric', hour12: false })
    .formatToParts(now)
  // Some engines render midnight as "24"; the modulo makes both spellings mean the same hour.
  return Number(parts.find((p) => p.type === 'hour')?.value ?? '0') % 24
}

function dayBefore(dateStr: string): string {
  const d = new Date(`${dateStr}T12:00:00Z`)
  d.setUTCDate(d.getUTCDate() - 1)
  return d.toISOString().slice(0, 10)
}

export function firstName(fullName: string | null | undefined): string {
  return fullName ? fullName.trim().split(/\s+/)[0] : ''
}

export function initials(fullName: string | null | undefined): string {
  if (!fullName) return '?'
  const p = fullName.trim().split(/\s+/)
  return ((p[0]?.[0] ?? '') + (p[1]?.[0] ?? '')).toUpperCase() || '?'
}

export type TodayState =
  /** `pending` — the step was taken on this phone and is still waiting to reach the server. */
  | { kind: 'none' }
  | { kind: 'in'; checkIn: string; pending?: boolean }
  | { kind: 'done'; checkIn: string; checkOut: string; pending?: boolean }

export function todayState(records: AttendanceRecord[]): TodayState {
  const rec = records.find((r) => r.attendanceDate === todayStr())
  if (!rec?.checkInAtUtc) return { kind: 'none' }
  if (!rec.checkOutAtUtc) return { kind: 'in', checkIn: rec.checkInAtUtc }
  return { kind: 'done', checkIn: rec.checkInAtUtc, checkOut: rec.checkOutAtUtc }
}

/** Just enough of a queued scan to place it in the day. */
export interface PendingScan {
  /** The phone's clock when the scan was taken — the time the server will record it at. */
  clientTimestampUtc: string
}

/**
 * The day as the EMPLOYEE experienced it, not as the server has heard about it.
 *
 * Reported from the field, and it is not cosmetic. A scan taken with no signal is saved on the phone
 * and sent later, but the home screen was built only from what the server knows — so after an offline
 * check-in it still read "Giriş et · Hələ giriş etməmisiniz". People concluded the scan had not
 * worked and scanned again.
 *
 * That second tap is the real damage. It is a separate scan with its own idempotency id, so both
 * eventually reach the server, and once they are more than the check-out interval apart the second
 * one is not a duplicate — it is a CHECK-OUT. Somebody who scanned twice at 08:50 and 09:05 because
 * the screen would not admit the first one has their working day closed at nine in the morning, and
 * is paid for fifteen minutes.
 *
 * It also stopped the app auto-opening the scanner on launch (that is gated on `kind === 'none'`),
 * which was pushing them toward exactly that second tap.
 *
 * Deliberately conservative: the queue only ever moves the day FORWARD by one step, and never all the
 * way to finished on its own. Two queued scans still read as "at work", because whether the second
 * one becomes a check-out depends on a server rule about how long after the first it was — and
 * guessing "done" would tell someone their day is closed when it may not be. Guessing "at work" only
 * ever costs an extra scan the server will decline.
 */
export function withPendingScans(
  state: TodayState,
  pending: PendingScan[],
  today: string = todayStr(),
): TodayState {
  const taps = pending
    .map((p) => p.clientTimestampUtc)
    .filter((t) => t.slice(0, 10) === today)
    .sort()
  if (taps.length === 0) return state

  if (state.kind === 'none') return { kind: 'in', checkIn: taps[0]!, pending: true }

  if (state.kind === 'in') {
    // A check-out taken offline: the server already has the check-in, so a tap AFTER it can only be
    // the way out. A tap before it is the check-in itself, still queued behind a reply that arrived
    // some other way — nothing to add.
    const out = taps.find((t) => t > state.checkIn)
    return out ? { kind: 'done', checkIn: state.checkIn, checkOut: out, pending: true } : state
  }

  return state
}


/**
 * A night shift that began yesterday evening and has not been closed yet.
 *
 * The home screen only ever looked at TODAY's row. For a night worker there is no such row at six in
 * the morning — their shift is still yesterday's — so the screen said «Giriş et · Hələ giriş
 * etməmisiniz» to somebody who had been at work for ten hours and was about to leave. The backend
 * was already right: a scan before noon on an overnight shift closes yesterday
 * (`AttendanceController`, the `IsOvernightOn(today-1) && hour < 12` branch). Only the words were
 * wrong, and the words are what decides whether the person taps.
 *
 * That mattered more than a wrong label usually does. Told they had not checked in, a night worker
 * reasonably does nothing — and the shift stays open, which is scored as zero hours for the night
 * they actually worked.
 *
 * The three conditions are the SERVER's, copied deliberately rather than approximated, so the card
 * never promises a check-out the scan will not perform:
 *   1. the shift really is overnight (it ends before it starts),
 *   2. it is before noon in COMPANY time — the same pivot the server uses,
 *   3. today has no row yet, and yesterday's is open.
 *
 * @param now  Injected so this is testable and so a phone left on the wrong timezone cannot change
 *             the answer — every comparison is made in company time.
 */
export function nightShiftState(
  records: AttendanceRecord[],
  shiftStart: string | null | undefined,
  shiftEnd: string | null | undefined,
  now: Date = new Date(),
): { kind: 'night'; checkIn: string } | null {
  if (!shiftStart || !shiftEnd) return null
  // "HH:mm" compares correctly as text, and this is the same test the server makes: End < Start.
  if (!(shiftEnd < shiftStart)) return null
  if (companyHour(now) >= 12) return null

  const today = companyDate(now)
  if (records.some((r) => r.attendanceDate === today && r.checkInAtUtc)) return null

  const last = records.find((r) => r.attendanceDate === dayBefore(today))
  if (!last?.checkInAtUtc || last.checkOutAtUtc) return null
  return { kind: 'night', checkIn: last.checkInAtUtc }
}
