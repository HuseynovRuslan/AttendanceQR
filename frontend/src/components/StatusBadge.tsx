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
