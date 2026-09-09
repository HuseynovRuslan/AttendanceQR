import type { AttendanceRecord } from '../api/attendance'
import { todayStr } from '../lib/att'
import { IconCalendar, IconCheck, IconClock, IconX } from './icons'

// Single source of truth for status → class/label/icon, so the same status always reads the
// same everywhere (badges, stat-card headers, anywhere else) — never hardcode these strings
// separately elsewhere.
export const STATUS_MAP: Record<string, { cls: string; label: string; icon: 'check' | 'clock' | 'x' | 'calendar' }> = {
  // "Tamamlayıb" (not "Gəlib"): OnTime/Late only ever apply once BOTH check-in and check-out exist
  // (see AttendanceCalculator.Compute), so this means "checked in and out — done for the day". Calling
  // it "Gəlib" (came) falsely implied that someone still at work (Incomplete/"İşdə") hadn't come at all.
  OnTime: { cls: 'b-present', label: 'Tamamlayıb', icon: 'check' },
  Late: { cls: 'b-present', label: 'Tamamlayıb', icon: 'check' },
  Absent: { cls: 'b-absent', label: 'Qayıb', icon: 'x' },
  // Board-only: a scheduled worker whose shift hasn't started yet. Neutral, NOT the red Qayıb — a
  // 21:00 night worker at 10:00 is not a no-show, their shift is later. Set by the live board; the
  // stored DailySummaryStatus never carries it.
  // «Növbəsi başlamayıb», not «Gözlənilir». The day is not late and nobody is waiting on this
  // person: their shift has simply not begun yet — a night guard at nine in the morning. «Gözlənilir»
  // sat next to «Qayıb» on the board and read as the first stage of being absent.
  Pending: { cls: 'b-pending', label: 'Növbəsi başlamayıb', icon: 'clock' },
  // Board-only, like Pending: imported but no first scan yet — the phone is still being handed over,
  // permissions granted, the shift assigned. NOT the red Qayıb: 290 of these drowned the 67 real
  // no-shows the morning a company onboarded. The stored DailySummaryStatus never carries it, and the
  // payroll never sees such a day at all (DailySummaryService skips it).
  Onboarding: { cls: 'b-pending', label: 'Aktivləşdirməyib', icon: 'clock' },
  // Checked in, no check-out yet. On a live "today" view this just means "still at work" — correct.
  // On a PAST day it means a check-out was never recorded (a real problem) — callers viewing a past
  // date should override this via StatusBadge's `override` prop (see TodayPage.tsx).
  Incomplete: { cls: 'b-permitted', label: 'İşdə', icon: 'clock' },
  // «Həftəlik istirahət», not a bare «İstirahət»: this is the ROSTER's own day off — a Sunday, a
  // rotation's off-day — and it used to render identically to a rest day a manager granted, which
  // made the granted one impossible to find again. The granted one is leaveVisual('Rest') below.
  DayOff: { cls: 'b-sick', label: 'Həftəlik istirahət', icon: 'calendar' },
  OnLeave: { cls: 'b-leave', label: 'Məzuniyyət', icon: 'calendar' },
  Permission: { cls: 'b-permission', label: 'İcazə', icon: 'check' },
  // Board-only: checked in from an ad-hoc field site (no office scan) — present, out in the field.
  Field: { cls: 'b-trip', label: 'Sahədə', icon: 'check' },
}

export function statusLabel(status: string): string {
  return STATUS_MAP[status]?.label ?? status
}

/**
 * THE one place a day becomes a word — status plus leave type, together.
 *
 * Every screen used to answer this for itself, so «fix it here and it comes back over there» was the
 * product's normal behaviour: the board read the type, the export did not, the profile had no type to
 * read at all, and a rest day appeared as «Məzuniyyət» in one place and vanished in another. Reading
 * the status alone cannot work — four entitlements share `OnLeave`, and `DayOff` covers both the
 * roster's own day off and one a manager granted. Both distinctions live here, once.
 *
 * @param incompleteLabel what «Incomplete» means in THIS context — «İşdə» on today's board, «Çıxış
 * yoxdur» on a past date. The only thing a caller may still decide.
 */
