import { describe, expect, it } from 'vitest'
import {
  fmtDate, fmtDateOfInstant, fmtDateTime, fmtDayMonth, fmtDuration, fmtHM, fmtPhone, fmtShortDate, fmtTime,
  minutesBetween, fmtLongDate,
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

describe('fmtPhone', () => {
  it('spells the stored nine digits the way a person writes them', () => {
    // What the server keeps (PhoneNumbers.Normalize): the subscriber number alone.
    expect(fmtPhone('508006710')).toBe('+994 50 800 67 10')
  })

  it('collapses every older spelling onto the same one', () => {
    // Rows imported before normalisation still carry these.
    expect(fmtPhone('0508006710')).toBe('+994 50 800 67 10')
    expect(fmtPhone('+994508006710')).toBe('+994 50 800 67 10')
    expect(fmtPhone('+994 50 800-67-10')).toBe('+994 50 800 67 10')
  })

  it('leaves a number it cannot vouch for as it was typed', () => {
    // A seven-digit landline forced into "+994 XX …" would be a wrong number, not a tidy one.
    expect(fmtPhone('4921234')).toBe('4921234')
  })

  it('is null for nothing, so the field draws its own dash', () => {
    expect(fmtPhone(null)).toBeNull()
    expect(fmtPhone('  ')).toBeNull()
  })
})

describe('fmtLongDate', () => {
  it('writes the date in Azerbaijani without asking the runtime for the locale', () => {
    // The bug: toLocaleDateString('az-AZ', {weekday:'long', month:'long'}) fell back to the root
    // locale and produced «2026 M09 7, Mon» — which went out as the title of the workbook sent to
    // the leadership every morning.
    expect(fmtLongDate('2026-09-07')).toBe('7 sentyabr 2026, bazar ertəsi')
  })

  it('names every weekday', () => {
    // 6–12 September 2026 is Sunday through Saturday.
    expect(fmtLongDate('2026-09-06')).toContain('bazar')
    expect(fmtLongDate('2026-09-08')).toContain('çərşənbə axşamı')
    expect(fmtLongDate('2026-09-09')).toContain('çərşənbə')
    expect(fmtLongDate('2026-09-10')).toContain('cümə axşamı')
    expect(fmtLongDate('2026-09-11')).toContain('cümə')
    expect(fmtLongDate('2026-09-12')).toContain('şənbə')
  })

  it('does not shift the day, whatever the machine timezone is', () => {
    expect(fmtLongDate('2026-01-01')).toBe('1 yanvar 2026, cümə axşamı')
  })
})
