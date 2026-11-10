import { describe, expect, it } from 'vitest'
import { minutesLeft, presenceDistance, presenceResultLabel, presenceWeekLine } from './presenceCheck'

// «Mövcudluq yoxlaması» on the manager's profile card and the person's home card. The wording is the part that
// matters: «cavab yoxdur» must read as a fact, and an empty week must not look like a row of failures.

describe('presenceResultLabel', () => {
  it('reads outside as the one bad result and no answer as neutral', () => {
    expect(presenceResultLabel('Outside')).toEqual({ text: 'Filialdan kənarda', tone: 'bad' })
    expect(presenceResultLabel('NoAnswer').tone).toBe('muted')
    expect(presenceResultLabel('GpsFailed').text).toBe('GPS alınmadı')
  })
})

describe('presenceDistance', () => {
  it('shows the distance against the radius it was judged by', () => {
    expect(presenceDistance({ distanceMeters: 84.6, radiusMeters: 150 })).toBe('85 m (radius 150 m)')
  })
  it('switches to kilometres rather than write «1.223 m», which reads as one metre', () => {
    expect(presenceDistance({ distanceMeters: 1222.6, radiusMeters: 150 })).toBe('1,2 km (radius 150 m)')
  })
  it('shows nothing for an unanswered check', () => {
    expect(presenceDistance({ distanceMeters: null, radiusMeters: null })).toBeNull()
  })
})

describe('presenceWeekLine', () => {
  it('says plainly when nothing was asked', () => {
    expect(presenceWeekLine({ asked: 0, inside: 0, outside: 0, noAnswer: 0, gpsFailed: 0, late: 0 }))
      .toBe('Son 7 gündə yoxlama olmayıb.')
  })
  it('names only what happened', () => {
    expect(presenceWeekLine({ asked: 12, inside: 10, outside: 1, noAnswer: 1, gpsFailed: 0, late: 2 }))
      .toBe('Son 7 gün: 12 yoxlamadan 10-i filialda, 1-i kənarda, 1-i cavabsız (2 gec cavab).')
    expect(presenceWeekLine({ asked: 3, inside: 3, outside: 0, noAnswer: 0, gpsFailed: 0, late: 0 }))
      .toBe('Son 7 gün: 3 yoxlamadan 3-i filialda.')
  })
})

describe('minutesLeft', () => {
  const now = Date.parse('2026-11-11T07:00:00Z')
  it('counts down to the end of the window, rounding up', () => {
    expect(minutesLeft('2026-11-11T07:14:10Z', now)).toBe(15)
    expect(minutesLeft('2026-11-11T07:00:30Z', now)).toBe(1)
  })
  it('stops at zero once the window has run out — the card still asks', () => {
    expect(minutesLeft('2026-11-11T06:50:00Z', now)).toBe(0)
    expect(minutesLeft('not a date', now)).toBe(0)
  })
})
