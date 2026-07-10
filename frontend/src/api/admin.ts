import { API_BASE_URL, apiRequest, getToken } from './client'
import type { Role } from '../lib/jwt'

// --- reports / today -------------------------------------------------------

export interface DayAttendanceRow {
  employeeId: string
  employeeName: string
  locationId: string
  locationName: string
  status: 'OnTime' | 'Late' | 'Absent' | 'Incomplete' | 'DayOff' | 'OnLeave' | 'Permission'
  checkInAtUtc: string | null
  checkOutAtUtc: string | null
  // Photo audit: today's record id + whether it has a check-in selfie (optional — older backends omit).
  recordId?: string | null
  hasPhoto?: boolean
  // Face audit.
  faceMatchScore?: number | null
  faceMatchStatus?: string
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
  leaveDays: number
  permissionDays: number
}

export interface ReportTotals {
  workDays: number
  lateCount: number
  absentDays: number
  incompleteDays: number
  totalWorkedHours: number
  overtimeHours: number
  leaveDays: number
  permissionDays: number
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

// --- reports / dashboard -----------------------------------------------------

export interface DailyTrendPoint {
  date: string
  checkIns: number
  checkOuts: number
}

export interface WeekdayPoint {
  dayOfWeek: number // 0=Sunday..6=Saturday
  checkIns: number
  checkOuts: number
}

export interface TopLateRow {
  employeeId: string
  employeeName: string
  lateCount: number
  totalLateMinutes: number
}

export interface DashboardReport {
  from: string
  to: string
  scopeLabel: string
  totalCheckIns: number
  totalCheckOuts: number
  lateCount: number
  absentCount: number
  incompleteCount: number
  dayOffCount: number
  leaveCount: number
  permissionCount: number
  totalWorkedHours: number
  overtimeHours: number
  outsideRadiusCount: number
  activeDeviceCount: number
  checkInOutRatio: number
  lateRate: number
  outsideRadiusRate: number
  avgDailyOperations: number
  trend: DailyTrendPoint[]
  weekdayBreakdown: WeekdayPoint[]
  topLate: TopLateRow[]
}

export function getDashboard(from: string, to: string, locationId?: string) {
  const q = new URLSearchParams({ from, to })
  if (locationId) q.set('locationId', locationId)
  return apiRequest<DashboardReport | { error: string }>(`/api/reports/dashboard?${q}`)
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
  // Bitmask indexed by JS Date.getDay() (Sunday=0 ... Saturday=6): bit set = working day.
  // Default 126 = every day except Sunday.
  workDaysMask: number
}

/** Create/update payload — active state is managed separately via setLocationActive. */
export type LocationInput = Omit<AdminLocation, 'id' | 'isActive'>

export function getToday() {
  return apiRequest<DayAttendanceRow[]>('/api/reports/today')
}

// --- problems (rejected scans) ----------------------------------------------

export interface ProblemRow {
  atUtc: string
  employeeId: string | null
  employeeName: string
  /** "Device" = blocked on the phone (no GPS); the scan never reached the server. */
  action: 'CheckIn' | 'CheckOut' | 'Device'
  reason: string
  /** Extra context for some reasons — e.g. the ± metres behind "GpsInaccurate". */
  detail: string | null
}

export interface ReasonCount {
  reason: string
  count: number
}

export interface ProblemsReport {
  date: string
  rejectedCount: number
  successCount: number
  summary: ReasonCount[]
  rows: ProblemRow[]
}

/** GET /api/reports/problems?date=yyyy-MM-dd — who couldn't scan that day, and why. */
export function getProblems(date: string) {
  return apiRequest<ProblemsReport | { error: string }>(`/api/reports/problems?date=${date}`)
}

/** Face audit: re-queue a background face-match for every record that has a check-in photo. */
export function recheckFaces() {
  return apiRequest<{ queued: number } | { error: string }>('/api/admin/attendance/recheck-faces', {
    method: 'POST',
  })
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

export interface StaticQrResult {
  token: string
  expiresAtUtc: string
  locationName: string
}

/** Long-lived (30-day) QR meant to be printed and posted at the location. */
export function generateStaticQr(locationId: string) {
  return apiRequest<StaticQrResult | { error: string }>(`/api/admin/locations/${locationId}/static-qr`, {
    method: 'POST',
  })
}

/** Instantly revokes every outstanding QR (kiosk + any printed poster) for this location. */
export function invalidateLocationQr(locationId: string) {
  return apiRequest<{ locationId: string; qrVersion: number } | { error: string }>(
    `/api/admin/locations/${locationId}/invalidate-qr`,
    { method: 'POST' },
  )
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
  email?: string | null
  phoneNumber?: string | null
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
  phoneNumber: string | null
  role: Role
  locationId: string
  locationName: string | null
  isActive: boolean
  activated: boolean
  hasDevice: boolean
  deviceLabel: string | null
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

/** Photo audit: clear ONE employee's reference selfie — re-seeds on their next check-in. */
export function resetReferencePhoto(id: string) {
  return apiRequest<{ id: string } | { error: string }>(`/api/admin/employees/${id}/reset-reference-photo`, {
    method: 'POST',
  })
}

/** Photo audit: clear ALL employees' reference selfies (e.g. all were the admin's face at setup). */
export function resetAllReferencePhotos() {
  return apiRequest<{ reset: number } | { error: string }>('/api/admin/employees/reset-all-reference-photos', {
    method: 'POST',
  })
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

// --- bound devices ---------------------------------------------------------

export interface DeviceBinding {
  id: string
  employeeId: string
  employeeName: string
  deviceLabel: string | null
  deviceFingerprint: string
  /** How the binding came to exist — "AutoBind" is the one worth a second look. */
  boundVia: 'Activation' | 'AutoBind' | 'AdminApproval'
  boundAtUtc: string
  lastSeenAtUtc: string
}

/** GET /api/admin/device-bindings — every active binding, newest first. An employee holds one per
 * browser context (Safari, the installed PWA), so several rows per person is normal. */
export function getDeviceBindings() {
  return apiRequest<DeviceBinding[]>('/api/admin/device-bindings')
}

/** POST /api/admin/device-bindings/{id}/revoke — kill one context. The row is kept and marked
 * revoked, which is what stops the next scan from silently re-adopting it. */
export function revokeDeviceBinding(id: string) {
  return apiRequest<{ status: string } | { error: string }>(`/api/admin/device-bindings/${id}/revoke`, {
    method: 'POST',
  })
}
