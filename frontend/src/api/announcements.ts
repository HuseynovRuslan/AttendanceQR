import { apiRequest } from './client'

/** One announcement as an employee sees it. */
export interface Announcement {
  id: string
  message: string
  createdAtUtc: string
}

/** One announcement as the admin sees it (carries the active flag). */
export interface AdminAnnouncement extends Announcement {
  isActive: boolean
}

/** GET /api/announcements — active announcements for the signed-in employee's tenant. */
export function getAnnouncements() {
  return apiRequest<Announcement[]>('/api/announcements')
}

// --- admin -----------------------------------------------------------------

export function getAdminAnnouncements() {
  return apiRequest<AdminAnnouncement[]>('/api/admin/announcements')
}

export function createAnnouncement(message: string) {
  return apiRequest<AdminAnnouncement | { error: string }>('/api/admin/announcements', {
    method: 'POST',
    body: { message },
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
