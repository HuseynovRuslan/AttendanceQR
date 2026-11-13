import type { PlanDay, PlanStep } from '../../../api/shiftPlan'

/**
 * The plan screen's arithmetic, kept out of the components so it can be read and tested on its own: the days a
 * view shows, how a shift is told apart at a glance, and where two shifts leave too little rest between them.
 */

export type View = 'day' | 'week' | 'month'

/** Dates are kept as «YYYY-MM-DD» strings end to end — the server's DateOnly — never as Date objects in local time. */
export function addDays(iso: string, days: number): string {
  const [y, m, d] = iso.split('-').map(Number)
  const date = new Date(Date.UTC(y, m - 1, d + days))
  return date.toISOString().slice(0, 10)
}

/** 0 = Sunday, the server's DayOfWeek. */
export function weekday(iso: string): number {
  const [y, m, d] = iso.split('-').map(Number)
  return new Date(Date.UTC(y, m - 1, d)).getUTCDay()
}

export function daysBetween(from: string, to: string): number {
  const a = Date.parse(`${from}T00:00:00Z`)
  const b = Date.parse(`${to}T00:00:00Z`)
  return Math.round((b - a) / 86_400_000)
}

/** The days a view shows around a date: the day; Monday–Sunday; the calendar month. */
export function rangeOf(view: View, anchor: string): { from: string; to: string } {
  if (view === 'day') return { from: anchor, to: anchor }
  if (view === 'week') {
    const back = (weekday(anchor) + 6) % 7
    const from = addDays(anchor, -back)
    return { from, to: addDays(from, 6) }
  }
  const [y, m] = anchor.split('-').map(Number)
  const from = `${y}-${String(m).padStart(2, '0')}-01`
  const last = new Date(Date.UTC(y, m, 0)).getUTCDate()
  return { from, to: `${y}-${String(m).padStart(2, '0')}-${String(last).padStart(2, '0')}` }
}

/** Where «‹» and «›» go from a view. */
export function step(view: View, anchor: string, direction: 1 | -1): string {
  if (view === 'day') return addDays(anchor, direction)
  if (view === 'week') return addDays(anchor, 7 * direction)
  const [y, m] = anchor.split('-').map(Number)
  const next = new Date(Date.UTC(y, m - 1 + direction, 1))
  return next.toISOString().slice(0, 10)
}

export function datesOf(from: string, to: string): string[] {
  const out: string[] = []
  for (let d = from; d <= to; d = addDays(d, 1)) out.push(d)
  return out
}

const MONTHS = ['yanvar', 'fevral', 'mart', 'aprel', 'may', 'iyun', 'iyul', 'avqust', 'sentyabr', 'oktyabr', 'noyabr', 'dekabr']
const MONTHS_TITLE = MONTHS.map((m) => m[0].toUpperCase() + m.slice(1))
export const WEEKDAYS_SHORT = ['B', 'B.e', 'Ç.a', 'Ç', 'C.a', 'C', 'Ş'] // by DayOfWeek, Sunday first
export const WEEKDAYS_LONG = ['bazar', 'bazar ertəsi', 'çərşənbə axşamı', 'çərşənbə', 'cümə axşamı', 'cümə', 'şənbə']

export function dayLabel(iso: string): string {
  const [, m, d] = iso.split('-').map(Number)
  return `${d} ${MONTHS[m - 1]}`
}

/** «12–18 noyabr», «28 oktyabr – 3 noyabr», «Noyabr 2026», «Cümə, 13 noyabr». */
export function rangeLabel(view: View, from: string, to: string): string {
  const [fy, fm, fd] = from.split('-').map(Number)
  const [, tm, td] = to.split('-').map(Number)
  if (view === 'month') return `${MONTHS_TITLE[fm - 1]} ${fy}`
  if (view === 'day') {
    const name = WEEKDAYS_LONG[weekday(from)]
    return `${name[0].toUpperCase()}${name.slice(1)}, ${fd} ${MONTHS[fm - 1]}`
  }
  return fm === tm ? `${fd}–${td} ${MONTHS[fm - 1]}` : `${fd} ${MONTHS[fm - 1]} – ${td} ${MONTHS[tm - 1]}`
}

