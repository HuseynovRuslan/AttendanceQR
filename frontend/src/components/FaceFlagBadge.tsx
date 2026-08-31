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

/**
 * What a face-match verdict says on screen, as data.
 *
 * Pulled out of the component so it can be tested: the suite runs in a node environment with no DOM,
 * which is the reason the rest of this codebase keeps its decisions in plain functions.
 *
 * @param compact  The today board. Icon and score only for a match, with the words in the tooltip.
 *
 * There the badge sits on every present row — «✓ Uyğun 100%» eighty times down a column whose whole
 * job is to be glanced at for the ONE row that is not a tick. A match needs no sentence. Anything
 * that is not a match keeps its words, because that is the row somebody has to read.
 */
export function faceBadge(
  status?: string | null,
  score?: number | null,
  compact = false,
): { icon: string; text: string; title: string; cls: string } | null {
  if (!status || status === 'NotChecked') return null
  const m = META[status] ?? META.NotChecked
  const showScore = typeof score === 'number' && (status === 'Ok' || status === 'Mismatch')
  const quiet = compact && status === 'Ok'
  const pct = showScore ? `${score}%` : ''
  return {
    icon: m.icon,
    text: quiet ? pct : (pct ? `${m.label} ${pct}` : m.label),
    title: m.label + (showScore ? ` — ${pct}` : ''),
    cls: m.cls,
  }
}

/** Small pill showing the face-match verdict (+ score for Ok/Mismatch). */
export function FaceFlagBadge(
  { status, score, compact = false }:
  { status?: string | null; score?: number | null; compact?: boolean },
) {
  const b = faceBadge(status, score, compact)
  // An unchecked row is a dash in a form, where the field is expected; nothing on the board, where a
  // whole column of dashes is what we were asked to clear away.
  if (!b) return compact ? null : <span className="muted" style={{ fontSize: 12 }}>—</span>
  return (
    <span
      className={`inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-semibold ${b.cls}`}
      title={b.title}
    >
      <span>{b.icon}</span>
      {b.text}
    </span>
  )
}
