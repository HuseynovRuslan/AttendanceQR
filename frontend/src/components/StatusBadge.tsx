import { IconCheck, IconClock, IconX } from './icons'

// Maps the backend status to the design's badge class, label, and icon.
const MAP: Record<string, { cls: string; label: string; icon: 'check' | 'clock' | 'x' }> = {
  OnTime: { cls: 'b-present', label: 'Davamda', icon: 'check' },
  Late: { cls: 'b-late', label: 'Gecikmə', icon: 'clock' },
  Absent: { cls: 'b-absent', label: 'Qayıb', icon: 'x' },
  Incomplete: { cls: 'b-permitted', label: 'Yarımçıq', icon: 'clock' },
}

export function StatusBadge({ status }: { status: string }) {
  const m = MAP[status] ?? { cls: 'b-absent', label: status, icon: 'x' as const }
  const Icon = m.icon === 'check' ? IconCheck : m.icon === 'clock' ? IconClock : IconX
  return (
    <span className={`badge ${m.cls}`}>
      <Icon />
      {m.label}
    </span>
  )
}
