import { describe, expect, it } from 'vitest'
import { flexibleSessionState } from './att'
import type { AttendanceRecord } from '../api/attendance'

/**
 * «Elastik qrafik»: in at 22:00 on Monday, still at work at 02:00 on Tuesday. There is no row for Tuesday, and the
 * card must still say «İşdəsiniz» — the server will close Monday's session with the next scan (FlexibleScanRules).
 * Past sixteen hours it will not, and neither may the card promise it.
 */
const rec = (p: Partial<AttendanceRecord>): AttendanceRecord =>
  ({ attendanceDate: '2026-10-05', checkInAtUtc: null, checkOutAtUtc: null, ...p }) as AttendanceRecord

/** Monday 5 October 2026, 22:00 Baku = 18:00 UTC. */
const IN_22 = '2026-10-05T18:00:00Z'
const open = [rec({ attendanceDate: '2026-10-05', checkInAtUtc: IN_22 })]

describe('flexibleSessionState', () => {
  it('keeps last night\'s open session on the card after midnight', () => {
    // Tuesday 02:00 Baku = Monday 22:00 UTC.
    expect(flexibleSessionState(open, new Date('2026-10-05T22:00:00Z'))).toEqual({ kind: 'night', checkIn: IN_22 })
  })

  it('still at 13:00 the next day — a flexible session has no noon pivot, only the sixteen hours', () => {
    // Tuesday 13:00 Baku = 09:00 UTC: fifteen hours in.
    expect(flexibleSessionState(open, new Date('2026-10-06T09:00:00Z'))).not.toBeNull()
  })

  it('lets go once a scan could no longer close it (a forgotten check-out)', () => {
    // Tuesday 14:30 Baku = 10:30 UTC: sixteen and a half hours.
    expect(flexibleSessionState(open, new Date('2026-10-06T10:30:00Z'))).toBeNull()
  })

  it('says nothing once today has a row of its own, or when last night was closed', () => {
    const now = new Date('2026-10-05T22:00:00Z')
    expect(flexibleSessionState([...open, rec({ attendanceDate: '2026-10-06', checkInAtUtc: '2026-10-05T21:30:00Z' })], now)).toBeNull()
    expect(flexibleSessionState([rec({ attendanceDate: '2026-10-05', checkInAtUtc: IN_22, checkOutAtUtc: '2026-10-05T21:00:00Z' })], now)).toBeNull()
  })
})