/**
 * The part of the day a shift belongs to, from its start: what its colour says. Four bands, not a colour per
 * template — a branch with nine shifts would otherwise be a paint chart nobody can read.
 */
export type Band = 'morning' | 'day' | 'evening' | 'night'

export function bandOf(start: string | null, overnight: boolean): Band {
  const hour = start ? Number(start.slice(0, 2)) : 9
  // By when it starts: an evening shift that runs past midnight is still the evening shift people know it as.
  if (hour >= 20 || (overnight && hour < 5)) return 'night'
  if (hour < 9) return 'morning'
  if (hour < 14) return 'day'
  return 'evening'
}

function minutes(hhmm: string): number {
  return Number(hhmm.slice(0, 2)) * 60 + Number(hhmm.slice(3, 5))
}

/** The labour rule the screen warns about: at least this many hours between one shift's end and the next start. */
export const MIN_REST_HOURS = 11

/**
 * Days that start too soon after the day before ended — a night to 06:00 followed by a 08:00 start is two hours'
 * rest. Only days somebody works count; a day off between them is rest enough.
 */
export function shortRestDays(days: PlanDay[], minHours = MIN_REST_HOURS): Set<string> {
  const short = new Set<string>()
  for (let i = 1; i < days.length; i++) {
    const before = days[i - 1]
    const after = days[i]
    if (!before.working || !after.working || !before.start || !before.end || !after.start) continue
    const endsAt = (before.overnight ? 24 * 60 : 0) + minutes(before.end)
    const startsAt = 24 * 60 + minutes(after.start)
    if (startsAt - endsAt < minHours * 60) short.add(after.date)
  }
  return short
}

/** Ready rotations: what a planner usually means by «2/2». Each is a list of steps; «S» is the chosen shift. */
export interface RotationPreset {
  id: string
  label: string
  /** 'shift' = the chosen shift, 'second' = the second chosen shift, 'rest' = a day off. */
  steps: ('shift' | 'second' | 'rest')[]
}

export const ROTATIONS: RotationPreset[] = [
  { id: '1-1', label: 'Bir gündən bir', steps: ['shift', 'rest'] },
  { id: '2-2', label: '2 gün iş, 2 gün istirahət', steps: ['shift', 'shift', 'rest', 'rest'] },
  { id: '3-1', label: 'Sutka (1 gün iş, 2 gün istirahət)', steps: ['shift', 'rest', 'rest'] },
  { id: '2-2-2', label: '2 gündüz, 2 gecə, 2 istirahət', steps: ['shift', 'shift', 'second', 'second', 'rest', 'rest'] },
]

export function needsSecondShift(steps: RotationPreset['steps']): boolean {
  return steps.includes('second')
}

/** A preset made into the server's steps. */
export function rotationSteps(preset: RotationPreset, shiftId: string, secondId: string | null): PlanStep[] {
  return preset.steps.map((s) => (s === 'rest' ? 'rest' : s === 'second' ? (secondId ?? shiftId) : shiftId))
}

/** Monday–Friday as the server's weekday mask (bit = DayOfWeek, Sunday = bit 0). */
export const WORKWEEK = 0b0111110

export function maskOf(days: number[]): number {
  return days.reduce((mask, d) => mask | (1 << d), 0)
}

/** How many person-days a plan writes: what the confirm line says before it is saved. */
export function cellsOf(people: number, from: string, to: string, mask: number | null, restOnOtherDays: boolean): number {
  const days = datesOf(from, to)
  const counted = mask === null || restOnOtherDays ? days.length : days.filter((d) => (mask & (1 << weekday(d))) !== 0).length
  return people * counted
}

/** The Azerbaijani words for a leave type the server sends. */
export const LEAVE_LABEL: Record<string, string> = {
  Vacation: 'Məzuniyyət',
  Sick: 'Xəstəlik',
  Unpaid: 'Ödənişsiz',
  Permission: 'İcazə',
  Rest: 'İstirahət',
  BusinessTrip: 'Ezamiyyət',
}

/** Two letters for a shift in the month view, where a column is a finger wide. */
export function initials(name: string): string {
  const words = name.trim().split(/\s+/).filter(Boolean)
  if (words.length === 0) return '—'
  if (words.length === 1) return words[0].slice(0, 2)
  return (words[0][0] + words[1][0]).toUpperCase()
}
