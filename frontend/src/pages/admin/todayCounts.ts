/**
 * Bucketing the day's board — and the one distinction it keeps getting wrong.
 *
 * Every kind of approved absence reaches the client as the SAME status, `OnLeave`. Holiday, sick
 * leave, unpaid leave and a work trip are told apart only by `leaveType`, which rides along on the
 * row. Anything that counts by status alone therefore quietly merges them.
 *
 * It has happened twice. The reports counted Ezamiyyət as Məzuniyyət until 3d6ac7e; the today board
 * still did, so a manager opening it read «Məzuniyyət 14» on a morning when several of those
 * fourteen were out working. And that is the reason it matters more than a mislabel: a person on
 * ezamiyyət is AT WORK — away at a site with no poster to scan — and the board is where somebody
 * decides who is missing today.
 *
 * So the bucketing lives here, as a pure function with tests, rather than as a loop inside a 700-line
 * screen where the next person to add a leave type will not find it.
 */

/** Just the fields the buckets are decided from. */
export interface TodayLike {
  status: string
  leaveType?: string | null
  /** A «Sahədə» day is closed by the worker leaving the SITE, not by an office scan. */
  fieldCheckOutAtUtc?: string | null
}

export interface TodayCounts {
  present: number
  absent: number
  pending: number
  /** İmport olunub, hələ ilk skanı yoxdur — lövhəyə-özəl «Hazırlanır». Qayıba QARIŞMIR. */
  onboarding: number
  incomplete: number
  /** The roster's own day off — a Sunday, a rotation's off-day. Nobody decided it for this person. */
  dayOff: number
  /** An İstirahət somebody GRANTED. Its own number because it is a decision, and the whole reason a
   *  manager files one is to be able to see it again; merged into dayOff it was invisible among the
   *  two hundred people whose Sunday it simply was. */
  rest: number
  /** Məzuniyyət — annual leave ONLY. Not sick, not unpaid, not a work trip, not a rest day. */
  onLeave: number
  /** Ödənişsiz məzuniyyət, apart: it is not paid, and the dashboard already counted it separately —
   *  folding it into «Məzuniyyət» here made the two admin screens report different numbers for the
   *  same morning, and the list under the card showed rows badged «Ödənişsiz məzuniyyət». */
  unpaid: number
  sick: number
  /** Ezamiyyət. Its own bucket because these people are working. */
  trip: number
  permission: number
}

const EMPTY: TodayCounts = {
  present: 0, absent: 0, pending: 0, onboarding: 0, incomplete: 0,
  dayOff: 0, rest: 0, onLeave: 0, unpaid: 0, sick: 0, trip: 0, permission: 0,
}

export type TodayBucket = keyof TodayCounts

/**
 * Which column of the day this one person belongs in.
 *
 * Split out of the counting loop because the Excel export needs the SAME answer, row by row: the
 * workbook's summary sheet is built from these buckets rather than re-derived from the status text,
 * so the file and the board cannot disagree about how many people are on a work trip. One definition,
 * two readers — the alternative is the bug in the header comment, shipped a third time.
 */
export function bucketOf(r: TodayLike): TodayBucket {
  // «Sahədə» is a day like any other: finished once they have left, still running until they do.
  // It used to count as arrived-and-done whatever the visit was doing, so on 08.09 the summary sent
  // to the leadership reported eleven people who were standing on a site as «Tamamlayıb» — at
  // Stadion ətrafı all three of that branch's "completed" were still out.
  if (r.status === 'Field') return r.fieldCheckOutAtUtc ? 'present' : 'incomplete'
  if (r.status === 'OnTime' || r.status === 'Late') return 'present'
  if (r.status === 'Absent') return 'absent'
  if (r.status === 'Pending') return 'pending'
  if (r.status === 'Onboarding') return 'onboarding'
  // DayOff carries two facts under one status: the roster's own day off, and one a manager granted.
  // Only leaveType separates them, and it now travels for rest days too.
  if (r.status === 'DayOff') return r.leaveType === 'Rest' ? 'rest' : 'dayOff'
  if (r.status === 'OnLeave') {
    if (r.leaveType === 'Sick') return 'sick'
    if (r.leaveType === 'BusinessTrip') return 'trip'
    if (r.leaveType === 'Unpaid') return 'unpaid'
    return 'onLeave'
  }
  if (r.status === 'Permission') return 'permission'
  // Checked in with no check-out yet: "İşdə" on today's board, "Çıxış yoxdur" on a past date.
  return 'incomplete'
}

export function countToday(rows: TodayLike[]): TodayCounts {
  const c = { ...EMPTY }
  for (const r of rows) c[bucketOf(r)]++
  return c
}

/**
 * Does this row belong under that card?
 *
 * The three leave cards all select rows whose status is `OnLeave`, so they cannot be matched by
 * status — clicking Məzuniyyət has to exclude the sick and the travelling, or the list disagrees
 * with the number on the card the reader just pressed.
 */
export function matchesLeaveCard(row: TodayLike, card: 'sick' | 'trip' | 'onLeave' | 'unpaid'): boolean {
  if (row.status !== 'OnLeave') return false
  if (card === 'sick') return row.leaveType === 'Sick'
  if (card === 'trip') return row.leaveType === 'BusinessTrip'
  if (card === 'unpaid') return row.leaveType === 'Unpaid'
  // «Məzuniyyət» is now annual leave alone. Written as an exclusion rather than a test for
  // 'Vacation' on purpose: a row whose type never arrived must still land somewhere, and the card it
  // belongs under is the general one.
  return row.leaveType !== 'Sick' && row.leaveType !== 'BusinessTrip' && row.leaveType !== 'Unpaid'
}

/**
 * One ordering for the board.
 *
 * Azerbaijani collation on the text columns, so «Ə» and «İ» land where a reader expects rather than
 * after Z. Missing times sort LAST in both directions — a person with no check-in is not "earliest",
 * and burying them at the top of an ascending sort is how a board stops being read at all.
 */
/**
 * One ordering for the board.
 *
 * Azerbaijani collation on the text columns, so «Ə» and «İ» land where a reader expects rather than
 * after Z. Missing times sort LAST in both directions — a person with no check-in is not "earliest",
 * and burying them at the top of an ascending sort is how a board stops being read at all.
 */
export type SortColumn = 'name' | 'location' | 'position' | 'status' | 'in' | 'out'

export function sortRows<T extends {
  employeeName: string; locationName: string; position?: string | null
  status: string; checkInAtUtc?: string | null; checkOutAtUtc?: string | null
}>(rows: T[], by: SortColumn, desc: boolean): T[] {
  const dir = desc ? -1 : 1
  const text = (a: string, b: string) => a.localeCompare(b, 'az') * dir
  const time = (a?: string | null, b?: string | null) => {
    if (!a && !b) return 0
    if (!a) return 1          // absent rows to the bottom, whichever way the arrow points
    if (!b) return -1
    return (a < b ? -1 : a > b ? 1 : 0) * dir
  }
  return [...rows].sort((x, y) => {
    switch (by) {
      case 'location': return text(x.locationName, y.locationName) || text(x.employeeName, y.employeeName)
      case 'position': return text(x.position ?? '', y.position ?? '') || text(x.employeeName, y.employeeName)
      case 'status': return text(x.status, y.status) || text(x.employeeName, y.employeeName)
      case 'in': return time(x.checkInAtUtc, y.checkInAtUtc) || text(x.employeeName, y.employeeName)
      case 'out': return time(x.checkOutAtUtc, y.checkOutAtUtc) || text(x.employeeName, y.employeeName)
      default: return text(x.employeeName, y.employeeName)
    }
  })
}
