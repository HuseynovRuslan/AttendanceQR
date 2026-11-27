import { describe, expect, it } from 'vitest'
import { companyDay, dayHeading, groupByDay, lookOf, metaLine } from './notificationKinds'

/** The center's sections and lines: by the company's day (Baku), whatever the viewer's device zone. */
describe('notification kinds and days', () => {
  it('maps each type to its own name and tone, and anything else to a neutral one', () => {
    expect(lookOf('attendance.check_in')).toEqual({ tone: 'in', label: 'Giriş' })
    expect(lookOf('attendance.check_out')).toEqual({ tone: 'out', label: 'Çıxış' })
    expect(lookOf('attendance.late')).toEqual({ tone: 'late', label: 'Gecikmə' })
    expect(lookOf('hr.leave_request').tone).toBe('other')
  })

  it('files an instant under the Baku day — 23:30 UTC is already tomorrow there', () => {
    expect(companyDay('2026-11-23T19:59:00Z')).toBe('2026-11-23')
    expect(companyDay('2026-11-23T20:00:00Z')).toBe('2026-11-24')
  })

  it('heads today and yesterday by name and older days by date, with the year only when it differs', () => {
    const now = new Date('2026-11-24T08:00:00Z')
    expect(dayHeading('2026-11-24', now)).toBe('Bu gün')
    expect(dayHeading('2026-11-23', now)).toBe('Dünən')
    expect(dayHeading('2026-11-02', now)).toBe('2 noyabr')
    expect(dayHeading('2025-12-31', now)).toBe('31 dekabr 2025')
  })

  it('groups consecutive lines of one day and keeps the order given', () => {
    const now = new Date('2026-11-24T08:00:00Z')
    const items = ['2026-11-24T07:00:00Z', '2026-11-24T05:00:00Z', '2026-11-23T10:00:00Z']
    const groups = groupByDay(items, (x) => x, now)
    expect(groups.map((g) => [g.heading, g.items.length])).toEqual([['Bu gün', 2], ['Dünən', 1]])
  })

  it('writes where and when, and says when it was an offline scan', () => {
    expect(metaLine('Ramana', '2026-11-24T05:02:00Z', false)).toBe('Ramana · 09:02')
    expect(metaLine(null, '2026-11-24T05:02:00Z', true)).toBe('09:02 · oflayn qeyd')
  })
})
