import type { ActivityItem } from '../api/notifications'
import type { CenterNotification, NotificationPreferences } from '../api/notificationCenter'
import { fmtTime } from './format'
import type { LiveItem } from './notificationHub'
import { groupByEvent } from './notificationKinds'

/**
 * The bell holds two lists that can say the same thing: the person's own notifications (persisted, read/unread) and
 * «Son hərəkət» — today's latest check-ins and check-outs, computed on the fly, office and field. When the person
 * receives the office ones as notifications, «Son hərəkət» would repeat them line for line.
 *
 * So «Son hərəkət» keeps only what the notifications do not already say:
 *   • field visits («sahəyə giriş») — the notification module does not carry them, they always stay;
 *   • office check-ins/outs — gone when the person's notifications cover every one of them (both types on, in the
 *     center, for every branch they may see); otherwise only the lines shown in the bell's own list right above are
 *     dropped, so a partial setting (one type, a few branches) still sees the rest here.
 * Nothing is lost: what leaves «Son hərəkət» is in the notification center, and the day board has all of it.
 */
export function feedCoversOffice(prefs: Pick<NotificationPreferences, 'eligible' | 'types' | 'branchMode' | 'channels'> | null): boolean {
  if (!prefs || !prefs.eligible || !prefs.channels.inApp || prefs.branchMode !== 'all') return false
  const on = new Set(prefs.types.filter((t) => t.enabled).map((t) => t.key))
  return on.has('attendance.check_in') && on.has('attendance.check_out')
}

const OFFICE = '/admin/today'

function officeKey(isIn: boolean, name: string, at: string): string {
  return `${isIn ? 'in' : 'out'}|${name.trim().toLocaleLowerCase('az')}|${at}`
}

/** The person's name in a «Son hərəkət» line: «Əliyev Elvin — giriş etdi». */
function nameOf(a: ActivityItem): string {
  const i = a.message.indexOf(' — ')
  return i > 0 ? a.message.slice(0, i) : a.message
}

export function activityBesideFeed(
  activity: ActivityItem[],
  shown: Pick<CenterNotification, 'typeKey' | 'subjectName' | 'occurredAtUtc'>[],
  covered: boolean,
): ActivityItem[] {
  const office = (a: ActivityItem) => a.linkTo === OFFICE
  if (covered) return activity.filter((a) => !office(a))
  const said = new Set(
    shown
      .filter((n) => n.subjectName && (n.typeKey === 'attendance.check_in' || n.typeKey === 'attendance.check_out'))
      .map((n) => officeKey(n.typeKey === 'attendance.check_in', n.subjectName!, fmtTime(n.occurredAtUtc))),
  )
  return activity.filter((a) => !office(a) || !said.has(officeKey(a.isIn, nameOf(a), a.at)))
}

/** The entry a burst should pop up: the newest scan's, its lateness rather than its arrival; and how many others came. */
export function toastOf(items: LiveItem[]): { lead: LiveItem; others: number } | null {
  if (items.length === 0) return null
  const keyed = items.map((i, n) => ({ ...i, id: i.id ?? `live-${n}`, readAtUtc: null }))
  const groups = groupByEvent(keyed)
  const last = groups[groups.length - 1]
  const lead = items[keyed.findIndex((k) => k.id === last.lead.id)] ?? items[items.length - 1]
  return { lead, others: groups.length - 1 }
}
