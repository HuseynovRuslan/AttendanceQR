import { apiRequest } from './client'

export interface NotificationItem {
  type: 'PendingDeviceChange' | 'PendingPinReset' | 'Problems' | 'OpenRecords' | 'LateToday' | 'Birthday'
  message: string
  linkTo: string
}

/** Informational feed — today's latest check-ins / check-outs. Doesn't drive the badge. */
export interface ActivityItem {
  message: string
  at: string
  isIn: boolean
  linkTo: string
}

export interface NotificationsSummary {
  totalCount: number
  items: NotificationItem[]
  activity: ActivityItem[]
}

/** GET /api/admin/notifications — computed live every call (pending device changes + today's
 * late count), no persisted/read state. Admin-only. */
export function getNotifications() {
  return apiRequest<NotificationsSummary>('/api/admin/notifications')
}
