import { apiRequest } from './client'

/** One assigned job, as both the worker's list and the manager's board see it. */
export interface EmployeeTask {
  id: string
  title: string
  description: string | null
  dueDate: string | null
  status: 'Assigned' | 'Done' | 'Approved' | 'Cancelled'
  /** Computed server-side against TODAY — never a stored flag, so it is never a day stale. */
  overdue: boolean
  assignedAtUtc: string
  doneAtUtc: string | null
  approvedAtUtc: string | null
  workerNote: string | null
  rejectionNote: string | null
  hasPhoto: boolean
  employeeId: string
  /** Board only — the worker's list already knows whose it is. */
  employeeName?: string | null
  assignedByName?: string | null
}

/** GET /api/employee-tasks/mine — the caller's open work plus what they finished recently. */
export function getMyTasks() {
  return apiRequest<EmployeeTask[]>('/api/employee-tasks/mine')
}

/** POST /api/employee-tasks/{id}/done — «Hazırdır», with an optional note and proof photo. */
export function completeTask(id: string, note: string | null, photoBase64: string | null) {
  return apiRequest<EmployeeTask | { error: string }>(`/api/employee-tasks/${id}/done`, {
    method: 'POST',
    body: { note, photoBase64 },
  })
}

// --- manager / admin -------------------------------------------------------

/** GET /api/employee-tasks — the board, already scoped server-side to what the caller may manage. */
export function getTaskBoard(status?: string) {
  return apiRequest<EmployeeTask[]>(`/api/employee-tasks${status ? `?status=${status}` : ''}`)
}

export function assignTask(employeeId: string, title: string, description: string | null, dueDate: string | null) {
  return apiRequest<EmployeeTask | { error: string }>('/api/employee-tasks', {
    method: 'POST',
    body: { employeeId, title, description, dueDate },
  })
}

export function approveTask(id: string) {
  return apiRequest<EmployeeTask | { error: string }>(`/api/employee-tasks/${id}/approve`, { method: 'POST' })
}

export function rejectTask(id: string, note: string | null) {
  return apiRequest<EmployeeTask | { error: string }>(`/api/employee-tasks/${id}/reject`, {
    method: 'POST',
    body: { note },
  })
}

export function cancelTask(id: string) {
  return apiRequest<EmployeeTask | { error: string }>(`/api/employee-tasks/${id}/cancel`, { method: 'POST' })
}

export function getTaskPhoto(id: string) {
  return apiRequest<{ url: string | null }>(`/api/employee-tasks/${id}/photo`)
}
