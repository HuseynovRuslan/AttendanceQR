import { apiRequest } from './client'

/**
 * Corrections somebody made to their OWN day (docs/strukturlar-ve-selahiyyetler.md §18). Allowed only where the
 * company has nobody else who could make them; kept for good, with the old times and the reason.
 */
export interface SelfCorrectionItem {
  id: string
  employeeId: string
  employeeName: string
  date: string
  /** Update | Create | ClearCheckout | CloseOpen | MissedCheckout */
  kind: string
  checkInBeforeUtc: string | null
  checkOutBeforeUtc: string | null
  checkInAfterUtc: string | null
  checkOutAfterUtc: string | null
  reason: string
  atUtc: string
  /** The company asks for a second approval and nobody has answered yet: nothing was applied. */
  awaitsApproval: boolean
  decision: { approved: boolean; byName: string; reason: string; atUtc: string } | null
  /** This viewer may approve or reject it. */
  mayDecide: boolean
}

export interface SelfCorrectionList {
  needsApproval: boolean
  /** Whether a second approval could be switched on for this viewer — somebody would have to give it. */
  approverAvailable: boolean
  items: SelfCorrectionItem[]
}

export function getSelfCorrections(days = 90) {
  return apiRequest<SelfCorrectionList>(`/api/admin/attendance/self-corrections?days=${days}`)
}

export function decideSelfCorrection(id: string, approve: boolean, reason: string) {
  return apiRequest<{ status: string } | { error: string }>(`/api/admin/attendance/self-corrections/${id}/decide`, {
    method: 'POST',
    body: { approve, reason },
  })
}

export function setSelfCorrectionApproval(needsApproval: boolean) {
  return apiRequest<{ needsApproval: boolean } | { error: string }>('/api/admin/attendance/self-corrections/setting', {
    method: 'PUT',
    body: { needsApproval },
  })
}
