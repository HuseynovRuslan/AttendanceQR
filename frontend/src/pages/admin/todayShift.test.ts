import { describe, expect, it } from 'vitest'
import { formatWorked, initials, shiftHours, shiftTitle, workedMinutes } from './todayShift'

describe('initials', () => {
  it('takes the first letter of the first two words', () => {
    expect(initials('Nərgiz Məmmədova')).toBe('NM')
    expect(initials('Haciyev Zahid Rahim')).toBe('HZ')
  })

  it('uppercases the AZERBAIJANI way', () => {
    // The default rule gives «IM», which is a spelling mistake printed down a column of six hundred.
    expect(initials('İlqar Mahid')).toBe('İM')
    expect(initials('iradə səfərova')).toBe('İS')
  })

  it('copes with one word and with nothing', () => {
    expect(initials('Musa')).toBe('MU')
    expect(initials('   ')).toBe('—')
  })
})

describe('shiftHours', () => {
  it('prints the day it was given', () => {
    expect(shiftHours({ shiftStart: '09:00', shiftEnd: '18:00' })).toBe('09:00–18:00')
  })

  it('prints BOTH stretches of a split day', () => {
    // «Əlavə qüvvə»: 07:00–11:00 and then 22:00–07:00. The morning alone would read as a four-hour
    // day on the one kind of row somebody opens this column to check.
    expect(shiftHours({
      shiftStart: '07:00', shiftEnd: '11:00', secondShiftStart: '22:00', secondShiftEnd: '07:00',
    })).toBe('07:00–11:00 + 22:00–07:00')
  })

  it('says nothing when the hours never arrived', () => {
    expect(shiftHours({})).toBeNull()
    expect(shiftHours({ shiftStart: '09:00' })).toBeNull()
  })

  it('names the shift in the tooltip when the hours came from one', () => {
    expect(shiftTitle({ shiftStart: '22:00', shiftEnd: '06:00', shiftName: 'Gecə A' }))
      .toBe('Gecə A · 22:00–06:00')
    expect(shiftTitle({ shiftStart: '09:00', shiftEnd: '18:00' })).toBe('09:00–18:00')
  })
})

describe('workedMinutes', () => {
  const NOW = Date.parse('2026-10-01T09:00:00Z')

  it('is the closed day, end to end', () => {
    expect(workedMinutes(
      { checkInAtUtc: '2026-10-01T04:00:00Z', checkOutAtUtc: '2026-10-01T13:00:00Z' }, NOW, true,
    )).toBe(540)
  })

  it('runs up to now while the day is open — on today only', () => {
    const open = { checkInAtUtc: '2026-10-01T05:30:00Z' }
    expect(workedMinutes(open, NOW, true)).toBe(210)
    // A past day nobody closed is zero hours — decided 2026-08-11 with the numbers in hand. Guessing
    // a departure here would pay 165 unclosed days out of this function's imagination.
    expect(workedMinutes(open, NOW, false)).toBeNull()
  })

  it('SUMS a split day instead of subtracting its ends', () => {
    // 07:00–11:00 and 22:00–07:00 next morning: eight hours of it unpaid in between. Last-minus-first
    // would say twenty-four.
    const worked = workedMinutes({
      checkInAtUtc: '2026-09-30T03:00:00Z',
      lastCheckOutAtUtc: '2026-10-01T03:00:00Z',
      blockSpans: [
        { inAtUtc: '2026-09-30T03:00:00Z', outAtUtc: '2026-09-30T07:00:00Z' },
        { inAtUtc: '2026-09-30T18:00:00Z', outAtUtc: '2026-10-01T03:00:00Z' },
      ],
    }, NOW, false)
    expect(worked).toBe(4 * 60 + 9 * 60)
  })

  it('counts the open stretch of a split day that is still running', () => {
    expect(workedMinutes({
      blockSpans: [
        { inAtUtc: '2026-10-01T03:00:00Z', outAtUtc: '2026-10-01T07:00:00Z' },
        { inAtUtc: '2026-10-01T08:30:00Z', outAtUtc: null },
      ],
    }, NOW, true)).toBe(240 + 30)
  })

  it('falls back to the field visit when there is no office record', () => {
    expect(workedMinutes({
      fieldCheckInAtUtc: '2026-10-01T04:00:00Z', fieldCheckOutAtUtc: '2026-10-01T08:00:00Z',
    }, NOW, true)).toBe(240)
  })

  it('has no answer for a day with no arrival at all', () => {
    expect(workedMinutes({}, NOW, true)).toBeNull()
  })

  it('never goes negative when a phone clock ran ahead', () => {
    // An offline scan carries the PHONE's time, which can sit in the future. A negative cell would
    // read as a minus sign against somebody's hours.
    expect(workedMinutes({ checkInAtUtc: '2026-10-01T10:00:00Z' }, NOW, true)).toBe(0)
  })
})

describe('formatWorked', () => {
  it('reads like the times beside it', () => {
    expect(formatWorked(48)).toBe('00:48')
    expect(formatWorked(106)).toBe('01:46')
    expect(formatWorked(540)).toBe('09:00')
    expect(formatWorked(null)).toBe('')
  })
})
