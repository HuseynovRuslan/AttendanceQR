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
  /** From `from` with no end: a standing shift or rotation, not days. */
  standing?: boolean
  /** Say what would change and save nothing. */
  preview?: boolean
  locationId?: string | null
}

export interface PlanApplied {
  ok: true
  set: number
  cleared: number
  unchanged: number
  people: number
  rebuiltDays: number
  /** Worked days whose rebuild has not held yet; the server keeps trying. */
  rebuildPending: number
  changeId: string | null
}

/** One line of a preview: how many days go from one shift to another. */
export interface PlanMove {
  from: string
  to: string
  count: number
}

export interface PlanPreview {
  preview: true
  standing: boolean
  people: number
  days?: number
  newDays?: number
  replacedDays?: number
  clearedDays?: number
  unchangedDays?: number
  changedPeople?: number
  label?: string
  from?: string
  pastDays: number
  changes: PlanMove[]
}

export interface PlanChangeItem {
  employee: string
  date: string
  standing: boolean
  before: string
  after: string
}

export interface PlanChange {
  id: string
  at: string
  by: string | null
  summary: string
  people: number
  days: number
  isUndo: boolean
  undoneAt: string | null
  undoneBy: string | null
  undoable: boolean
  items: PlanChangeItem[]
  more: number
}

export interface PlanUndone {
  ok: true
  reverted: number
  kept: number
  rebuiltDays: number
  rebuildPending: number
  changeId: string
}

export interface PlanRefusal {
  error: string
  pastDays?: number
  count?: number
  employees?: string[]
  maxDays?: number
  kept?: number
}

export function getPlanLocations() {
  return apiRequest<PlanLocation[]>('/api/admin/shift-plan/locations')
}

export function getShiftPlan(locationId: string, from: string, to: string) {
  const q = new URLSearchParams({ locationId, from, to })
  return apiRequest<PlanGrid | PlanRefusal>(`/api/admin/shift-plan?${q}`)
}

export function applyShiftPlan(body: PlanApply) {
  return apiRequest<PlanApplied | PlanPreview | PlanRefusal>('/api/admin/shift-plan/apply', { method: 'POST', body })
}

export function getPlanChanges(locationId: string) {
  return apiRequest<PlanChange[] | PlanRefusal>(`/api/admin/shift-plan/changes?${new URLSearchParams({ locationId })}`)
}

export function undoPlanChange(id: string, confirmPast = false) {
  return apiRequest<PlanUndone | PlanRefusal>(`/api/admin/shift-plan/changes/${id}/undo`, { method: 'POST', body: { confirmPast } })
}
