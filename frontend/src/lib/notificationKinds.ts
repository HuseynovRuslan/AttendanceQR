import { COMPANY_TZ, fmtTime } from './format'

/**
 * How each notification type looks, in one place: the settings page and the center share it, so a type has the same
 * icon and colour wherever it appears. Colour is never the only signal — every type also has its own icon and name.
 *
 *   giriş — green, an arrow into a door
 *   çıxış — orange, an arrow out of a door
 *   gecikmə — amber, an alarm clock
 */
export type KindTone = 'in' | 'out' | 'late' | 'other'

export interface KindLook {
  tone: KindTone
  /** The short name for filters and the screen reader. */
  label: string
}

export const KIND_LOOK: Record<string, KindLook> = {
  'attendance.check_in': { tone: 'in', label: 'Giriş' },
  'attendance.check_out': { tone: 'out', label: 'Çıxış' },
  'attendance.late': { tone: 'late', label: 'Gecikmə' },
}

export function lookOf(typeKey: string): KindLook {
  return KIND_LOOK[typeKey] ?? { tone: 'other', label: 'Bildiriş' }
}

/** Foreground / background pairs, ≥ 4.5:1 for the foreground on white and on its own background. */
export const TONE_COLORS: Record<KindTone, { fg: string; bg: string }> = {
  in: { fg: '#15803D', bg: '#DCFCE7' },
  out: { fg: '#C2410C', bg: '#FFEDD5' },
  late: { fg: '#A16207', bg: '#FEF3C7' },
  other: { fg: '#475569', bg: '#F1F5F9' },
}

/** The company day (Asia/Baku) of an instant, as YYYY-MM-DD — never the viewer's device zone. */
export function companyDay(iso: string): string {
  return new Intl.DateTimeFormat('en-CA', { timeZone: COMPANY_TZ, year: 'numeric', month: '2-digit', day: '2-digit' })
    .format(new Date(iso))
}

const MONTHS = ['yanvar', 'fevral', 'mart', 'aprel', 'may', 'iyun', 'iyul', 'avqust', 'sentyabr', 'oktyabr', 'noyabr', 'dekabr']

/** «Bu gün», «Dünən», else «24 noyabr» (with the year when it is not this one). */
export function dayHeading(day: string, now: Date = new Date()): string {
  const today = companyDay(now.toISOString())
  const yesterday = companyDay(new Date(now.getTime() - 86_400_000).toISOString())
  if (day === today) return 'Bu gün'
  if (day === yesterday) return 'Dünən'
  const [y, m, d] = day.split('-').map(Number)
  const thisYear = Number(today.slice(0, 4))
  return `${d} ${MONTHS[m - 1]}${y !== thisYear ? ` ${y}` : ''}`
}

/** Consecutive runs of items that fall on the same company day — the list's date sections, in the order given. */
export function groupByDay<T>(items: T[], at: (item: T) => string, now: Date = new Date()): { day: string; heading: string; items: T[] }[] {
  const groups: { day: string; heading: string; items: T[] }[] = []
  for (const item of items) {
    const day = companyDay(at(item))
    const last = groups[groups.length - 1]
    if (last && last.day === day) last.items.push(item)
    else groups.push({ day, heading: dayHeading(day, now), items: [item] })
  }
  return groups
}

/** «Ramana · 09:02», and «· oflayn qeyd» when it was recorded without a connection and synced later. */
export function metaLine(branchName: string | null, occurredAtUtc: string, wasOffline: boolean): string {
  const parts = [branchName, fmtTime(occurredAtUtc)].filter(Boolean) as string[]
  if (wasOffline) parts.push('oflayn qeyd')
  return parts.join(' · ')
}

/** What a line needs to be grouped with the other lines about the same scan. */
export interface Groupable {
  id: string
  typeKey: string
  readAtUtc: string | null
  /** The scan it is about: a check-in and its lateness share it. Absent on an old line — then it stands alone. */
  eventId?: string | null
}

/** One entry of the list: a line, or the lines about one scan (a check-in and its lateness). */
export interface EventGroup<T extends Groupable> {
  key: string
  /** The line the entry shows: the lateness when there is one — «… işə 25 dəqiqə gecikdi» says the arrival too. */
  lead: T
  /** Every line of the entry, lead included, in list order. */
  members: T[]
  unread: boolean
}

/**
 * The lines about the same scan as one entry, at the place the first of them appears. Only lines that are there are
 * grouped — who receives what (settings) and what is shown (filters) decide the lines, this only draws them: with the
 * «Gecikmə» filter, or for somebody who switched check-ins off, the lateness stands alone.
 */
export function groupByEvent<T extends Groupable>(items: T[]): EventGroup<T>[] {
  const groups: EventGroup<T>[] = []
  const byEvent = new Map<string, EventGroup<T>>()
  for (const item of items) {
    const key = item.eventId ? `e:${item.eventId}` : `n:${item.id}`
    const existing = byEvent.get(key)
    if (existing) {
      existing.members.push(item)
      if (item.typeKey === 'attendance.late') existing.lead = item
      existing.unread = existing.unread || !item.readAtUtc
      continue
    }
    const group: EventGroup<T> = { key, lead: item, members: [item], unread: !item.readAtUtc }
    byEvent.set(key, group)
    groups.push(group)
  }
  return groups
}

/** How many entries are unread among these lines — what the badge counts, so it matches what the list shows. */
export function unreadEntries<T extends Groupable>(items: T[]): number {
  return groupByEvent(items).filter((g) => g.unread).length
}
