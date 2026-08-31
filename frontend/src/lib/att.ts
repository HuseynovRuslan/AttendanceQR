import type { AttendanceRecord } from '../api/attendance'

// Attendance-domain helpers. The time/date formatters that used to live here moved to lib/format.ts,
// where someone looking for a time formatter can actually find them.

export function todayStr(): string {
  return new Date().toISOString().slice(0, 10)
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
