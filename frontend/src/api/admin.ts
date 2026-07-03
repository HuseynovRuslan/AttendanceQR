import { API_BASE_URL, apiRequest, getToken } from './client'
import type { Role } from '../lib/jwt'

// --- reports / today -------------------------------------------------------

export interface DayAttendanceRow {
  employeeId: string
  employeeName: string
  locationName: string
  status: 'OnTime' | 'Late' | 'Absent' | 'Incomplete'
  checkInAtUtc: string | null
  checkOutAtUtc: string | null
}

export interface EmployeeReportRow {
  employeeId: string
  employeeName: string
  locationName: string
  workDays: number
  lateCount: number
  absentDays: number
  incompleteDays: number
  totalWorkedHours: number
  overtimeHours: number
}

export interface ReportTotals {
  workDays: number
  lateCount: number
  absentDays: number
  incompleteDays: number
  totalWorkedHours: number
  overtimeHours: number
}

export interface AttendanceReport {
  from: string
  to: string
  scopeLabel: string
  rows: EmployeeReportRow[]
  totals: ReportTotals
}

export interface LocationDto {
  id: string
  name: string
}

/** Full location shape returned by the admin location-management endpoints. */
export interface AdminLocation {
  id: string
  name: string
  latitude: number
  longitude: number
  radiusMeters: number
  shiftStart: string // "HH:mm"
  shiftEnd: string // "HH:mm"
  lateThresholdMinutes: number
  isActive: boolean
}

/** Create/update payload — active state is managed separately via setLocationActive. */
export type LocationInput = Omit<AdminLocation, 'id' | 'isActive'>

export function getToday() {
  return apiRequest<DayAttendanceRow[]>('/api/reports/today')
}

export function getSummary(from: string, to: string, locationId?: string) {
  const q = new URLSearchParams({ from, to })
  if (locationId) q.set('locationId', locationId)
  return apiRequest<AttendanceReport | { error: string }>(`/api/reports/summary?${q}`)
}

export function getMyLocations() {
  return apiRequest<LocationDto[]>('/api/reports/my-locations')
}

export function getAdminLocations() {
  return apiRequest<AdminLocation[]>('/api/admin/locations')
}

export function createLocation(input: LocationInput) {
  return apiRequest<AdminLocation | { error: string }>('/api/admin/locations', {
    method: 'POST',
    body: input,
  })
}

export function updateLocation(id: string, input: LocationInput) {
  return apiRequest<AdminLocation | { error: string }>(`/api/admin/locations/${id}`, {
    method: 'PUT',
    body: input,
  })
}

export function deleteLocation(id: string) {
  return apiRequest<{ deleted: string } | { error: string }>(`/api/admin/locations/${id}`, {
    method: 'DELETE',
  })
}

export function setLocationActive(id: string, isActive: boolean) {
  return apiRequest<AdminLocation | { error: string }>(`/api/admin/locations/${id}/active`, {
    method: 'PUT',
    body: { isActive },
  })
}

/** Streams the .xlsx back as a blob and triggers a browser download. */
export async function downloadReportExcel(from: string, to: string, locationId?: string): Promise<void> {
  const q = new URLSearchParams({ from, to })
  if (locationId) q.set('locationId', locationId)
  const token = getToken()
  const res = await fetch(`${API_BASE_URL}/api/reports/summary/export?${q}`, {
    headers: token ? { Authorization: `Bearer ${token}` } : {},
  })
  if (!res.ok) throw new Error(`export failed: ${res.status}`)
  const blob = await res.blob()
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `attendance_${from}_${to}.xlsx`
  document.body.appendChild(a)
  a.click()
  a.remove()
  URL.revokeObjectURL(url)
}

// --- employees -------------------------------------------------------------

export interface InviteResult {
  employeeId: string
  activationToken: string
  activationUrl: string
}

export interface InvitePayload {
  fullName: string
  email: string
  locationId: string
  role: Role
  fatherName?: string | null
  position?: string | null
  birthYear?: number | null
}

export interface AdminEmployee {
  id: string
  fullName: string
  fatherName: string | null
  position: string | null
  birthYear: number | null
  email: string
  role: Role
  locationId: string
  locationName: string | null
  isActive: boolean
  activated: boolean
  hasDevice: boolean
  boundAtUtc: string | null
  createdAtUtc: string
}

export type EmployeeUpdatePayload = Omit<InvitePayload, never> & { isActive: boolean }

export function getEmployees() {
  return apiRequest<AdminEmployee[]>('/api/admin/employees')
}

export function invite(payload: InvitePayload) {
  return apiRequest<InviteResult | { error: string }>('/api/admin/employees/invite', {
    method: 'POST',
    body: payload,
  })
}

export function updateEmployee(id: string, payload: EmployeeUpdatePayload) {
  return apiRequest<{ id: string } | { error: string }>(`/api/admin/employees/${id}`, {
    method: 'PUT',
    body: payload,
  })
}

export function deleteEmployee(id: string, force = false) {
  const q = force ? '?force=true' : ''
  return apiRequest<{ deleted: string; forced: boolean } | { error: string }>(`/api/admin/employees/${id}${q}`, {
    method: 'DELETE',
  })
}

export function reinviteEmployee(id: string) {
  return apiRequest<InviteResult | { error: string }>(`/api/admin/employees/${id}/reinvite`, {
    method: 'POST',
  })
}

/** Testing helper — clears an employee's check-in/check-out history so the same account +
 * device can be used to re-test the scan flow. Keeps the account and device binding. */
export function resetEmployeeAttendance(id: string) {
  return apiRequest<{ attendanceRecordsDeleted: number; summariesDeleted: number } | { error: string }>(
    `/api/admin/employees/${id}/reset-attendance`,
    { method: 'POST' },
  )
}

// --- device changes --------------------------------------------------------

export interface PendingDeviceChange {
  requestId: string
  employeeId: string
  employeeName: string
  currentDeviceFingerprint: string | null
  newDeviceFingerprint: string
  requestedAtUtc: string
}

export function getPendingDeviceChanges() {
  return apiRequest<PendingDeviceChange[]>('/api/admin/device-change/pending')
}

export function approveDeviceChange(id: string) {
  return apiRequest<{ status: string } | { error: string }>(`/api/admin/device-change/${id}/approve`, {
    method: 'POST',
  })
}

export function rejectDeviceChange(id: string) {
  return apiRequest<{ status: string } | { error: string }>(`/api/admin/device-change/${id}/reject`, {
    method: 'POST',
  })
}
