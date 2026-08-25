import { apiRequest } from './client'

export type LeaveType = 'Vacation' | 'Sick' | 'Unpaid' | 'Permission' | 'Rest' | 'BusinessTrip'

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


/** What the server did with a batch — created some, skipped the ones already off over those dates. */
export interface LeaveBatchResult {
  created: { employeeId: string; fullName: string }[]
  skipped: {
    employeeId: string
    fullName: string
    reason: string
    conflictType?: string
    conflictFrom?: string
    conflictTo?: string
  }[]
}

/** Files one leave for several people at once. Anyone already off over those dates is skipped and
 *  named back rather than failing the whole batch — a crew of forty must not stop at one holiday. */
export function addLeave(input: {
  employeeIds: string[]
  fromDate: string
  toDate: string
  type: LeaveType
  note?: string
}) {
  return apiRequest<LeaveBatchResult | { error: string; skipped?: LeaveBatchResult['skipped'] }>(
    '/api/admin/leaves',
    {
      method: 'POST',
      body: {
        employeeIds: input.employeeIds,
        // The server reads EmployeeIds when present; this keeps a single-row body valid too.
        employeeId: input.employeeIds[0],
        fromDate: input.fromDate,
        toDate: input.toDate,
        type: input.type,
        note: input.note || null,
      },
    },
  )
}

export function deleteLeave(id: string) {
  return apiRequest<{ deleted: string } | { error: string }>(`/api/admin/leaves/${id}`, {
    method: 'DELETE',
  })
}
