import { apiRequest } from './client'

export type AnnouncementAudience = 'All' | 'AtWork' | 'NotAtWork' | 'Selected'

/** One announcement as an employee sees it. */
export interface Announcement {
  id: string
  title: string | null
  message: string
  createdAtUtc: string
}

/** One announcement as the admin sees it. */
export interface AdminAnnouncement {
  id: string
  title: string | null
  message: string
  audience: AnnouncementAudience
  scheduledForUtc: string | null
  recipientCount: number
  isActive: boolean
  createdAtUtc: string
}

export interface CreateAnnouncementInput {
  title?: string | null
  message: string
  audience: AnnouncementAudience
  /** "yyyy-MM-ddTHH:mm" local wall-clock, or null for immediate. */
  scheduledForLocal?: string | null
  /** Required when audience is 'Selected'. */
  recipientIds?: string[]
}

/** GET /api/announcements — announcements visible to the signed-in employee right now. */
export function getAnnouncements() {
  return apiRequest<Announcement[]>('/api/announcements')
}

// --- admin -----------------------------------------------------------------

export function getAdminAnnouncements() {
  return apiRequest<AdminAnnouncement[]>('/api/admin/announcements')
}

export function createAnnouncement(input: CreateAnnouncementInput) {
  return apiRequest<{ id: string } | { error: string }>('/api/admin/announcements', {
    method: 'POST',
    body: input,
  })
}

export function retireAnnouncement(id: string) {
  return apiRequest<{ id: string; isActive: boolean } | { error: string }>(
    `/api/admin/announcements/${id}/retire`,
    { method: 'POST' },
  )
}

export function deleteAnnouncement(id: string) {
  return apiRequest<{ deleted: string } | { error: string }>(`/api/admin/announcements/${id}`, {
    method: 'DELETE',
  })
}
