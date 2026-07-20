import { apiRequest } from './client'

export interface NotificationItem {
  type: 'PendingDeviceChange' | 'LateToday' | 'Birthday'
  message: string
  linkTo: string
}

export interface NotificationsSummary {
  totalCount: number
  items: NotificationItem[]
}

/** GET /api/admin/notifications — computed live every call (pending device changes + today's
 * late count), no persisted/read state. Admin-only. */
export function getNotifications() {
  return apiRequest<NotificationsSummary>('/api/admin/notifications')
}
