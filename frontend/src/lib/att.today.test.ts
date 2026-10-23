import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { AttendanceRecord } from '../api/attendance'
import { todayState } from './att'

/**
 * The home card after a «səyyar» field visit.
 *
 * Reported from the field: a driver sent out at 08:00 came back to the centre at noon and found no
 * «Giriş et» — the card said the day was finished. The server was ready to reopen it; the phone had
 * decided on its own. These pin that the card now asks the server, and that an ordinary finished day
 * stays finished (showing the button there invites a stray tap the server refuses).
 */

const DAY = '2026-09-18'
const row = (over: Partial<AttendanceRecord>): AttendanceRecord => ({
  recordId: crypto.randomUUID(),
  attendanceDate: DAY,
  locationId: 'l',
  checkInAtUtc: `${DAY}T04:00:00Z`,
  checkOutAtUtc: `${DAY}T05:00:00Z`,
  status: 'OnTime',
  ...over,
} as AttendanceRecord)

beforeEach(() => {
  vi.useFakeTimers()
  vi.setSystemTime(new Date(`${DAY}T08:00:00Z`))   // noon in Baku
})
afterEach(() => vi.useRealTimers())

describe('today after a field visit', () => {
  it('a field-only day does not read as finished', () => {
    const field = row({ recordId: '00000000-0000-0000-0000-000000000000', isFieldDay: true })

    expect(todayState([field], null)).toEqual({ kind: 'none', again: 'field' })
  })

  it('a check-in the field visit closed offers the scan when the server says so', () => {
    const morning = row({ closedByFieldVisit: true })

    expect(todayState([morning], { ...morning, mayScanAgain: true })).toEqual({ kind: 'none', again: 'field' })
  })

  it('trusts the server when it says no', () => {
    const morning = row({ closedByFieldVisit: true })

    expect(todayState([morning], { ...morning, mayScanAgain: false }).kind).toBe('done')
  })

  it('without the server, a single field-closed stretch still offers the scan', () => {
    expect(todayState([row({ closedByFieldVisit: true })]).kind).toBe('none')
  })

  it('an ordinary finished day stays finished', () => {
    const day = row({})

    expect(todayState([day], day).kind).toBe('done')
    expect(todayState([day]).kind).toBe('done')
  })

  it('a split shift gets its second window', () => {
    const first = row({})

    expect(todayState([first], { ...first, mayScanAgain: true })).toEqual({ kind: 'none', again: 'second' })
  })

  it('two stretches on one date: the open one decides, whatever the list order', () => {
    const morning = row({ closedByFieldVisit: true })
    const afternoon = row({ checkInAtUtc: `${DAY}T07:30:00Z`, checkOutAtUtc: null })

    expect(todayState([morning, afternoon])).toEqual({ kind: 'in', checkIn: afternoon.checkInAtUtc })
    expect(todayState([afternoon, morning])).toEqual({ kind: 'in', checkIn: afternoon.checkInAtUtc })
  })

  it('two closed stretches: finished, by the latest', () => {
    const morning = row({ closedByFieldVisit: true })
    const afternoon = row({ checkInAtUtc: `${DAY}T07:30:00Z`, checkOutAtUtc: `${DAY}T13:00:00Z` })

    expect(todayState([morning, afternoon])).toEqual({
      kind: 'done', checkIn: afternoon.checkInAtUtc, checkOut: afternoon.checkOutAtUtc,
    })
  })

  it('nothing today is a plain fresh day', () => {
    expect(todayState([], null)).toEqual({ kind: 'none' })
  })
})
