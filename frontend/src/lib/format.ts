// Every time/date/duration string the UI shows, in the one place people will look for it.
//
// fmtTime and fmtDate already existed in lib/att.ts and were duplicated locally in fifteen files
// anyway — nobody hunting for a time formatter thinks to open a module called "att". They live here
// now, under a name that says what they are; att.ts keeps the attendance-domain helpers.
//
// Two things are deliberately kept apart even though they read alike:
//   • a CALENDAR DATE ("2026-07-16", an AttendanceDate) is split as text — never through Date(), which
//     would drag it through a timezone and can land on the day before.
//   • an INSTANT (an ISO timestamp like CheckInAtUtc) is converted to the viewer's local time, which
//     is the whole point of showing it.

const AZ_MONTHS = [
  'yanvar', 'fevral', 'mart', 'aprel', 'may', 'iyun',
  'iyul', 'avqust', 'sentyabr', 'oktyabr', 'noyabr', 'dekabr',
]

/**
 * An instant → local "HH:mm".
 * @param fallback what to show when there is no value — "—" reads as "nothing here" in a table,
 * but an empty string is right where the surrounding text already says it (see ScanPage).
 */
export function fmtTime(iso: string | null | undefined, fallback = '—'): string {
  if (!iso) return fallback
  return new Date(iso).toLocaleTimeString('az-AZ', { hour: '2-digit', minute: '2-digit' })
}

/** A calendar date "YYYY-MM-DD" → "DD.MM.YYYY". Text only — see the note above. */
export function fmtDate(dateOnly: string): string {
  const [y, m, d] = dateOnly.split('-')
  return `${d}.${m}.${y}`
}

/** A calendar date → "DD.MM" — for axis labels, where the year is noise. */
export function fmtShortDate(dateOnly: string): string {
  const [, m, d] = dateOnly.split('-')
  return `${d}.${m}`
}

/** A calendar date → "5 iyul". Reads as prose, for a sentence rather than a table. */
export function fmtDayMonth(dateOnly: string): string {
  const d = new Date(`${dateOnly}T00:00:00`)
  return `${d.getDate()} ${AZ_MONTHS[d.getMonth()] ?? ''}`
}

/** An instant → local "DD.MM HH:mm". */
export function fmtDateTime(iso: string): string {
  return new Date(iso).toLocaleString('az-AZ', {
    day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit',
  })
}

/** An instant → local "DD.MM.YYYY". Not the same as fmtDate: this one shifts by timezone. */
export function fmtDateOfInstant(iso: string): string {
  return new Date(iso).toLocaleDateString('az-AZ', { day: '2-digit', month: '2-digit', year: 'numeric' })
}

export function minutesBetween(startIso: string, endIso: string): number {
  return Math.round((new Date(endIso).getTime() - new Date(startIso).getTime()) / 60_000)
}

/** Two instants → "8 saat 30 dəqiqə". Clamped: a negative span is bad data, not "-1 saat". */
export function fmtDuration(startIso: string, endIso: string): string {
  const m = Math.max(0, minutesBetween(startIso, endIso))
  return `${Math.floor(m / 60)} saat ${m % 60} dəqiqə`
}

/**
 * Decimal hours (as the reports API returns them) → "8 saat 30 dəq".
 * The reason this exists: 0.32 hours rendered as "0.32 saat" was read as "32 minutes" — it is 19.
 */
export function fmtHM(hours: number): string {
  const totalMin = Math.round((hours || 0) * 60)
  if (totalMin === 0) return '—'
  const h = Math.floor(totalMin / 60)
  const m = totalMin % 60
  if (h === 0) return `${m} dəq`
  return m === 0 ? `${h} saat` : `${h} saat ${m} dəq`
}
