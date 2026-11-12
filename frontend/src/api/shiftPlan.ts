import { apiRequest } from './client'

/**
 * «Növbə planı» — who works which shift on which day. A planned day is the same row a cover day always was, so
 * everything that reads a day (the board, the tabel, the scan's night rule, the reminder) already follows it.
 */

export interface PlanLocation {
  id: string
  name: string
}

export interface PlanShift {
  id: string
  name: string
  start: string
  end: string
  overnight: boolean
  locationId: string | null
}

/**
 * One person's one day.
 * - plan: a shift planned for this day
 * - rest: a planned day off
 * - base: their own standing shift, nothing planned
 * - off: their own standing shift's day off, nothing planned
 */
export type DayKind = 'plan' | 'rest' | 'base' | 'off'

export interface PlanDay {
  date: string
  kind: DayKind
  scheduleId: string | null
  shift: string | null
  start: string | null
  end: string | null
  overnight: boolean
  working: boolean
  holiday: string | null
  leave: string | null
  checkIn: string | null
  checkOut: string | null
}

export interface PlanEmployee {
  id: string
  name: string
  position: string | null
  role: 'Employee' | 'Manager' | 'Admin'
  scheduleId: string | null
  days: PlanDay[]
}

export interface PlanGrid {
  locationId: string
  from: string
  to: string
  today: string
  schedules: PlanShift[]
  employees: PlanEmployee[]
}

/** A shift's id, a day off, or back to the person's own shift. */
export type PlanStep = string | 'rest' | 'clear'

export interface PlanApply {
  employeeIds: string[]
  steps: PlanStep[]
  from?: string
  to?: string
  dates?: string[]
  weekdays?: number | null
  restOnOtherDays?: boolean
  confirmPast?: boolean
  note?: string | null
}

export interface PlanApplied {
  ok: true
  set: number
  cleared: number
  unchanged: number
  people: number
  rebuiltDays: number
}

export interface PlanRefusal {
  error: string
  pastDays?: number
  count?: number
  employees?: string[]
  maxDays?: number
}

export function getPlanLocations() {
  return apiRequest<PlanLocation[]>('/api/admin/shift-plan/locations')
}

export function getShiftPlan(locationId: string, from: string, to: string) {
  const q = new URLSearchParams({ locationId, from, to })
  return apiRequest<PlanGrid | PlanRefusal>(`/api/admin/shift-plan?${q}`)
}

export function applyShiftPlan(body: PlanApply) {
  return apiRequest<PlanApplied | PlanRefusal>('/api/admin/shift-plan/apply', { method: 'POST', body })
}
