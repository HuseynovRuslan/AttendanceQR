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

/** Just the two fields the buckets are decided from. */
export interface TodayLike {
  status: string
  leaveType?: string | null
}

export interface TodayCounts {
  present: number
  absent: number
  pending: number
  /** İmport olunub, hələ ilk skanı yoxdur — lövhəyə-özəl «Hazırlanır». Qayıba QARIŞMIR. */
  onboarding: number
  incomplete: number
  dayOff: number
  /** Məzuniyyət — holiday and unpaid/rest leave. NOT sick, NOT a work trip. */
  onLeave: number
  sick: number
  /** Ezamiyyət. Its own bucket because these people are working. */
  trip: number
  permission: number
}

const EMPTY: TodayCounts = {
  present: 0, absent: 0, pending: 0, onboarding: 0, incomplete: 0,
  dayOff: 0, onLeave: 0, sick: 0, trip: 0, permission: 0,
}

export function countToday(rows: TodayLike[]): TodayCounts {
  const c = { ...EMPTY }
  for (const r of rows) {
    if (r.status === 'OnTime' || r.status === 'Late' || r.status === 'Field') c.present++
    else if (r.status === 'Absent') c.absent++
    else if (r.status === 'Pending') c.pending++
    else if (r.status === 'Onboarding') c.onboarding++
    else if (r.status === 'DayOff') c.dayOff++
    else if (r.status === 'OnLeave') {
      if (r.leaveType === 'Sick') c.sick++
      else if (r.leaveType === 'BusinessTrip') c.trip++
      else c.onLeave++
    }
    else if (r.status === 'Permission') c.permission++
    // Checked in with no check-out yet: "İşdə" on today's board, "Çıxış yoxdur" on a past date.
    else c.incomplete++
  }
  return c
}

/**
 * Does this row belong under that card?
 *
 * The three leave cards all select rows whose status is `OnLeave`, so they cannot be matched by
 * status — clicking Məzuniyyət has to exclude the sick and the travelling, or the list disagrees
 * with the number on the card the reader just pressed.
 */
export function matchesLeaveCard(row: TodayLike, card: 'sick' | 'trip' | 'onLeave'): boolean {
  if (row.status !== 'OnLeave') return false
  if (card === 'sick') return row.leaveType === 'Sick'
  if (card === 'trip') return row.leaveType === 'BusinessTrip'
  return row.leaveType !== 'Sick' && row.leaveType !== 'BusinessTrip'
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
