import { fromCompanyInputValue } from './format'

/**
 * The two instants a hand-written attendance day is saved with.
 *
 * A record is stamped with the day its shift BEGINS, and a night shift ends the next morning — so on
 * an overnight day the check-out's calendar date is NOT the record's own date. The form asks for one
 * date and two clock times, which is how anybody describes a shift («beşinci, doqquzdan altıya»), and
 * this is where «altıya» becomes six o'clock the NEXT morning.
 *
 * Without it the form built both instants from the record's date, so a night — in at 21:00, out at
 * 06:51 — produced a check-out fourteen hours BEFORE the check-in. The server refused it
 * (`CheckOutBeforeCheckIn`) and the screen said only «Qeyd yaradılmadı», which meant the one day that
 * most needs writing by hand — a night nobody scanned at all — was the one day that could not be
 * written. A manager asked how to record it; there was no way to.
 */
export type ManualRecordTimes = {
  /** The record's date: always the day the shift began, which is what the form asked for. */
  date: string
  /** The calendar date the check-out falls on — the same day, or the next when it crosses midnight. */
  outDate: string
  /** True when the check-out belongs to the following morning. */
  overnight: boolean
  checkInIso: string
  checkOutIso?: string
}

/**
 * @param date  the record's date, "YYYY-MM-DD" — the day the shift began
 * @param inTime  "HH:mm" on that date
 * @param outTime  "HH:mm"; a time at or before the check-in is read as the next morning
 */
export function manualRecordTimes(date: string, inTime: string, outTime?: string): ManualRecordTimes | null {
  if (!date || !inTime) return null

  // A check-out no later on the clock than the check-in can only mean midnight was crossed. Equal
  // times land here too and give a 24-hour day: absurd, but so is a zero-minute one, and the form
  // shows the pair it is about to save so either is caught by the eye that typed it.
  const overnight = !!outTime && outTime <= inTime
  const outDate = overnight ? nextDay(date) : date

  return {
    date,
    outDate,
    overnight,
    checkInIso: fromCompanyInputValue(`${date}T${inTime}`),
    checkOutIso: outTime ? fromCompanyInputValue(`${outDate}T${outTime}`) : undefined,
  }
}

/** Calendar arithmetic only — never through the local zone, which would move the day abroad. */
function nextDay(date: string): string {
  const [y, m, d] = date.split('-').map(Number)
  const t = new Date(Date.UTC(y, m - 1, d + 1)) // Date.UTC rolls the month and year over for us
  return `${t.getUTCFullYear()}-${pad(t.getUTCMonth() + 1)}-${pad(t.getUTCDate())}`
}

const pad = (n: number) => String(n).padStart(2, '0')
