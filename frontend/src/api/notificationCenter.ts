import { apiRequest } from './client'

/**
 * The notification center and the person's own notification settings — backend NotificationsController
 * (/api/notifications). Not to be confused with api/notifications.ts, the admin bell's live «needs attention» list,
 * which is computed on the fly and has no history.
 */

export type NotificationTypeKey = 'attendance.check_in' | 'attendance.check_out' | 'attendance.late'

export interface CenterNotification {
  id: string
  typeKey: NotificationTypeKey | string
  title: string
  body: string
  subjectEmployeeId: string | null
  subjectName: string | null
  /** The scope branch (the subject's own). */
  locationId: string | null
  /** Where it happened. */
  branchName: string | null
  /** When it happened — the scan's own moment, also for an offline scan synced later. */
  occurredAtUtc: string
  wasOffline: boolean
  /** Minutes late, for a late arrival. */
  minutes: number | null
  url: string | null
  createdAtUtc: string
  readAtUtc: string | null
}

export interface CenterPage {
  items: CenterNotification[]
  nextCursor: string | null
}

export interface CenterQuery {
  type?: string
  locationId?: string
  unread?: boolean
  cursor?: string | null
  limit?: number
}

export function getCenter(q: CenterQuery = {}) {
  const params = new URLSearchParams()
  if (q.type) params.set('type', q.type)
  if (q.locationId) params.set('locationId', q.locationId)
  if (q.unread) params.set('unread', 'true')
  if (q.cursor) params.set('cursor', q.cursor)
  if (q.limit) params.set('limit', String(q.limit))
  const qs = params.toString()
  return apiRequest<CenterPage>(`/api/notifications${qs ? `?${qs}` : ''}`)
}

export function getUnreadCount() {
  // `more`: the server counts only as far as the badge shows (99) — beyond that it is «99+».
  return apiRequest<{ unread: number; more?: boolean }>('/api/notifications/unread-count')
}

export function markRead(id: string) {
  return apiRequest<{ id: string; readAtUtc: string }>(`/api/notifications/${id}/read`, { method: 'POST' })
}

export function markAllRead(type?: string) {
  return apiRequest<{ marked: number }>(`/api/notifications/read-all${type ? `?type=${encodeURIComponent(type)}` : ''}`, {
    method: 'POST',
  })
}

export function getStreamTicket() {
  return apiRequest<{ ticket: string; expiresInSeconds: number }>('/api/notifications/stream-ticket', { method: 'POST' })
}

// --- settings -----------------------------------------------------------------------------------------------

export interface PreferenceType {
  key: NotificationTypeKey | string
  label: string
  description: string
  enabled: boolean
}

export interface PreferenceBranch {
  id: string
  name: string
  structureId: string | null
  structureName: string | null
}

export interface Channels {
  inApp: boolean
  push: boolean
  web: boolean
}

export interface NotificationPreferences {
  category: string
  /** Whether the person may see anybody's attendance at all. False: the attendance section is not for them. */
  eligible: boolean
  companyWide: boolean
  types: PreferenceType[]
  branchMode: 'all' | 'selected'
  locationIds: string[]
  channels: Channels
  /** The branches they may pick from — only their own scope, from the server. */
  branches: PreferenceBranch[]
  updatedAtUtc: string | null
}

export interface PreferencesInput {
  category: string
  enabledTypes: string[]
  branchMode: 'all' | 'selected'
  locationIds: string[]
  inApp: boolean
  push: boolean
  web: boolean
}

export function getPreferences(category = 'attendance') {
  return apiRequest<NotificationPreferences>(`/api/notifications/preferences?category=${encodeURIComponent(category)}`)
}

export function savePreferences(input: PreferencesInput) {
  return apiRequest<NotificationPreferences | { error: string }>('/api/notifications/preferences', {
    method: 'PUT',
    body: input,
  })
}
