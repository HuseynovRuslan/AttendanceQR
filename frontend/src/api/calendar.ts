import { apiRequest } from './client'

export interface NonWorkingDay {
  id: string
  date: string // "yyyy-MM-dd"
  description: string
  locationId: string | null
  locationName: string | null
}

export function getNonWorkingDays() {
  return apiRequest<NonWorkingDay[]>('/api/admin/non-working-days')
}

export function addNonWorkingDay(date: string, description: string, locationId?: string | null) {
  return apiRequest<NonWorkingDay | { error: string }>('/api/admin/non-working-days', {
    method: 'POST',
    body: { date, description, locationId: locationId ?? null },
  })
}

export function deleteNonWorkingDay(id: string) {
  return apiRequest<{ deleted: string } | { error: string }>(`/api/admin/non-working-days/${id}`, {
    method: 'DELETE',
  })
}
