import { IconCheck, IconClock, IconX } from './icons'

// Single source of truth for status → class/label/icon, so the same status always reads the
// same everywhere (badges, stat-card headers, anywhere else) — never hardcode these strings
// separately elsewhere.
export const STATUS_MAP: Record<string, { cls: string; label: string; icon: 'check' | 'clock' | 'x' }> = {
  OnTime: { cls: 'b-present', label: 'Gəlib', icon: 'check' },
  Late: { cls: 'b-late', label: 'Gecikmə', icon: 'clock' },
  Absent: { cls: 'b-absent', label: 'Qayıb', icon: 'x' },
  Incomplete: { cls: 'b-permitted', label: 'İşdə', icon: 'clock' },
}

export function statusLabel(status: string): string {
  return STATUS_MAP[status]?.label ?? status
}

export function StatusBadge({ status }: { status: string }) {
  const m = STATUS_MAP[status] ?? { cls: 'b-absent', label: status, icon: 'x' as const }
  const Icon = m.icon === 'check' ? IconCheck : m.icon === 'clock' ? IconClock : IconX
  return (
    <span className={`badge ${m.cls}`}>
      <Icon />
      {m.label}
    </span>
  )
}
