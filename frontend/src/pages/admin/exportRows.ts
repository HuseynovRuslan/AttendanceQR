import { fmtTime, toCompanyInputValue } from '../../lib/format'
import { bucketOf } from './todayCounts'
import { formatWorked } from './todayShift'

/**
 * Turning one board row into one line of the workbook the leadership receives.
 *
 * This lived inside the board screen, where an audit found three ways it printed something the screen
 * beside it did not say. All three were about a time cell, and all three are the same mistake: a
 * spreadsheet cell has no tooltip, no badge and no context, so everything the board conveys by sitting
 * on a particular day has to be written INTO the cell or it is lost.
 */

/** The fields this needs from a board row — deliberately narrow, so a test can build one by hand. */
export interface ExportableRow {
  employeeName: string
  locationName: string
  position?: string | null
  /** «Sənəd üzrə» — the employer and site the documents name. `paperSite` decides which area this
   *  person falls under in the «sənəd üzrə» view; null means the paperwork agrees with the branch. */
  paperEmployer?: string | null
  paperSite?: string | null
  status: string
  leaveType?: string | null
  checkInAtUtc?: string | null
  checkOutAtUtc?: string | null
  fieldCheckInAtUtc?: string | null
  fieldCheckOutAtUtc?: string | null
  lateArrivalReason?: string | null
  earlyDepartureReason?: string | null
  wasOffline?: boolean
  hasPhoto?: boolean
  /** Each session, only on a day worked in more than one (the server sends nothing for an ordinary day). */
  blockSpans?: { inAtUtc: string | null; outAtUtc: string | null }[] | null
  /** The server's own count for the day — closed sessions only. See DayAttendanceRow.workedMinutes. */
  workedMinutes?: number | null
}

/**
 * A time cell, stamped with the day it belongs to when that is not the day being exported.
 *
 * Bare clock times cannot carry a night shift. A guard checks in at 20:06 and out at 05:21 — one
 * record, two calendar days — and the row reads «20:06 → 05:21», which anyone takes for a check-out
 * BEFORE the check-in. The carry-over is the same failure the other way round: until a night's window
 * closes, the board shows the previous evening's check-in on today's row, so a morning export printed
 * «21:33» as though the man had arrived at half nine this morning.
 *
 * @param base the exported day, "YYYY-MM-DD" in company time
 */
export function cellTime(iso: string | null | undefined, base: string): string {
  if (!iso) return ''          // empty, exactly as the board's own cell is — not a dash
  const clock = fmtTime(iso, '')
  const day = toCompanyInputValue(iso).slice(0, 10)
  if (day === base) return clock
  const delta = Math.round(
    (Date.parse(`${day}T00:00:00Z`) - Date.parse(`${base}T00:00:00Z`)) / 86_400_000)
  return `${clock} (${delta > 0 ? '+' : ''}${delta})`
}

/**
 * A «Sahədə» day has NO attendance record — its times live in the field-visit fields, which is why
 * the board's own cells read `?? fieldCheckInAtUtc`. The export did not, so a gardener who worked a
 * full day at an ad-hoc site exported as «Sahədə — —» while being counted under «Tamamlayıb».
 */
export const entryOf = (r: ExportableRow) => r.checkInAtUtc ?? r.fieldCheckInAtUtc

/** Sessions of a day worked in more than one, else null — the one test every caller below asks. */
const sessionsOf = (r: ExportableRow) => (r.blockSpans && r.blockSpans.length > 1 ? r.blockSpans : null)

/**
 * The day's departure: the LAST session's — and nothing while that session is still open.
 *
 * `checkOutAtUtc` belongs to the day's FIRST session, so a flexible worker who left at 12:00 and came back at
 * 13:00 exported «Çıxış 12:00» while standing at work, and one who finished at 18:00 still read «12:00». The
 * server's `lastCheckOutAtUtc` is null exactly when the last session is open, and falling back from it to
 * `checkOutAtUtc` brought the first session's exit straight back — so a day of several sessions reads its
 * last one and nothing else. The board's «Çıxış» cell uses this too, so the screen and the file agree.
 */
export function exitOf(r: ExportableRow): string | null | undefined {
  const sessions = sessionsOf(r)
  if (sessions) return sessions[sessions.length - 1].outAtUtc
  return r.checkOutAtUtc ?? r.fieldCheckOutAtUtc
}

/** Whether a session of this day has begun and not ended — at work today, a forgotten check-out on a past day. */
export function hasOpenSession(r: ExportableRow): boolean {
  const sessions = sessionsOf(r)
  if (sessions) return sessions.some((s) => !!s.inAtUtc && !s.outAtUtc)
  return !!entryOf(r) && !exitOf(r)
}

/**
 * «09:00–12:00, 13:00–18:00» — every session, only on a day worked in more than one. Each time is stamped with
 * its own day when that is not the exported one (`cellTime`), so a session across midnight reads «22:00–06:00
 * (+1)» and an open one reads «açıq».
 */
