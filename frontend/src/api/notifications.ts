import { apiRequest } from './client'

export interface NotificationItem {
  type: 'PendingDeviceChange' | 'LateToday' | 'Birthday' | 'TaskAssigned' | 'TaskCompleted'
  message: string
  linkTo: string
}

export interface NotificationsSummary {
  totalCount: number
  items: NotificationItem[]
}

/** GET /api/admin/notifications — computed live every call, no persisted/read state. Admin gets
 * device-change + birthday reminders; both Admin and Manager get their own task alerts. */
export function getNotifications() {
  return apiRequest<NotificationsSummary>('/api/admin/notifications')
}
