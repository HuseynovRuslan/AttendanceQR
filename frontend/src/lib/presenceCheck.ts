import type { PresenceCheckRow, PresenceCheckView, PresenceResult } from '../api/presenceChecks'

/** How a result reads on the manager's history. «Cavab yoxdur» is a fact, not a verdict. */
export function presenceResultLabel(result: PresenceResult): { text: string; tone: 'ok' | 'bad' | 'muted' | 'warn' } {
  switch (result) {
    case 'Inside': return { text: 'Filialda', tone: 'ok' }
    case 'Outside': return { text: 'Filialdan kənarda', tone: 'bad' }
    case 'NoAnswer': return { text: 'Cavab yoxdur', tone: 'muted' }
    case 'GpsFailed': return { text: 'GPS alınmadı', tone: 'warn' }
    case 'Pending': return { text: 'Cavab gözlənilir', tone: 'muted' }
    default: return { text: 'Ləğv edildi', tone: 'muted' }
  }
}

/**
 * The distance part of a history row: «85 m (radius 150 m)», «1,2 km (radius 150 m)», nothing for no answer.
 * Kilometres past a thousand metres: Azerbaijani groups thousands with a dot, and «1.223 m» reads as one metre.
 */
export function presenceDistance(row: Pick<PresenceCheckRow, 'distanceMeters' | 'radiusMeters'>): string | null {
  if (row.distanceMeters == null || !Number.isFinite(row.distanceMeters)) return null
  const d = row.distanceMeters
  const text = d < 1000 ? `${Math.round(d)} m` : `${(d / 1000).toFixed(1).replace('.', ',')} km`
  return row.radiusMeters != null ? `${text} (radius ${row.radiusMeters} m)` : text
}

/** The last week in one line. Empty weeks say so rather than showing a row of zeros. */
export function presenceWeekLine(week: PresenceCheckView['week']): string {
  if (week.asked === 0) return 'Son 7 gündə yoxlama olmayıb.'
  const parts = [`${week.inside}-i filialda`]
  if (week.outside > 0) parts.push(`${week.outside}-i kənarda`)
  if (week.noAnswer > 0) parts.push(`${week.noAnswer}-i cavabsız`)
  if (week.gpsFailed > 0) parts.push(`${week.gpsFailed}-də GPS alınmayıb`)
  const late = week.late > 0 ? ` (${week.late} gec cavab)` : ''
  return `Son 7 gün: ${week.asked} yoxlamadan ${parts.join(', ')}${late}.`
}

/** Minutes left to answer on time, never below zero — after that the card still asks, without a countdown. */
export function minutesLeft(answerByUtc: string, now: number = Date.now()): number {
  const ms = new Date(answerByUtc).getTime() - now
  return Number.isFinite(ms) && ms > 0 ? Math.ceil(ms / 60_000) : 0
}
