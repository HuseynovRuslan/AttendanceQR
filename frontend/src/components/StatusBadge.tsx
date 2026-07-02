const STYLES: Record<string, string> = {
  OnTime: 'bg-green-100 text-green-800',
  Late: 'bg-yellow-100 text-yellow-800',
  Absent: 'bg-red-100 text-red-800',
  Incomplete: 'bg-slate-200 text-slate-700',
}

const LABELS: Record<string, string> = {
  OnTime: 'Vaxtında',
  Late: 'Gecikmə',
  Absent: 'Qaib',
  Incomplete: 'Yarımçıq',
}

export function StatusBadge({ status }: { status: string }) {
  return (
    <span className={`inline-block px-2 py-0.5 rounded-full text-xs font-medium ${STYLES[status] ?? 'bg-slate-200 text-slate-700'}`}>
      {LABELS[status] ?? status}
    </span>
  )
}
