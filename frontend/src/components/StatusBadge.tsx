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
  // Checked in, no check-out yet. On a live "today" view this just means "still at work" — correct.
  // On a PAST day it means a check-out was never recorded (a real problem) — callers viewing a past
  // date should override this via StatusBadge's `override` prop (see TodayPage.tsx).
  Incomplete: { cls: 'b-permitted', label: 'İşdə', icon: 'clock' },
  DayOff: { cls: 'b-sick', label: 'İstirahət', icon: 'calendar' },
  OnLeave: { cls: 'b-leave', label: 'Məzuniyyət', icon: 'calendar' },
  Permission: { cls: 'b-permission', label: 'İcazə', icon: 'check' },
}

export function statusLabel(status: string): string {
  return STATUS_MAP[status]?.label ?? status
}

type StatusVisual = { cls: string; label: string; icon: 'check' | 'clock' | 'x' | 'calendar' }

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
