import { describe, expect, it } from 'vitest'
import { countToday, matchesLeaveCard, sortRows, type TodayLike } from './todayCounts'

/**
 * The board's cards, and the distinction that has now been got wrong twice.
 *
 * Holiday, sick leave, unpaid leave and a work trip all arrive as one status — `OnLeave` — and are
 * separable only by `leaveType`. The reports merged Ezamiyyət into Məzuniyyət until 3d6ac7e; the
 * today board still did, and an owner reading «Məzuniyyət 14» on a morning when several of those
 * people were out working reported it. These tests exist so there is no third time.
 */

const row = (status: string, leaveType?: string): TodayLike => ({ status, leaveType })

describe('a work trip is not a holiday', () => {
  it('counts Ezamiyyət on its own', () => {
    const c = countToday([
      row('OnLeave', 'Vacation'),
      row('OnLeave', 'BusinessTrip'),
      row('OnLeave', 'BusinessTrip'),
    ])

    expect(c.trip).toBe(2)
    expect(c.onLeave).toBe(1)
  })

  it('keeps sick leave separate too', () => {
    const c = countToday([row('OnLeave', 'Sick'), row('OnLeave', 'Vacation'), row('OnLeave', 'BusinessTrip')])

    expect(c).toMatchObject({ sick: 1, onLeave: 1, trip: 1 })
  })

  it('leaves unpaid and rest leave under Məzuniyyət', () => {
    // They ARE absences from work; only sick and the trip are told apart, and for different reasons.
    const c = countToday([row('OnLeave', 'Unpaid'), row('OnLeave', 'Rest'), row('OnLeave', 'Vacation')])

    expect(c.onLeave).toBe(3)
    expect(c.trip).toBe(0)
  })

  it('does not lose a leave whose type is missing', () => {
    // An older record, or one the API did not fill. It is still somebody's day — it must land in a
    // bucket rather than falling through to "checked in without checking out".
    const c = countToday([row('OnLeave'), row('OnLeave', null as unknown as string)])

    expect(c.onLeave).toBe(2)
    expect(c.incomplete).toBe(0)
  })
})

describe('the rest of the board', () => {
  it('counts the ordinary statuses', () => {
    const c = countToday([
      row('OnTime'), row('Late'), row('Field'),
      row('Absent'), row('Pending'), row('DayOff'), row('Permission'),
    ])

    expect(c).toMatchObject({ present: 3, absent: 1, pending: 1, dayOff: 1, permission: 1 })
  })

  it('treats an unknown status as still at work', () => {
    // "Checked in, no check-out yet" has no status of its own — it is what is left over.
    expect(countToday([row('CheckedIn')]).incomplete).toBe(1)
  })

  it('counts nothing for an empty board', () => {
    expect(countToday([])).toMatchObject({ present: 0, onLeave: 0, trip: 0, sick: 0 })
  })
})

describe('clicking a card shows exactly what it counted', () => {
  // The cards are filters. If the list under one disagrees with the number printed on it, the reader
  // has no way to tell which of the two is lying.
  const people = [
    row('OnLeave', 'Vacation'),
    row('OnLeave', 'Sick'),
    row('OnLeave', 'BusinessTrip'),
    row('OnTime'),
  ]

  it('matches each leave card to its own count', () => {
    const c = countToday(people)

    for (const card of ['sick', 'trip', 'onLeave'] as const)
      expect(people.filter((r) => matchesLeaveCard(r, card))).toHaveLength(
        card === 'sick' ? c.sick : card === 'trip' ? c.trip : c.onLeave,
      )
  })

  it('never matches a row that is not on leave at all', () => {
    expect(matchesLeaveCard(row('OnTime'), 'onLeave')).toBe(false)
    expect(matchesLeaveCard(row('Absent'), 'trip')).toBe(false)
  })
})

describe('ordering the board', () => {
  const row = (name: string, over: Partial<Parameters<typeof sortRows>[0][number]> = {}) => ({
    employeeName: name, locationName: 'Mərkəz', position: null,
    status: 'OnTime', checkInAtUtc: null, checkOutAtUtc: null, ...over,
  })

  it('sorts names the Azerbaijani way, not the ASCII way', () => {
    // «Ə» sits after E in the alphabet, and after Z in a naive sort — which puts a large share of
    // this company's staff at the bottom of every list.
    const out = sortRows([row('Zeynalov'), row('Əliyev'), row('Abbasov')], 'name', false)

    expect(out.map((r) => r.employeeName)).toEqual(['Abbasov', 'Əliyev', 'Zeynalov'])
  })

  it('reverses when asked', () => {
    const out = sortRows([row('Abbasov'), row('Zeynalov')], 'name', true)

    expect(out.map((r) => r.employeeName)).toEqual(['Zeynalov', 'Abbasov'])
  })

  it('keeps people with no check-in at the bottom in BOTH directions', () => {
    // The one that matters. An absentee has no time, and "no time" is not "earliest" — sorting them
    // to the top of an ascending list buries everybody who actually came, which is the opposite of
    // what somebody sorting by arrival wants to see.
    const rows = [row('Yox'), row('Səkkiz', { checkInAtUtc: '2026-08-31T08:00:00Z' })]

    expect(sortRows(rows, 'in', false).map((r) => r.employeeName)).toEqual(['Səkkiz', 'Yox'])
    expect(sortRows(rows, 'in', true).map((r) => r.employeeName)).toEqual(['Səkkiz', 'Yox'])
  })

  it('falls back to the name so equal rows never shuffle between renders', () => {
    const rows = [row('Bəbirov', { position: 'Bağban' }), row('Abbasov', { position: 'Bağban' })]

    expect(sortRows(rows, 'position', false).map((r) => r.employeeName)).toEqual(['Abbasov', 'Bəbirov'])
  })

  it('sorts a missing job title as empty rather than dropping the row', () => {
    const out = sortRows([row('A', { position: 'Bağban' }), row('B')], 'position', false)

    expect(out).toHaveLength(2)
    expect(out[0]!.employeeName).toBe('B')
  })

  it('does not mutate what it was given', () => {
    const rows = [row('Zeynalov'), row('Abbasov')]

    sortRows(rows, 'name', false)

    expect(rows[0]!.employeeName).toBe('Zeynalov')
  })
})
