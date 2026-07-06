import { apiRequest } from './client'

export type LeaveType = 'Vacation' | 'Sick' | 'Unpaid' | 'Permission'

export interface LeaveRecord {
  id: string
  employeeId: string
  employeeName: string
  fromDate: string // "yyyy-MM-dd"
  toDate: string
  type: LeaveType
  note: string | null
  createdAtUtc: string
}

export function getLeaves(params?: { from?: string; to?: string; employeeId?: string }) {
  const q = new URLSearchParams()
  if (params?.from) q.set('from', params.from)
  if (params?.to) q.set('to', params.to)
  if (params?.employeeId) q.set('employeeId', params.employeeId)
  const qs = q.toString()
  return apiRequest<LeaveRecord[]>(`/api/admin/leaves${qs ? `?${qs}` : ''}`)
}

export function addLeave(employeeId: string, fromDate: string, toDate: string, type: LeaveType, note?: string) {
  return apiRequest<LeaveRecord | { error: string }>('/api/admin/leaves', {
    method: 'POST',
    body: { employeeId, fromDate, toDate, type, note: note || null },
  })
}

export function deleteLeave(id: string) {
  return apiRequest<{ deleted: string } | { error: string }>(`/api/admin/leaves/${id}`, {
    method: 'DELETE',
  })
}
