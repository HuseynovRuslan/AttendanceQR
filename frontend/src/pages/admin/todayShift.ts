/**
 * The two columns the redesigned board added — the hours somebody owes, and the hours they have put
 * in so far.
 *
 * Both are pure functions with tests rather than expressions inside the screen, for the same reason
 * `todayCounts` is: a number on this board is read as a fact about a person's pay, and the two cases
 * that decide it — a split day, and a day that was never closed — are exactly the ones nobody thinks
 * of while writing JSX.
 */

/** Just the fields these two answers are decided from. */
export interface ShiftLike {
  shiftStart?: string | null
  shiftEnd?: string | null
  shiftName?: string | null
  secondShiftStart?: string | null
  secondShiftEnd?: string | null
}

export interface WorkedLike {
  checkInAtUtc?: string | null
  checkOutAtUtc?: string | null
  /** On a split day the NIGHT's departure, not the morning block's. */
  lastCheckOutAtUtc?: string | null
  blockSpans?: { inAtUtc: string | null; outAtUtc: string | null }[] | null
  fieldCheckInAtUtc?: string | null
  fieldCheckOutAtUtc?: string | null
}

/**
 * The window this person is measured against, as one line.
 *
 * A split day prints BOTH stretches. The crew on «əlavə qüvvə» work 07:00–11:00 and then 22:00–07:00,
 * and a column that showed the morning alone would say four hours on the one kind of day where the
 * schedule is the thing somebody opened the board to check.
 */
export function shiftHours(row: ShiftLike): string | null {
  if (!row.shiftStart || !row.shiftEnd) return null
  const first = `${row.shiftStart}–${row.shiftEnd}`
  return row.secondShiftStart && row.secondShiftEnd
    ? `${first} + ${row.secondShiftStart}–${row.secondShiftEnd}`
    : first
}

/** The fuller sentence, for the cell's tooltip: the named shift, when the hours came from one. */
export function shiftTitle(row: ShiftLike): string | undefined {
  const hours = shiftHours(row)
  if (!hours) return undefined
  return row.shiftName ? `${row.shiftName} · ${hours}` : hours
}

/**
 * Minutes worked on this day so far — or null when the question has no answer.
 *
 * Three rules, and each of them is a decision this product already made elsewhere:
 *
 *  • A split day is the SUM of its stretches, never last-departure minus first-arrival. The hours
 *    between the two blocks are not paid (`WorkSpan.RosteredReturn`), and a row reading «07:00 →
 *    07:00» as twenty-four hours would be a day's wage invented out of a subtraction.
 *  • A day still running counts up to NOW — but only on today's board. That is what makes the column
 *    live, and what makes the morning's «who has been here four hours» answerable at all.
 *  • A past day that was never closed is NOT estimated. It is zero hours, by a decision taken with
 *    the numbers in hand on 2026-08-11 and declined again since; guessing a departure here would
 *    quietly pay 165 unclosed days at whatever this function felt like.
 */
export function workedMinutes(row: WorkedLike, nowMs: number, isToday: boolean): number | null {
  const at = (s?: string | null) => (s ? Date.parse(s) : NaN)
  const span = (inAt?: string | null, outAt?: string | null): number | null => {
    const a = at(inAt)
    if (Number.isNaN(a)) return null
    const b = at(outAt)
    if (!Number.isNaN(b)) return Math.max(0, Math.round((b - a) / 60000))
    // Open. Running only while the day is today; a past day that was never closed stays zero.
    return isToday ? Math.max(0, Math.round((nowMs - a) / 60000)) : null
  }

  if (row.blockSpans && row.blockSpans.length > 0) {
    let total = 0
    let any = false
    for (const b of row.blockSpans) {
      const m = span(b.inAtUtc, b.outAtUtc)
      if (m !== null) { total += m; any = true }
    }
    return any ? total : null
  }

  const office = span(row.checkInAtUtc, row.lastCheckOutAtUtc ?? row.checkOutAtUtc)
  if (office !== null) return office
  // A «Sahədə» day has no office record at all — its hours are the visit's.
  return span(row.fieldCheckInAtUtc, row.fieldCheckOutAtUtc)
}

/**
 * The two letters that stand in for a face on the board.
 *
 * Uppercased with the AZERBAIJANI locale, which is the whole reason this is a function: the default
 * rule turns «i» into «I», and an İlqar whose avatar reads «IM» is a spelling mistake printed six
 * hundred times down a column. Azerbaijani maps it to «İ».
 */
export function initials(name: string): string {
  const words = name.trim().split(/\s+/).filter(Boolean)
  if (words.length === 0) return '—'
  const letters = words.length === 1
    ? words[0].slice(0, 2)
    : words[0][0] + words[1][0]
  return letters.toLocaleUpperCase('az')
}

/** «08:24» — hours and minutes, zero-padded, the way the board's Giriş/Çıxış columns beside it read. */
export function formatWorked(minutes: number | null): string {
  if (minutes === null) return ''
  const h = Math.floor(minutes / 60)
  const m = minutes % 60
  return `${String(h).padStart(2, '0')}:${String(m).padStart(2, '0')}`
}
