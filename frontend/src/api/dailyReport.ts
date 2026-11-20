import { API_BASE_URL, apiRequest, getToken } from './client'

// The daily management report on Telegram — company admins only (AdminDailyReportController).

export interface DailyReportRecipient {
  id: string
  name: string
  /** Empty = the whole company. */
  locationIds: string[]
  /** A Telegram chat is connected — reports go to it. */
  linked: boolean
  linkedAtUtc: string | null
  /** A «connect» link was issued and has not run out yet (48 h). */
  linkPending: boolean
}

export interface DailyReportDelivery {
  id: string
  recipientName: string
  date: string
  kind: 'scheduled' | 'manual'
  status: 'sent' | 'pending' | 'failed'
  attempts: number
  lastError: string | null
  createdAtUtc: string
  sentAtUtc: string | null
}

export interface DailyReportState {
  /** "HH:mm" (Baku time), or null = the automatic report is off. */
  time: string | null
  /** False = this server has no bot token: nothing can be sent, the preview still works. */
  botConfigured: boolean
  botUsername: string
  recipients: DailyReportRecipient[]
  deliveries: DailyReportDelivery[]
}

type ErrorBody = { error: string }

export function getDailyReport() {
  return apiRequest<DailyReportState>('/api/admin/daily-report')
}

export function saveDailyReportTime(time: string | null) {
  return apiRequest<{ time: string | null } | ErrorBody>('/api/admin/daily-report/settings', {
    method: 'PUT',
    body: { time },
  })
}

export function addDailyReportRecipient(name: string, locationIds: string[]) {
  return apiRequest<{ id: string; connectLink: string | null } | ErrorBody>('/api/admin/daily-report/recipients', {
    method: 'POST',
    body: { name, locationIds },
  })
}

export function updateDailyReportRecipient(id: string, name: string, locationIds: string[]) {
  return apiRequest<{ id: string } | ErrorBody>(`/api/admin/daily-report/recipients/${id}`, {
    method: 'PUT',
    body: { name, locationIds },
  })
}

export function removeDailyReportRecipient(id: string) {
  return apiRequest<{ removed: string } | ErrorBody>(`/api/admin/daily-report/recipients/${id}`, {
    method: 'DELETE',
  })
}

export function newDailyReportConnectLink(id: string) {
  return apiRequest<{ connectLink: string } | ErrorBody>(`/api/admin/daily-report/recipients/${id}/connect-link`, {
    method: 'POST',
  })
}

export function sendDailyReportNow(recipientId?: string) {
  return apiRequest<{ results: { recipientName: string; sent: boolean; error: string | null }[] } | ErrorBody>(
    '/api/admin/daily-report/send-now',
    // Printing a PDF per recipient and sending it takes a few seconds each.
    { method: 'POST', body: { recipientId: recipientId ?? null }, timeoutMs: 120_000 },
  )
}

/**
 * Today's report as it would be sent now — nothing is sent or stored. Returned as a blob so the page can
 * open it in a new tab (HTML) or download it (PDF) with the admin's own token; a plain link would carry none.
 */
export async function fetchDailyReportPreview(format: 'html' | 'pdf', recipientId?: string): Promise<Blob | null> {
  const q = new URLSearchParams({ format })
  if (recipientId) q.set('recipientId', recipientId)
  const token = getToken()
  const res = await fetch(`${API_BASE_URL}/api/admin/daily-report/preview?${q}`, {
    headers: token ? { Authorization: `Bearer ${token}` } : {},
  })
  if (!res.ok) return null
  return res.blob()
}
