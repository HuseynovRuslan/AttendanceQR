import { describe, expect, it } from 'vitest'
import {
  COMPANY_TZ,
  fmtDateOfInstant,
  fmtDateTime,
  fmtTime,
  fromCompanyInputValue,
  toCompanyInputValue,
} from './format'

/**
 * Times are read in the COMPANY's timezone, never the device's.
 *
 * The bug these exist for: a worker at Dədə Qorqud left at 19:28 and the board showed 18:28. The
 * record was correct; the phone reading it was on UTC+3, and every time on every screen of that
 * device was an hour early. Nobody suspects the phone — they suspect the system, and the argument
 * that follows is about somebody's wages.
 *
 * Every case below asserts an ABSOLUTE answer for a fixed instant. That is what makes them meaningful:
 * a correct implementation gives the same answer whatever timezone the machine running the tests is
 * in, so these fail on any regression back to device-local rendering.
 */

/** The real record from 26 August 2026: check-in 03:58 UTC, check-out 15:28 UTC. */
const CHECK_IN = '2026-08-26T03:58:47.377081Z'
const CHECK_OUT = '2026-08-26T15:28:04.496516Z'

describe('reading a time', () => {
  it('shows the Baku wall clock, not the reader’s', () => {
    // 15:28 UTC is 19:28 in Baku. This is the exact pair from the report.
    expect(fmtTime(CHECK_OUT)).toBe('19:28')
    expect(fmtTime(CHECK_IN)).toBe('07:58')
  })

  it('keeps its fallback for a missing time', () => {
    expect(fmtTime(null)).toBe('—')
    expect(fmtTime(undefined, '')).toBe('')
  })

  it('carries the timezone into the date-and-time form too', () => {
    expect(fmtDateTime(CHECK_OUT)).toContain('19:28')
  })

  it('lands on the right DAY for an instant near midnight', () => {
    // 22:30 UTC on the 26th is 02:30 on the 27th in Baku. A reader on UTC-5 would otherwise be shown
    // the 26th — the day a summary, a tabel row and a day's pay all hang off.
    expect(fmtDateOfInstant('2026-08-26T22:30:00Z')).toBe('27.08.2026')
  })
})

describe('editing a time', () => {
  it('fills the box with the same time the board shows', () => {
    expect(toCompanyInputValue(CHECK_OUT)).toBe('2026-08-26T19:28')
  })

  it('saves the time it displays', () => {
    // The correction path that mattered: an admin sees 19:28 and leaves it alone. What goes back must
    // be the instant that was already stored, not their device's idea of 19:28.
    expect(fromCompanyInputValue('2026-08-26T19:28')).toBe('2026-08-26T15:28:00.000Z')
  })

  it('round-trips an instant through the input and back', () => {
    const back = fromCompanyInputValue(toCompanyInputValue(CHECK_OUT))
    // Seconds are not in a datetime-local box, so the minute is what has to survive.
    expect(back.slice(0, 16)).toBe('2026-08-26T15:28')
  })

  it('handles midnight, where an off-by-one lands on the wrong day', () => {
    expect(fromCompanyInputValue('2026-08-27T00:00')).toBe('2026-08-26T20:00:00.000Z')
    expect(toCompanyInputValue('2026-08-26T20:00:00Z')).toBe('2026-08-27T00:00')
  })
})

describe('the timezone constant', () => {
  it('matches the backend App:TimeZone', () => {
    // If these ever disagree, the board and the payroll disagree. Pinned so a change to one is a
    // failing test rather than a discovery at the end of a month.
    expect(COMPANY_TZ).toBe('Asia/Baku')
  })
})
