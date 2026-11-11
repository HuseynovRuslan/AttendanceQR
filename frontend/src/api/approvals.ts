import { apiRequest } from './client'

/**
 * Change requests — /api/approvals (docs/tesdiq-sistemi-faza1.md). The server decides every right in every AuthzMode
 * and answers 404 «ApprovalsDisabled» while the deployment's switch is off; these are only its shapes.
 *
 * Only one kind of request exists on the server: «Yeni işçi» (EmployeeCreate). The screens must not offer more.
 */

export type RequestStatus = 'Pending' | 'Approved' | 'Rejected' | 'Cancelled'
export type RequestType = 'EmployeeCreate'
export type EventKind = 'Created' | 'Approved' | 'Rejected' | 'Cancelled' | 'Applied' | 'ApplyRefused' | 'Direct'

export interface ApiError {
  error: string
  /** DuplicatePendingRequest: the request that already waits. */
  number?: number
  /** AlreadyDecided: where it stands now. */
  status?: RequestStatus
  /** ApplyRefused: why the approval could not be applied. */
  reason?: string
  /** ApprovalRequired: whether this person may send a request instead. */
  canRequest?: boolean
}

export interface ApprovalBranch {
  locationId: string
  name: string
  structureId: string | null
  structureName: string | null
  canRequest: boolean
  canActDirectly: boolean
  isApprover: boolean
  standsIn: boolean
  canView: boolean
}

export interface ApprovalsMe {
  enabled: boolean
  branches?: ApprovalBranch[]
  queueCount?: number
}

export interface RequestItem {
  id: string
  number: number | null
  type: RequestType
  status: RequestStatus
  direct: boolean
  locationId: string
  locationName: string | null
  subject: string
  requestedBy: string | null
  requestedByMe: boolean
  createdAtUtc: string
  decidedBy: string | null
  decidedAtUtc: string | null
  /** The rejection's reason (or the approver's note). */
  decisionNote: string | null
  waitingOver24h: boolean
  canDecide: boolean
  canCancel: boolean
}

export interface EmployeePayload {
  firstName: string
  lastName: string
  fatherName: string | null
  /** Null when the server withheld it from this reader (contactsHidden). */
  phoneNumber: string | null
  email: string | null
  position: string | null
  birthDate: string | null
  locationId: string
  scheduleId: string | null
  fullName?: string
}

export interface HistoryLine {
  kind: EventKind
  atUtc: string
  actor: string | null
  note: string | null
  data: Record<string, unknown> | null
}

export interface RequestDetail {
  request: RequestItem
  payload: EmployeePayload | null
  /** True when the phone number, the e-mail and the birth date were removed for this reader. */
  contactsHidden: boolean
  note: string | null
  decisionNote: string | null
  employeeId: string | null
  history: HistoryLine[]
}

export interface RequestSummary {
  id: string
  number: number | null
  status: RequestStatus
  locationName: string | null
  subject: string
  createdAtUtc: string
  replayed: boolean
}

export interface FormOptions {
  positions: string[]
  schedules: { id: string; name: string; start: string; end: string; branchOnly: boolean }[]
}

export interface NewEmployeeRequest {
  firstName: string
  lastName: string
  fatherName?: string | null
  phoneNumber?: string | null
  email?: string | null
  position?: string | null
  birthDate?: string | null
  locationId: string
  scheduleId?: string | null
  note?: string | null
}

export type ListView = 'queue' | 'mine' | 'all'

export function getApprovalsMe() {
  return apiRequest<ApprovalsMe>('/api/approvals/me')
}

export function listRequests(view: ListView, opts: { status?: RequestStatus; locationId?: string; limit?: number } = {}) {
  const q = new URLSearchParams({ view })
  if (opts.status) q.set('status', opts.status)
  if (opts.locationId) q.set('locationId', opts.locationId)
  if (opts.limit) q.set('limit', String(opts.limit))
  return apiRequest<{ items: RequestItem[] } | ApiError>(`/api/approvals/requests?${q}`)
}

export function getRequest(id: string) {
  return apiRequest<RequestDetail | ApiError>(`/api/approvals/requests/${encodeURIComponent(id)}`)
}

export function getFormOptions(locationId: string) {
  return apiRequest<FormOptions | ApiError>(`/api/approvals/options?locationId=${encodeURIComponent(locationId)}`)
}

/** The key is the client's «this is the same press of the button»: the same key returns the first request. */
export function createEmployeeRequest(body: NewEmployeeRequest, idempotencyKey: string) {
  return apiRequest<RequestSummary | ApiError>('/api/approvals/requests/employee-create', {
    method: 'POST', body, headers: { 'Idempotency-Key': idempotencyKey },
  })
}

export interface ApproveResult {
  request: RequestSummary
  employeeId?: string
  /** Shown once to the approver and never stored readably. Absent on a replayed approval. */
  tempPin?: string
}

export function approveRequest(id: string, note?: string) {
  return apiRequest<ApproveResult | ApiError>(`/api/approvals/requests/${encodeURIComponent(id)}/approve`, {
    method: 'POST', body: { note: note ?? null },
  })
}

export function rejectRequest(id: string, note: string) {
  return apiRequest<{ request: RequestSummary } | ApiError>(`/api/approvals/requests/${encodeURIComponent(id)}/reject`, {
    method: 'POST', body: { note },
  })
}

export function cancelRequest(id: string, note?: string) {
  return apiRequest<{ request: RequestSummary } | ApiError>(`/api/approvals/requests/${encodeURIComponent(id)}/cancel`, {
    method: 'POST', body: { note: note ?? null },
  })
}

export function isApiError(x: unknown): x is ApiError {
  return !!x && typeof x === 'object' && 'error' in x && typeof (x as { error: unknown }).error === 'string'
}
