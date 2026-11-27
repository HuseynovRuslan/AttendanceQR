import { describe, expect, it } from 'vitest'
import { companyDay, dayHeading, groupByDay, groupByEvent, lookOf, metaLine, unreadEntries } from './notificationKinds'

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

  describe('the lines about one scan', () => {
    const line = (id: string, typeKey: string, eventId: string | null, read = false) => ({
      id, typeKey, eventId, readAtUtc: read ? '2026-11-24T06:00:00Z' : null,
    })

    it('shows a check-in and its lateness as one entry led by the lateness, where the first of them stood', () => {
      const items = [line('a', 'attendance.check_in', 'e1'), line('x', 'attendance.check_out', 'e2'), line('b', 'attendance.late', 'e1')]
      const groups = groupByEvent(items)
      expect(groups.map((g) => g.key)).toEqual(['e:e1', 'e:e2'])
      expect(groups[0].lead.id).toBe('b')
      expect(groups[0].members.map((m) => m.id)).toEqual(['a', 'b'])
    })

    it('leaves a line alone when it is the only one there — a filter or a setting decided that, not the grouping', () => {
      expect(groupByEvent([line('b', 'attendance.late', 'e1')]).map((g) => g.members.length)).toEqual([1])
      // An old line without its scan never joins anything.
      expect(groupByEvent([line('a', 'attendance.check_in', null), line('b', 'attendance.late', null)])).toHaveLength(2)
    })

    it('is unread while any of its lines is, and counts as one entry', () => {
      const items = [line('a', 'attendance.check_in', 'e1', true), line('b', 'attendance.late', 'e1'), line('c', 'attendance.check_in', 'e3', true)]
      expect(groupByEvent(items).map((g) => g.unread)).toEqual([true, false])
      expect(unreadEntries(items)).toBe(1)
      expect(unreadEntries([line('a', 'attendance.check_in', 'e1'), line('b', 'attendance.late', 'e1')])).toBe(1)
    })
  })
})
