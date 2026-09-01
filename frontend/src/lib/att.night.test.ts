import { describe, expect, it } from 'vitest'
import { companyDate, nightShiftState } from './att'
import type { AttendanceRecord } from '../api/attendance'

/**
 * The real case, from a customer on 2026-09-01.
 *
 * Yaiçnikov charges the machines at Heydər Əliyev Mərkəzi from 20:00 to 06:00. He scanned in at
 * 19:37 on 31 August and at 05:53 the next morning the app told him he had not checked in yet — so
 * he did not scan, and the night he had just worked stayed open, which is scored as zero hours.
 *
 * Company time throughout: the server stamps `attendanceDate` in Asia/Baku and pivots on noon there,
 * so a phone in another timezone must not be able to reach a different answer.
 */
const rec = (p: Partial<AttendanceRecord>): AttendanceRecord =>
  ({ attendanceDate: '2026-08-31', checkInAtUtc: null, checkOutAtUtc: null, ...p }) as AttendanceRecord

/** 2026-08-31 19:37 Baku = 15:37 UTC. */
const CHECK_IN = '2026-08-31T15:37:00Z'
const openNight = [rec({ attendanceDate: '2026-08-31', checkInAtUtc: CHECK_IN })]

/** 2026-09-01 05:53 Baku = 01:53 UTC — the moment in the screenshot. */
const AT_0553 = new Date('2026-09-01T01:53:00Z')

describe('nightShiftState', () => {
  it('recognises the shift that is still running at six in the morning', () => {
    expect(nightShiftState(openNight, '20:00', '06:00', AT_0553)).toEqual({ kind: 'night', checkIn: CHECK_IN })
  })

  it('says nothing to a day worker with the same open record', () => {
    // An unclosed day shift is a different problem with a different answer — the admin closes it.
    // Offering "Çıxış et" here would promise a check-out the server would refuse to perform.
    expect(nightShiftState(openNight, '09:00', '18:00', AT_0553)).toBeNull()
  })

  it('stops at noon, exactly where the server stops', () => {
    const noon = new Date('2026-09-01T08:00:00Z') // 12:00 Baku
    expect(nightShiftState(openNight, '20:00', '06:00', noon)).toBeNull()
    const justBefore = new Date('2026-09-01T07:59:00Z') // 11:59 Baku
    expect(nightShiftState(openNight, '20:00', '06:00', justBefore)).not.toBeNull()
  })

  it('goes quiet once the night has been closed', () => {
    const closed = [rec({ attendanceDate: '2026-08-31', checkInAtUtc: CHECK_IN, checkOutAtUtc: '2026-09-01T02:00:00Z' })]
    expect(nightShiftState(closed, '20:00', '06:00', AT_0553)).toBeNull()
  })

  it('leaves today alone once today has a row of its own', () => {
    const started = [...openNight, rec({ attendanceDate: '2026-09-01', checkInAtUtc: '2026-09-01T01:00:00Z' })]
    expect(nightShiftState(started, '20:00', '06:00', AT_0553)).toBeNull()
  })

  it('needs a shift at all — an employee with no hours set gets no promise', () => {
    expect(nightShiftState(openNight, null, null, AT_0553)).toBeNull()
    expect(nightShiftState(openNight, '20:00', null, AT_0553)).toBeNull()
  })

  it('does not reach back further than one night', () => {
    const older = [rec({ attendanceDate: '2026-08-29', checkInAtUtc: '2026-08-29T15:37:00Z' })]
    expect(nightShiftState(older, '20:00', '06:00', AT_0553)).toBeNull()
  })
})

describe('companyDate', () => {
  it('is the company\'s date, not UTC\'s', () => {
    // 2026-09-01 01:00 Baku is still 31 August in UTC. The old todayStr() returned the UTC date and
    // so looked up the wrong row for the four hours after midnight — the only hours a night worker
    // is awake.
    expect(companyDate(new Date('2026-08-31T21:00:00Z'))).toBe('2026-09-01')
    expect(companyDate(new Date('2026-09-01T01:53:00Z'))).toBe('2026-09-01')
  })
})
