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
