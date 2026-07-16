import { describe, expect, it } from 'vitest'
import {
  fmtDate, fmtDateOfInstant, fmtDateTime, fmtDayMonth, fmtDuration, fmtHM, fmtShortDate, fmtTime,
  minutesBetween,
} from './format'

// These are shared by fifteen screens now, so a mistake here is a mistake everywhere. The cases that
// matter most are the ones that used to differ between the copies: the missing-value fallback, and
// calendar-date vs instant.

describe('fmtTime', () => {
  it('shows an instant as local HH:mm', () => {
    // 05:45Z is 09:45 in Baku (UTC+4) — the conversion is the point of the function.
    expect(fmtTime('2026-07-15T05:45:00Z')).toBe('09:45')
  })

  it('defaults to a dash when there is no value', () => {
    expect(fmtTime(null)).toBe('—')
    expect(fmtTime(undefined)).toBe('—')
  })

  it('takes a different fallback, which is why ScanPage can still render nothing', () => {
    // ScanPage's own copy returned '' — "Saat —" would read as a time that exists.
    expect(fmtTime(null, '')).toBe('')
  })
})

describe('fmtDate', () => {
  it('reformats a calendar date without going through a timezone', () => {
    expect(fmtDate('2026-07-15')).toBe('15.07.2026')
  })

  it('does not shift the day, whatever the machine timezone is', () => {
    // The bug this prevents: new Date('2026-01-01') is midnight UTC, which is 31 December in the
    // Americas. An AttendanceDate is a calendar date and must never move.
    expect(fmtDate('2026-01-01')).toBe('01.01.2026')
  })
})

describe('fmtShortDate', () => {
  it('drops the year for axis labels', () => {
    expect(fmtShortDate('2026-07-15')).toBe('15.07')
  })
})

describe('fmtDayMonth', () => {
  it('reads as prose', () => {
    expect(fmtDayMonth('2026-07-05')).toBe('5 iyul')
  })

  it('covers every month', () => {
    expect(fmtDayMonth('2026-01-31')).toBe('31 yanvar')
    expect(fmtDayMonth('2026-12-01')).toBe('1 dekabr')
  })
})

describe('fmtDateTime / fmtDateOfInstant', () => {
  it('formats an instant in local time', () => {
    // 21:30Z on the 15th is 01:30 on the 16th in Baku — an instant, unlike fmtDate, genuinely shifts.
    expect(fmtDateTime('2026-07-15T21:30:00Z')).toContain('16.07')
    expect(fmtDateOfInstant('2026-07-15T21:30:00Z')).toBe('16.07.2026')
  })
})

describe('minutesBetween / fmtDuration', () => {
  it('measures a shift', () => {
    expect(minutesBetween('2026-07-15T05:00:00Z', '2026-07-15T13:30:00Z')).toBe(510)
    expect(fmtDuration('2026-07-15T05:00:00Z', '2026-07-15T13:30:00Z')).toBe('8 saat 30 dəqiqə')
  })

  it('clamps a negative span rather than showing "-1 saat"', () => {
    // HistoryPage's copy did not clamp; bad data would have rendered negative numbers at the user.
    expect(fmtDuration('2026-07-15T13:00:00Z', '2026-07-15T05:00:00Z')).toBe('0 saat 0 dəqiqə')
  })
})

describe('fmtHM', () => {
  it('turns decimal hours into words', () => {
    expect(fmtHM(8.5)).toBe('8 saat 30 dəq')
    expect(fmtHM(8)).toBe('8 saat')
  })

  it('renders a fraction of an hour as minutes', () => {
    // The reason this function exists: "0.32 saat" was read as 32 minutes. It is 19.
    expect(fmtHM(0.32)).toBe('19 dəq')
  })

  it('shows a dash for nothing worked', () => {
    expect(fmtHM(0)).toBe('—')
  })
})
