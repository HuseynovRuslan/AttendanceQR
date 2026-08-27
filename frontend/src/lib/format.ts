// Every time/date/duration string the UI shows, in the one place people will look for it.
//
// fmtTime and fmtDate already existed in lib/att.ts and were duplicated locally in fifteen files
// anyway — nobody hunting for a time formatter thinks to open a module called "att". They live here
// now, under a name that says what they are; att.ts keeps the attendance-domain helpers.
//
// Two things are deliberately kept apart even though they read alike:
//   • a CALENDAR DATE ("2026-07-16", an AttendanceDate) is split as text — never through Date(), which
//     would drag it through a timezone and can land on the day before.
//   • an INSTANT (an ISO timestamp like CheckInAtUtc) is rendered in the COMPANY's timezone.
//
// That second rule used to say "the viewer's local time", and it cost a day's wages to find out why
// that was wrong. A worker at Dədə Qorqud left at 19:28 and the board showed 18:28: the record was
// right, the phone reading it was on UTC+3, and every time on every screen of that device was an hour
// early. Nobody suspects the phone — they suspect the system, and the argument that follows is about
// somebody's pay.
//
// A shift that started at 07:58 in Baku started at 07:58 for everyone looking at it: the manager
// abroad, the kiosk in the yard, the phone whose clock was set by hand. The timezone of the device is
// not a fact about the shift, so it does not get to change what the shift says.

/**
 * The company's timezone — everything this system records happens in it.
 *
 * MUST match the backend's `App:TimeZone` (appsettings.json, "Asia/Baku"), which decides shift starts,
 * day boundaries and every summary. If that option is ever changed, change this with it: the two
 * disagreeing means the board and the payroll disagree.
 */
export const COMPANY_TZ = 'Asia/Baku'

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
  return new Date(iso).toLocaleTimeString('az-AZ', { hour: '2-digit', minute: '2-digit', timeZone: COMPANY_TZ })
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

/** An instant → "DD.MM HH:mm" in the company's timezone. */
export function fmtDateTime(iso: string): string {
  return new Date(iso).toLocaleString('az-AZ', {
    day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit', timeZone: COMPANY_TZ,
  })
}

/** An instant → "DD.MM.YYYY HH:mm" in the company's timezone — the long form for audit-style lists. */
export function fmtFullDateTime(iso: string): string {
  return new Date(iso).toLocaleString('az-AZ', {
    day: '2-digit', month: '2-digit', year: 'numeric',
    hour: '2-digit', minute: '2-digit', timeZone: COMPANY_TZ,
  })
}

/** An instant → "DD.MM.YYYY" in the company's timezone. Not the same as fmtDate: this one converts. */
export function fmtDateOfInstant(iso: string): string {
  return new Date(iso).toLocaleDateString('az-AZ', {
    day: '2-digit', month: '2-digit', year: 'numeric', timeZone: COMPANY_TZ,
  })
}

// --- editing an instant ------------------------------------------------------------------------
// An <input type="datetime-local"> speaks the DEVICE's wall clock, in both directions. Filling one
// with a device-local rendering and reading it straight back is lossless only while nobody touches
// it — and the moment an admin on a misconfigured phone "corrects" a time that merely LOOKED wrong,
// they write their device's hour into somebody's attendance record. These convert through the
// company's timezone instead, so the box shows what the board shows and saves what it shows.

/** How far ahead of UTC the company's timezone is, at a given instant (ms). */
function companyOffsetMs(at: Date): number {
  const parts = new Intl.DateTimeFormat('en-US', {
    timeZone: COMPANY_TZ, hour12: false,
    year: 'numeric', month: '2-digit', day: '2-digit',
    hour: '2-digit', minute: '2-digit', second: '2-digit',
  }).formatToParts(at)
  const p: Record<string, string> = {}
  for (const part of parts) p[part.type] = part.value
  // hour12:false yields "24" for midnight in some engines — % 24 keeps it a real hour.
  const asIfUtc = Date.UTC(
    Number(p.year), Number(p.month) - 1, Number(p.day),
    Number(p.hour) % 24, Number(p.minute), Number(p.second),
  )
  return asIfUtc - at.getTime()
}

const pad2 = (n: number) => String(n).padStart(2, '0')

/** An instant → "YYYY-MM-DDTHH:mm" as the company's wall clock, for a datetime-local input. */
export function toCompanyInputValue(iso: string): string {
  const at = new Date(iso)
  const shifted = new Date(at.getTime() + companyOffsetMs(at))
  return `${shifted.getUTCFullYear()}-${pad2(shifted.getUTCMonth() + 1)}-${pad2(shifted.getUTCDate())}`
    + `T${pad2(shifted.getUTCHours())}:${pad2(shifted.getUTCMinutes())}`
}

/** "YYYY-MM-DDTHH:mm" read as the company's wall clock → an ISO instant. */
export function fromCompanyInputValue(value: string): string {
  const [datePart, timePart = '00:00'] = value.split('T')
  const [y, m, d] = datePart.split('-').map(Number)
  const [hh, mm] = timePart.split(':').map(Number)
  const guess = Date.UTC(y, m - 1, d, hh, mm)
  // Two passes: the offset must be measured AT the instant, and the first guess is off by exactly
  // that offset. The second correction settles it for any zone that is not mid-transition.
  const once = guess - companyOffsetMs(new Date(guess))
  return new Date(guess - companyOffsetMs(new Date(once))).toISOString()
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
