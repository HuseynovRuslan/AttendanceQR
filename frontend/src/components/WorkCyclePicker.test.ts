import { describe, expect, it } from 'vitest'
import { isWorkingDay, NO_CYCLE, type WorkCycleValue } from './WorkCyclePicker'

/**
 * The preview strip is only useful if it agrees with the server. These mirror
 * WorkCycleTests.cs case for case: if the two ever drift, a manager confirms a calendar that is not
 * the one attendance is actually judged against, which is worse than showing no preview at all.
 */
describe('isWorkingDay', () => {
  const cycle = (days: number | null, onDays: number, anchor: string): WorkCycleValue =>
    ({ days, onDays, anchor })

  // 2026-07-01 is a Wednesday; the 5th is a Sunday.
  const d = (day: number) => new Date(2026, 6, day)

  it('falls back to the weekly calendar with no cycle', () => {
    expect(isWorkingDay(NO_CYCLE, d(5))).toBe(false) // Sunday
    expect(isWorkingDay(NO_CYCLE, d(6))).toBe(true)  // Monday
  })

  it('alternates on every-other-day and overrides the weekly Sunday', () => {
    const c = cycle(2, 1, '2026-07-01')
    for (const day of [1, 3, 5, 7, 9]) expect(isWorkingDay(c, d(day))).toBe(true)
    for (const day of [2, 4, 6, 8, 10]) expect(isWorkingDay(c, d(day))).toBe(false)
  })

  it('handles sutka (1 on, 2 off)', () => {
    const c = cycle(3, 1, '2026-07-01')
    expect([1, 2, 3, 4].map((n) => isWorkingDay(c, d(n)))).toEqual([true, false, false, true])
  })

  it('handles 2 on / 2 off', () => {
    const c = cycle(4, 2, '2026-07-01')
    expect([1, 2, 3, 4, 5].map((n) => isWorkingDay(c, d(n)))).toEqual([true, true, false, false, true])
  })

  it('resolves dates before the anchor onto the right half of the cycle', () => {
    const c = cycle(2, 1, '2026-07-10')
    expect(isWorkingDay(c, d(8))).toBe(true)
    expect(isWorkingDay(c, d(9))).toBe(false)
    expect(isWorkingDay(c, d(2))).toBe(true)
  })

  it('does not drift over months', () => {
    const c = cycle(2, 1, '2026-07-01')
    expect(isWorkingDay(c, new Date(2026, 6, 31))).toBe(true)   // +30
    expect(isWorkingDay(c, new Date(2026, 7, 1))).toBe(false)   // +31
    expect(isWorkingDay(c, new Date(2026, 11, 31))).toBe(false) // +183
  })

  it('ignores a cycle with no anchor rather than calling every day a rest day', () => {
    const c = cycle(2, 1, '')
    expect(isWorkingDay(c, d(6))).toBe(true)  // Monday, per the weekly calendar
    expect(isWorkingDay(c, d(5))).toBe(false) // Sunday
  })
})
