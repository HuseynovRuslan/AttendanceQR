import { apiRequest } from './client'
import type { GeoFailKind } from '../lib/geo'

/** «Mövcudluq yoxlaması» — see PresenceCheck on the server. */

export interface PendingPresenceCheck {
  id: string
  sentAtUtc: string
  /** After this the answer is «late» — still taken, only marked. */
  answerByUtc: string
}

/** GET /api/presence-checks/pending — the question waiting for this person, or null. */
export function getPendingPresenceCheck() {
  return apiRequest<{ check: PendingPresenceCheck | null }>('/api/presence-checks/pending', { timeoutMs: 15_000 })
}

export type PresenceAnswer =
  | { latitude: number; longitude: number; accuracyMeters: number | null }
  | { failure: GeoFailKind }

/** POST /api/presence-checks/{id}/answer — the phone's position, or why it gave none. */
export function answerPresenceCheck(id: string, answer: PresenceAnswer) {
  return apiRequest<{ result: string; late: boolean } | { error: string }>(`/api/presence-checks/${id}/answer`, {
    method: 'POST',
    body: answer,
    timeoutMs: 20_000,
  })
}

export type PresenceResult = 'Pending' | 'Inside' | 'Outside' | 'NoAnswer' | 'Cancelled' | 'GpsFailed'

export interface PresenceCheckRow {
  id: string
  sentAtUtc: string
  result: PresenceResult
  respondedAtUtc: string | null
  late: boolean
  distanceMeters: number | null
  radiusMeters: number | null
  accuracyMeters: number | null
  reached: number | null
  branch: string | null
}

export interface PresenceCheckView {
  on: boolean
  untilUtc: string | null
  setAtUtc: string | null
  setByName: string | null
  canChange: boolean
  allowedDays: number[]
  week: { asked: number; inside: number; outside: number; noAnswer: number; gpsFailed: number; late: number }
  checks: PresenceCheckRow[]
}

/** GET /api/admin/employees/{id}/presence-checks — the switch, the last week and the history. */
export function getEmployeePresenceChecks(employeeId: string) {
  return apiRequest<PresenceCheckView | { error: string }>(`/api/admin/employees/${employeeId}/presence-checks`)
}

/** PUT /api/admin/employees/{id}/presence-checks — on for 7, 14 or 30 days; null switches it off. */
export function setEmployeePresenceChecks(employeeId: string, days: number | null) {
  return apiRequest<PresenceCheckView | { error: string }>(`/api/admin/employees/${employeeId}/presence-checks`, {
    method: 'PUT',
    body: { days },
  })
}
