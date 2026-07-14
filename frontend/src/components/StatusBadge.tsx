import type { AttendanceRecord } from '../api/attendance'
import { todayStr } from '../lib/att'
import { IconCalendar, IconCheck, IconClock, IconX } from './icons'

// Single source of truth for status → class/label/icon, so the same status always reads the
// same everywhere (badges, stat-card headers, anywhere else) — never hardcode these strings
// separately elsewhere.
export const STATUS_MAP: Record<string, { cls: string; label: string; icon: 'check' | 'clock' | 'x' | 'calendar' }> = {
  OnTime: { cls: 'b-present', label: 'Gəlib', icon: 'check' },
  // Rendered exactly like OnTime, on purpose. Every employee keeps their own hours, so a single
  // location-wide shift makes "Gecikmə" a wrong label rather than a useful one. The backend still
  // records the status — once per-employee schedules exist, this becomes meaningful again.
  Late: { cls: 'b-present', label: 'Gəlib', icon: 'check' },
  Absent: { cls: 'b-absent', label: 'Qayıb', icon: 'x' },
  Incomplete: { cls: 'b-permitted', label: 'İşdə', icon: 'clock' },
  DayOff: { cls: 'b-sick', label: 'İstirahət', icon: 'calendar' },
  OnLeave: { cls: 'b-leave', label: 'Məzuniyyət', icon: 'calendar' },
  Permission: { cls: 'b-permission', label: 'İcazə', icon: 'check' },
}

export function statusLabel(status: string): string {
  return STATUS_MAP[status]?.label ?? status
}

export function StatusBadge({ status }: { status: string }) {
  const m = STATUS_MAP[status] ?? { cls: 'b-absent', label: status, icon: 'x' as const }
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