export function sessionsText(r: ExportableRow, date: string): string {
  const sessions = sessionsOf(r)
  if (!sessions) return ''
  return sessions
    .map((s) => `${cellTime(s.inAtUtc, date) || '—'}–${s.outAtUtc ? cellTime(s.outAtUtc, date) : 'açıq'}`)
    .join(', ')
}

/**
 * The «İşlənmiş» cell: the hours the SERVER counted (closed sessions; the break between sessions unpaid), and
 * «açıq» beside them while a session is still open — never an estimate of it. The same figure the tabel reads, so
 * the file and the tabel cannot disagree about a person's day. Empty when the day had no session at all, and when
 * an older server sends no figure (nothing is better than a second, different sum).
 */
export function workedText(r: ExportableRow): string {
  const open = hasOpenSession(r)
  const minutes = r.workedMinutes
  if (minutes == null) return ''
  if (minutes > 0) return open ? `${formatWorked(minutes)} + açıq` : formatWorked(minutes)
  if (open) return 'açıq'
  return entryOf(r) ? formatWorked(0) : ''
}

export interface ExportRowOut {
  name: string
  position: string
  location: string
  status: string
  checkIn: string
  checkOut: string
  photo: string
  bucket: string
  paper: string
  /** «08:00» / «03:05 + açıq» / «açıq» — see workedText. */
  worked: string
  /** «09:00–12:00, 13:00–18:00» on a day of several sessions, else empty. */
  sessions: string
}

/**
 * @param statusLabel the board's own label for this row — passed in rather than derived, because
 * Məzuniyyət, Xəstəlik and Ezamiyyət all arrive as one status and only the screen tells them apart.
 */
export function exportRow(
  r: ExportableRow, date: string, statusLabel: string, view: AreaView = 'actual',
): ExportRowOut {
  return {
    name: r.employeeName,
    position: r.position ?? '',
    // The workbook groups by whatever Location it is handed — both the «Xülasə» lines and the
    // «Davamiyyət» banners — so sending the chosen view here re-shapes the whole file, and the
    // server needs to know nothing about the distinction.
    location: areaOf(r, view),
    status: statusLabel,
    // «oflayn» rides along because the board flags it: that time is the phone's clock, not the
    // server's, and a reader of the file has no other way to know.
    checkIn: cellTime(entryOf(r), date)
      + (r.wasOffline ? ' (oflayn)' : '')
      + (r.lateArrivalReason ? ` (gec: ${r.lateArrivalReason})` : ''),
    checkOut: cellTime(exitOf(r), date)
      + (r.earlyDepartureReason ? ` (tez: ${r.earlyDepartureReason})` : ''),
    // A field day carries no selfie by design (the field routes send no photo at all), so «yox»
    // there would read as a missing one rather than one that was never asked for.
    photo: r.hasPhoto ? 'var' : r.checkInAtUtc ? 'yox' : '—',
    bucket: bucketOf(r),
    // Blank for nearly everyone, and deliberately blank rather than a dash: the column has to stay
    // quiet on the many so the eye lands on the few whose paperwork names another company.
    paper: r.paperEmployer
      ? `${r.paperEmployer}${r.paperSite ? ` / ${r.paperSite}` : ''}`
      : '',
    worked: workedText(r),
    sessions: sessionsText(r, date),
  }
}

/**
 * Which area a row belongs to, in the view the reader chose.
 *
 * «Faktiki» is where the person stands. «Sənəd üzrə» is what their paperwork names — and falls back
 * to the branch when nobody wrote one, because for almost everybody the two agree. That fallback is
 * the whole rule: `paperSite ?? locationName`, written once, so the checkbox list, the filter, the
 * summary lines and the detail groups can never mean four different things by «ərazi».
 *
 * The same expression also decides the workbook's own grouping — the sheet groups by whatever
 * Location it is handed — so choosing the view here re-shapes the file with nothing to keep in sync
 * on the server.
 */
export type AreaView = 'actual' | 'paper'

export function areaOf(
  r: { locationName: string; paperSite?: string | null },
  view: AreaView,
): string {
  return view === 'paper' ? (r.paperSite || r.locationName) : r.locationName
}

/**
 * Whether two area names are the same area, the way the server matches a company form's lines:
 * trimmed, case folded the Azerbaijani way, letters kept apart («ı» is not «i», «ə» is not «e»). The
 * dialog asks the same question the server will, so it can never offer a choice the server refuses.
 */
export function sameArea(a: string, b: string): boolean {
  return a.trim().localeCompare(b.trim(), 'az', { sensitivity: 'accent' }) === 0
}

/** The chosen areas a company form has no line for — the ones the server would refuse. Empty means
 *  the choice fits the form. */
export function unlistedAreas(chosen: readonly string[], formAreas: readonly string[]): string[] {
  return chosen.filter((a) => !formAreas.some((f) => sameArea(f, a)))
}

/** Every area present in a set of rows, in the chosen view — the export dialog's tick list, and what
 *  "all of them" means when the view changes. */
export function uniqueAreas(
  rows: readonly { locationName: string; paperSite?: string | null }[],
  view: AreaView,
): string[] {
  return [...new Set(rows.map((r) => areaOf(r, view)))].sort((a, b) => a.localeCompare(b, 'az'))
}