export function dayLabel(status: string, leaveType?: string | null, incompleteLabel?: string): string {
  if (status === 'Incomplete' && incompleteLabel) return incompleteLabel
  return dayVisual(status, leaveType)?.label ?? status
}

/** The same answer as {@link dayLabel}, with the colour — for anything that draws a badge. */
export function dayVisual(status: string, leaveType?: string | null): StatusVisual | undefined {
  // A leave type is only ever meaningful for the two statuses a leave record can produce. Anywhere
  // else it is stale data riding along on a row, and honouring it would badge a worked day as leave.
  if (status === 'OnLeave' || status === 'DayOff') {
    const v = leaveVisual(leaveType)
    if (v) return v
  }
  return STATUS_MAP[status]
}

export type StatusVisual = { cls: string; label: string; icon: 'check' | 'clock' | 'x' | 'calendar' }

// An "OnLeave" row is one of several kinds of leave, and the stored status collapses them all to one
// value — so the badge must read the row's LeaveType to say which. Without this, every leave (sick,
// unpaid, …) shows as "Məzuniyyət", which is simply wrong for anyone marked Xəstəlik. One place,
// reused by every board that shows a leave row.
export function leaveVisual(leaveType?: string | null): StatusVisual | undefined {
  switch (leaveType) {
    case 'Vacation':
      return { cls: 'b-leave', label: 'Məzuniyyət', icon: 'calendar' }
    case 'Sick':
      return { cls: 'b-sick', label: 'Xəstəlik', icon: 'calendar' }
    case 'Unpaid':
      return { cls: 'b-leave', label: 'Ödənişsiz məzuniyyət', icon: 'calendar' }
    case 'Rest':
      // Deliberately distinct from STATUS_MAP.DayOff, which it used to duplicate exactly. A rest day
      // somebody granted is a decision; a weekend is the calendar. It is still NOT məzuniyyət and
      // keeps its own colour family, away from the blue leave badges.
      return { cls: 'b-permitted', label: 'İstirahət (təyin edilmiş)', icon: 'calendar' }
    case 'BusinessTrip':
      return { cls: 'b-trip', label: 'Ezamiyyət', icon: 'calendar' }
    case 'Permission':
      return { cls: 'b-permission', label: 'İcazə', icon: 'check' }
    default:
      return undefined // unknown/missing → caller falls back to the plain status visual
  }
}

/** `override` lets a caller replace the looked-up visual for one specific status in one context —
 *  e.g. TodayPage shows "Incomplete" as "Çıxış yoxdur" (not "İşdə") when viewing a past date. */
export function StatusBadge({ status, override }: { status: string; override?: StatusVisual }) {
  const m = override ?? STATUS_MAP[status] ?? { cls: 'b-absent', label: status, icon: 'x' as const }
  const Icon = m.icon === 'check' ? IconCheck : m.icon === 'clock' ? IconClock : m.icon === 'calendar' ? IconCalendar : IconX
  return (
    <span className={`badge ${m.cls}`}>
      <Icon />
      {m.label}
    </span>
  )
}

/**
 * Completion-state badge for one record row (Home / history) — distinct from the OnTime/Late status,
 * and from each other regardless of the tenant accent (fixed colours):
 *   • check-in + check-out            → green  "Tamamlandı"
 *   • check-in, no check-out, today   → blue   "İşdə" (still at work)
 *   • check-in, no check-out, past    → red    "Çıxış yoxdur"
 *   • no check-in                     → the record's own status (Qayıb, İstirahət, …)
 */
export function RecordBadge({ r }: { r: AttendanceRecord }) {
  if (r.checkInAtUtc && r.checkOutAtUtc)
    return (
      <span className="badge" style={{ background: '#E7F6EC', color: '#1B7F3B' }}>
        <IconCheck />
        Tamamlandı
      </span>
    )
  if (r.checkInAtUtc && !r.checkOutAtUtc)
    return r.attendanceDate < todayStr() ? (
      <span className="badge" style={{ background: '#FBEAE7', color: '#C2410C' }}>
        <IconX />
        Çıxış yoxdur
      </span>
    ) : (
      <span className="badge" style={{ background: '#EAF1FE', color: '#2563EB' }}>
        <IconClock />
        İşdə
      </span>
    )
  return <StatusBadge status={r.status} />
}
