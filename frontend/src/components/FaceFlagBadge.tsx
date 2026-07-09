/** Face-audit statuses that should be surfaced to a manager for review. */
export function faceIsFlagged(status?: string | null): boolean {
  return status === 'Mismatch' || status === 'MultiFace' || status === 'NoFace'
}

const META: Record<string, { label: string; cls: string; icon: string }> = {
  Ok: { label: 'Uyğun', cls: 'bg-green-100 text-green-700', icon: '✓' },
  Mismatch: { label: 'Uyğunsuz', cls: 'bg-red-100 text-red-700', icon: '⚠' },
  MultiFace: { label: 'Çoxlu üz', cls: 'bg-amber-100 text-amber-700', icon: '👥' },
  NoFace: { label: 'Üz yoxdur', cls: 'bg-amber-100 text-amber-700', icon: '⚠' },
  NoReference: { label: 'Referans yox', cls: 'bg-slate-100 text-slate-500', icon: '–' },
  Error: { label: 'Xəta', cls: 'bg-slate-100 text-slate-500', icon: '–' },
  NotChecked: { label: 'Yoxlanmayıb', cls: 'bg-slate-100 text-slate-400', icon: '–' },
}

/** Small pill showing the face-match verdict (+ score for Ok/Mismatch). */
export function FaceFlagBadge({ status, score }: { status?: string | null; score?: number | null }) {
  if (!status || status === 'NotChecked') return <span className="muted" style={{ fontSize: 12 }}>—</span>
  const m = META[status] ?? META.NotChecked
  const showScore = typeof score === 'number' && (status === 'Ok' || status === 'Mismatch')
  return (
    <span
      className={`inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-semibold ${m.cls}`}
      title={m.label}
    >
      <span>{m.icon}</span>
      {m.label}
      {showScore ? ` ${score}%` : ''}
    </span>
  )
}
