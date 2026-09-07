import { describe, expect, it } from 'vitest'
import { manualRecordTimes } from './manualRecord'

// The case these exist for: a guard covers the night, nobody scans at all, and the day has to be
// written by hand. Baku is UTC+4, so a 21:00 check-in is 17:00Z on the same date and the 06:51
// check-out is 02:51Z on the NEXT one.

describe('manualRecordTimes', () => {
  it('keeps a day shift on its own date', () => {
    const t = manualRecordTimes('2026-09-05', '09:00', '18:00')!
    expect(t.overnight).toBe(false)
    expect(t.outDate).toBe('2026-09-05')
    expect(t.checkInIso).toBe('2026-09-05T05:00:00.000Z')
    expect(t.checkOutIso).toBe('2026-09-05T14:00:00.000Z')
  })

  it('moves a night shift check-out to the next morning', () => {
    // Vüqar's night, exactly: in 21:00 on Saturday, out 06:51 on Sunday. Built from one date, this
    // used to produce a check-out fourteen hours before the check-in and the server refused it.
    const t = manualRecordTimes('2026-09-05', '21:00', '06:51')!
    expect(t.overnight).toBe(true)
    expect(t.date).toBe('2026-09-05') // the record still belongs to the day the shift began
    expect(t.outDate).toBe('2026-09-06')
    expect(t.checkInIso).toBe('2026-09-05T17:00:00.000Z')
    expect(t.checkOutIso).toBe('2026-09-06T02:51:00.000Z')
    expect(new Date(t.checkOutIso!).getTime()).toBeGreaterThan(new Date(t.checkInIso).getTime())
  })

  it('rolls over a month end', () => {
    expect(manualRecordTimes('2026-08-31', '22:00', '06:00')!.outDate).toBe('2026-09-01')
  })

  it('rolls over a year end', () => {
    expect(manualRecordTimes('2026-12-31', '22:00', '06:00')!.outDate).toBe('2027-01-01')
  })

  it('leaves the check-out empty when there is none — an open day is a real answer', () => {
    const t = manualRecordTimes('2026-09-05', '21:00')!
    expect(t.checkOutIso).toBeUndefined()
    expect(t.overnight).toBe(false)
    expect(t.outDate).toBe('2026-09-05')
  })

  it('needs both a date and a check-in', () => {
    expect(manualRecordTimes('', '09:00')).toBeNull()
    expect(manualRecordTimes('2026-09-05', '')).toBeNull()
  })
})
