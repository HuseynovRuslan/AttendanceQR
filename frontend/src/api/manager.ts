import { apiRequest } from './client'
import type { LeaveBatchResult } from './leaves'

// A manager's own write surface. Every call is scoped server-side to the branches they oversee, so
// the client never has to (and never should) enforce scope itself — it only shows what it is given.

export interface ManagerLocation {
  id: string
  name: string
}

export interface ManagerEmployee {
  id: string
  /** The manager's own row — only ever present when the caller asked for it (the leave form does). */
  isSelf?: boolean
  /** May this manager CHANGE this row? Their branch AND plain staff — the same rule every write
   *  re-checks server-side. A colleague's row is visible and read-only. */
  manageable?: boolean
  /** Not ordinary staff — another manager, or the admin who clocks in at this branch. */
  isColleague?: boolean
  fullName: string
  firstName: string | null
  lastName: string | null
  fatherName: string | null
  position: string | null
  phoneNumber: string | null
  /** Null for phone-only employees (email became optional). */
  email: string | null
  locationId: string
  locationName: string
  birthDate: string | null
  birthYear: number | null
  workStart: string | null
  workEnd: string | null
  /** The named shift they are on; set → it decides hours, days and rotation. */
  scheduleId: string | null
  /** Rotation, used only when scheduleId is null. */
  workCycleDays: number | null
  workCycleOnDays: number | null
  workCycleAnchor: string | null
  photoExempt: boolean
  canFieldCheckIn: boolean
  /** Read-only for a manager. Whether an account may ride on somebody else's phone is granted here in
   *  bulk over their own staff, but the per-employee edit form does not carry it — see the manager
   *  projection in ManagerController for why. */
  canShareDevice: boolean
  isActive: boolean
  activated: boolean
}

// No salary, no role — a manager sets neither, so the shape carries neither.
export interface ManagerEmployeeInput {
  fullName: string
  firstName?: string | null
  lastName?: string | null
  email: string | null
  phoneNumber: string | null
  fatherName: string | null
  position: string | null
  locationId: string
  birthDate: string | null
  birthYear: number | null
  workStart: string | null
  workEnd: string | null
  /** The named shift they are on; set → it decides hours, days and rotation. */
  scheduleId: string | null
  /** Rotation, used only when scheduleId is null. */
  workCycleDays: number | null
  workCycleOnDays: number | null
  workCycleAnchor: string | null
  photoExempt: boolean
  /** Field/mobile check-in permission — see EmployeeUpdateRequest. Round-tripped so a manager edit
   *  preserves it (managers don't toggle it in their form, mirroring photoExempt). */
  canFieldCheckIn: boolean
  isActive: boolean
}

export function getManagerLocations() {
  return apiRequest<ManagerLocation[]>('/api/manager/locations')
}

export function getManagerPositions() {
  return apiRequest<{ name: string }[]>('/api/manager/positions')
}

/** `includeSelf` adds the manager's OWN row — the leave form needs it, since a manager may file their
 *  own absence. Every other screen leaves it off: those buttons refuse a non-Employee row anyway. */
export function getManagerEmployees(includeSelf = false) {
  return apiRequest<ManagerEmployee[]>(`/api/manager/employees${includeSelf ? '?includeSelf=true' : ''}`)
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

/** The manager's own version — same body, narrowed server-side to their branches' plain staff. */
export function bulkPermissionAsManager(
  employeeIds: string[],
  permission: 'ShareDevice' | 'FieldCheckIn',
  allowed: boolean,
) {
  return apiRequest<{ changed: number; total: number; skipped: number } | { error: string }>(
    '/api/manager/employees/bulk-permission',
    { method: 'POST', body: { employeeIds, permission, allowed } },
  )
}

/** ONE employee at this manager's branches — what the shared profile screen loads instead of the
 *  admin roster, which is unscoped and carries monthlySalary. */
export function getManagerEmployee(id: string) {
  return apiRequest<(ManagerEmployee & { manageable: boolean }) | { error: string }>(
    `/api/manager/employees/${id}`,
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

/** A name the leave form may file against: this manager's staff, their colleagues at the same
 *  branches (a fellow manager, an admin who works there) and themselves. Deliberately separate from
 *  getManagerEmployees — that one carries phone, email and birth date, which a manager has no reason
 *  to see for a peer. */
export interface LeaveSubject {
  id: string
  fullName: string
  position: string | null
  isSelf: boolean
  isColleague: boolean
}

export function getLeaveSubjects() {
  return apiRequest<LeaveSubject[]>('/api/manager/leave-subjects')
}

export function getManagerLeaves(from?: string, to?: string) {
  const q = new URLSearchParams()
  if (from) q.set('from', from)
  if (to) q.set('to', to)
  const qs = q.toString()
  return apiRequest<ManagerLeave[]>(`/api/manager/leaves${qs ? `?${qs}` : ''}`)
}

export function createManagerLeave(input: {
  employeeIds: string[]
  fromDate: string
  toDate: string
  type: string
  note: string | null
}) {
  return apiRequest<LeaveBatchResult | { error: string; skipped?: LeaveBatchResult['skipped'] }>(
    '/api/manager/leaves',
    {
      method: 'POST',
      body: { ...input, employeeId: input.employeeIds[0] },
    },
  )
}

export function deleteManagerLeave(id: string) {
  return apiRequest<{ deleted: string } | { error: string }>(`/api/manager/leaves/${id}`, {
    method: 'DELETE',
  })
}

/** The named shifts, read-only — a manager assigns people to a shift but does not define one. */
export interface ManagerSchedule {
  id: string
  name: string
  shiftStart: string
  shiftEnd: string
  lateThresholdMinutes: number
  workDaysMask: number
  workCycleDays: number | null
  workCycleOnDays: number | null
  workCycleAnchor: string | null
  isOvernight: boolean
  /** Days whose hours differ, keyed by day number as a string ("0" = Sunday … "6" = Saturday). */
  dayHours: Record<string, { start: string; end: string }>
}

export function getManagerSchedules() {
  return apiRequest<ManagerSchedule[]>('/api/manager/schedules')
}

/** Create/update payload for a shift — same shape the admin path uses. */
export interface ManagerScheduleInput {
  name: string
  shiftStart: string
  shiftEnd: string
  lateThresholdMinutes: number
  workDaysMask: number
  workCycleDays: number | null
  workCycleOnDays: number | null
  workCycleAnchor: string | null
}

export function createManagerSchedule(input: ManagerScheduleInput) {
  return apiRequest<{ id: string } | { error: string }>('/api/manager/schedules', { method: 'POST', body: input })
}

/** Refused (403 ScheduleUsedOutsideBranch) while anyone outside this manager's branches is on it —
 *  editing a shift re-judges past days for everyone on it. */
export function updateManagerSchedule(id: string, input: ManagerScheduleInput) {
  return apiRequest<{ id: string } | { error: string }>(`/api/manager/schedules/${id}`, { method: 'PUT', body: input })
}

export function deleteManagerSchedule(id: string) {
  return apiRequest<{ deleted: string } | { error: string }>(`/api/manager/schedules/${id}`, { method: 'DELETE' })
}
