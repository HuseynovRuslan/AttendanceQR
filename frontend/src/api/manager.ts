import { apiRequest } from './client'

// A manager's own write surface. Every call is scoped server-side to the branches they oversee, so
// the client never has to (and never should) enforce scope itself — it only shows what it is given.

export interface ManagerLocation {
  id: string
  name: string
}

export interface ManagerEmployee {
  id: string
  fullName: string
  fatherName: string | null
  position: string | null
  phoneNumber: string | null
  email: string
  locationId: string
  locationName: string
  birthDate: string | null
  birthYear: number | null
  workStart: string | null
  workEnd: string | null
  photoExempt: boolean
  isActive: boolean
  activated: boolean
}

// No salary, no role — a manager sets neither, so the shape carries neither.
export interface ManagerEmployeeInput {
  fullName: string
  email: string | null
  phoneNumber: string | null
  fatherName: string | null
  position: string | null
  locationId: string
  birthDate: string | null
  birthYear: number | null
  workStart: string | null
  workEnd: string | null
  photoExempt: boolean
  isActive: boolean
}

export function getManagerLocations() {
  return apiRequest<ManagerLocation[]>('/api/manager/locations')
}

export function getManagerPositions() {
  return apiRequest<{ name: string }[]>('/api/manager/positions')
}

export function getManagerEmployees() {
  return apiRequest<ManagerEmployee[]>('/api/manager/employees')
}

export function createManagerEmployee(input: ManagerEmployeeInput) {
  return apiRequest<{ id: string; tempPin: string } | { error: string }>('/api/manager/employees', {
    method: 'POST',
    body: input,
  })
}

export function updateManagerEmployee(id: string, input: ManagerEmployeeInput) {
  return apiRequest<{ id: string } | { error: string }>(`/api/manager/employees/${id}`, {
    method: 'PUT',
    body: input,
  })
}

export function resetManagerEmployeePin(id: string) {
  return apiRequest<{ id: string; tempPin: string } | { error: string }>(
    `/api/manager/employees/${id}/reset-pin`,
    { method: 'POST' },
  )
}

// --- leaves ---

export interface ManagerLeave {
  id: string
  employeeId: string
  employeeName: string
  fromDate: string
  toDate: string
  type: string
  note: string | null
}

export function getManagerLeaves(from?: string, to?: string) {
  const q = new URLSearchParams()
  if (from) q.set('from', from)
  if (to) q.set('to', to)
  const qs = q.toString()
  return apiRequest<ManagerLeave[]>(`/api/manager/leaves${qs ? `?${qs}` : ''}`)
}

export function createManagerLeave(input: {
  employeeId: string
  fromDate: string
  toDate: string
  type: string
  note: string | null
}) {
  return apiRequest<{ id: string } | { error: string }>('/api/manager/leaves', {
    method: 'POST',
    body: input,
  })
}

export function deleteManagerLeave(id: string) {
  return apiRequest<{ deleted: string } | { error: string }>(`/api/manager/leaves/${id}`, {
    method: 'DELETE',
  })
}
