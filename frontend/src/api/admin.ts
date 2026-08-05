import { API_BASE_URL, apiRequest, getToken } from './client'
import type { Role } from '../lib/jwt'

// --- reports / today -------------------------------------------------------

export interface DayAttendanceRow {
  employeeId: string
  employeeName: string
  locationId: string
  locationName: string
  status: 'OnTime' | 'Late' | 'Absent' | 'Pending' | 'Incomplete' | 'DayOff' | 'OnLeave' | 'Permission'
  checkInAtUtc: string | null
  checkOutAtUtc: string | null
  // Photo audit: today's record id + whether it has a check-in selfie (optional — older backends omit).
  recordId?: string | null
  hasPhoto?: boolean
  // Face audit.
  faceMatchScore?: number | null
  faceMatchStatus?: string
  // Reasons the employee gave for arriving late / leaving early (null if none/skipped).
  lateArrivalReason?: string | null
  earlyDepartureReason?: string | null
  // True when captured offline and synced later — the time is the phone's clock (older backends omit).
  wasOffline?: boolean
  /** Where the employee stood at check-in (scan position); null on older/admin records. */
  checkInLatitude?: number | null
  checkInLongitude?: number | null
  /** The leave type on a leave day — "Vacation"/"Sick"/"Unpaid"/"Permission"/"Rest"/"BusinessTrip"
   *  (Ezamiyyət) — since the status collapses Vacation/Sick/Unpaid/BusinessTrip into OnLeave. Null on
   *  a non-leave day. */
  leaveType?: string | null
  /** Name of the admin/manager who assigned the leave, for attribution on the board. */
  leaveAssignedBy?: string | null
  /** The single-day leave's id — lets the board revert/change it in place. Null for multi-day leaves. */
  leaveId?: string | null
  /** Name of the admin/manager who set THIS record by hand (open-record close, time fix, undo-checkout).
   *  Null for a real scan — the board flags a manually-entered day. */
  manualBy?: string | null
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
  /** Attendance rate for that day (attended ÷ expected, 0–100) — what the sparkline plots. */
  attendanceRate: number
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

/** The attendance board. Omit `date` for the live today board; pass "yyyy-MM-dd" for a past day. */
export function getToday(date?: string) {
  return apiRequest<DayAttendanceRow[]>(`/api/reports/today${date ? `?date=${date}` : ''}`)
}

export interface ExportDayRowInput {
  name: string
  location: string
  status: string
  checkIn: string
  checkOut: string
  photo: string
}

/** POST /api/reports/export-day — send the visible board rows, download a tidy .xlsx. Returns false on
 * failure. Reads the blob directly (not JSON), so it can't ride apiRequest. */
export async function exportDayXlsx(payload: {
  title: string
  date: string
  rows: ExportDayRowInput[]
}): Promise<boolean> {
  const token = getToken()
  try {
    const res = await fetch(`${API_BASE_URL}/api/reports/export-day`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}) },
      body: JSON.stringify(payload),
    })
    if (!res.ok) return false
    const blob = await res.blob()
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `davamiyyet-${payload.date}.xlsx`
    document.body.appendChild(a)
    a.click()
    a.remove()
    URL.revokeObjectURL(url)
    return true
  } catch {
    return false
  }
}

// --- problems (rejected scans) ----------------------------------------------

export interface ProblemRow {
  atUtc: string
  employeeId: string | null
  employeeName: string
  /** The employee's assigned site — so a geofence problem hitting one location is visible as such. */
  locationName: string
  /** "Scan" = rejected before check-in/out was decided (geofence/device/token). "Device" = blocked
   *  on the phone (no GPS), scan never reached the server. */
  action: 'Scan' | 'CheckOut' | 'Device'
  reason: string
  /** Extra context for some reasons — e.g. the ± metres behind "GpsInaccurate". */
  detail: string | null
}

export interface ReasonCount {
  reason: string
  count: number
}

export interface MapGeofence {
  locationName: string
  latitude: number
  longitude: number
  radiusMeters: number
}

export interface ProblemsReport {
  from: string
  to: string
  rejectedCount: number
  successCount: number
  summary: ReasonCount[]
  rows: ProblemRow[]
  /** Geofence circles for sites with an OutsideRadius rejection; the map draws these + the points. */
  geofences: MapGeofence[]
}

/** GET /api/reports/problems?from=…&to=… — who couldn't scan across the range, and why. */
export function getProblems(from: string, to: string) {
  return apiRequest<ProblemsReport | { error: string }>(`/api/reports/problems?from=${from}&to=${to}`)
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

/** One day of an employee's month — the breakdown behind the profile summary tiles. */
export interface EmployeeDay {
  date: string
  status: 'OnTime' | 'Late' | 'Absent' | 'Incomplete' | 'DayOff' | 'OnLeave' | 'Permission'
  checkInAtUtc: string | null
  checkOutAtUtc: string | null
  workedMinutes: number
  lateMinutes: number
  /** Which leave kind, when status is OnLeave — so the day breakdown names it (Xəstəlik / Ezamiyyət /
   *  Məzuniyyət) instead of collapsing all to "Məzuniyyət". Null on a non-leave day. */
  leaveType?: string | null
}

export function getEmployeeDays(employeeId: string, from: string, to: string) {
  const q = new URLSearchParams({ employeeId, from, to })
  return apiRequest<EmployeeDay[]>(`/api/reports/employee-days?${q}`)
}

export function getMyLocations() {
  return apiRequest<LocationDto[]>('/api/reports/my-locations')
}

// --- birthdays (Doğum günləri) ---------------------------------------------

export interface BirthdayRow {
  employeeId: string
  fullName: string
  locationName: string
  birthDate: string
  day: number
  turningAge: number
  isToday: boolean
}

/** This month's birthdays (employees with a full date). Admin only. */
export function getBirthdays() {
  return apiRequest<BirthdayRow[]>('/api/admin/birthdays')
}

// --- payroll (Maaş) --------------------------------------------------------

export interface PayrollRow {
  employeeId: string
  employeeName: string
  locationName: string
  /** Fixed monthly salary in AZN; null = not set (money columns blank). */
  monthlySalary: number | null
  scheduledDays: number
  workDays: number
  absentDays: number
  leaveDays: number
  permissionDays: number
  overtimeHours: number
  perDay: number
  deduction: number
  payable: number
}

export interface PayrollReport {
  from: string
  to: string
  scopeLabel: string
  rows: PayrollRow[]
  totalMonthlySalary: number
  totalDeduction: number
  totalPayable: number
}

/** Payroll table for the period. Admin only (salaries are sensitive) → 403 for a manager. */
export function getPayroll(from: string, to: string, locationId?: string) {
  const q = new URLSearchParams({ from, to })
  if (locationId) q.set('locationId', locationId)
  return apiRequest<PayrollReport | { error: string }>(`/api/reports/payroll?${q}`)
}

/** Streams the payroll .xlsx back as a blob and triggers a browser download. */
export async function downloadPayrollExcel(from: string, to: string, locationId?: string): Promise<void> {
  const q = new URLSearchParams({ from, to })
  if (locationId) q.set('locationId', locationId)
  const token = getToken()
  const res = await fetch(`${API_BASE_URL}/api/reports/payroll/export?${q}`, {
    headers: token ? { Authorization: `Bearer ${token}` } : {},
  })
  if (!res.ok) throw new Error(`payroll export failed: ${res.status}`)
  const blob = await res.blob()
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `maas_${from}_${to}.xlsx`
  document.body.appendChild(a)
  a.click()
  a.remove()
  URL.revokeObjectURL(url)
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
  /** Structured name parts; the backend composes fullName as "firstName lastName" when both are set. */
  firstName?: string | null
  lastName?: string | null
  email?: string | null
  phoneNumber?: string | null
  locationId: string
  role: Role
  fatherName?: string | null
  position?: string | null
  birthYear?: number | null
  /** Full date of birth "yyyy-MM-dd" (preferred over birthYear). */
  birthDate?: string | null
  // Per-employee work hours "HH:mm" (empty/null → the location's shift is used).
  workStart?: string | null
  workEnd?: string | null
  // Fixed monthly salary in AZN for the payroll report; null/omitted → not set.
  monthlySalary?: number | null
  // Waives the check-in selfie. Defaults to FALSE server-side, so every caller must send it or the
  // exemption is silently switched off by an unrelated edit.
  photoExempt?: boolean
  // The named shift this employee is on. Set → it decides hours, days AND rotation, and the three
  // workCycle fields below are ignored server-side. Same null-default rule as photoExempt: omit it on
  // an edit and the assignment is dropped.
  scheduleId?: string | null
  // Rotation, used only when scheduleId is null. Cycle length, how many of its first days are worked,
  // and one date the employee IS working.
  workCycleDays?: number | null
  workCycleOnDays?: number | null
  workCycleAnchor?: string | null
}

export interface AdminEmployee {
  id: string
  fullName: string
  firstName: string | null
  lastName: string | null
  fatherName: string | null
  position: string | null
  birthYear: number | null
  /** Full date of birth "yyyy-MM-dd" (preferred over birthYear); null on rows that only had a year. */
  birthDate?: string | null
  workStart?: string | null
  workEnd?: string | null
  monthlySalary?: number | null
  /** True when an admin has waived the check-in selfie for this employee. */
  photoExempt?: boolean
  /** The named shift they are on, if any, plus its name for display. */
  scheduleId?: string | null
  scheduleName?: string | null
  /** Rotation, used only when scheduleId is null. */
  workCycleDays?: number | null
  workCycleOnDays?: number | null
  workCycleAnchor?: string | null
  /** When the employee accepted the data-processing notice; null = not yet. */
  consentAcceptedAtUtc?: string | null
  email: string | null
  phoneNumber: string | null
  role: Role
  locationId: string
  locationName: string | null
  /** Manager only: the branches they may SEE in reports. Empty on a manager = blank panel. */
  managedLocationIds: string[]
  managedLocationNames: string[]
  isActive: boolean
  activated: boolean
  /** "Son aktivlik" — last time the employee opened the app (home/menu load). null = never opened. */
  lastActiveAtUtc: string | null
  /** True when at least one device is subscribed to push — i.e. announcements actually reach them. */
  pushEnabled?: boolean
  hasDevice: boolean
  deviceLabel: string | null
  boundAtUtc: string | null
  createdAtUtc: string
}

export type EmployeeUpdatePayload = Omit<InvitePayload, never> & {
  isActive: boolean
  /** Manager only. Omit/undefined = leave as-is; [] = clear. Ignored for other roles. */
  managedLocationIds?: string[]
}

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

/** POST /api/admin/employees/{id}/reset-pin — set a random temporary PIN for an activated employee
 * who forgot theirs (a PIN can't be read back — only reset). Returns the temp PIN to pass on. */
export function resetPin(id: string) {
  return apiRequest<{ tempPin: string } | { error: string }>(`/api/admin/employees/${id}/reset-pin`, {
    method: 'POST',
  })
}

// --- bulk invite -----------------------------------------------------------

export interface BulkInviteRow {
  fullName: string
  phoneNumber?: string | null
  position?: string | null
  fatherName?: string | null
  birthYear?: number | null
  /** Per-row branch/role by NAME; null falls back to the batch's LocationId / Role. */
  roleName?: string | null
  locationName?: string | null
}

export interface BulkInvitePayload {
  locationId: string
  role: Role
  rows: BulkInviteRow[]
}

export interface BulkInviteCreated {
  employeeId: string
  fullName: string
  phoneNumber: string | null
  activationToken: string
  activationUrl: string
}

export interface BulkInviteResult {
  createdCount: number
  failedCount: number
  created: BulkInviteCreated[]
  failed: { fullName: string; error: string }[]
}

/** POST /api/admin/employees/bulk-invite — add many employees at once (one shared location + role).
 * Each row is validated independently; failures come back per-row without blocking the rest. */
export function bulkInvite(payload: BulkInvitePayload) {
  return apiRequest<BulkInviteResult | { error: string }>('/api/admin/employees/bulk-invite', {
    method: 'POST',
    body: payload,
  })
}

export interface BulkImportCreated {
  employeeId: string
  fullName: string
  phoneNumber: string | null
  /** Temporary PIN to hand out — the employee sets their own on first login. */
  tempPin: string
}

export interface BulkImportResult {
  createdCount: number
  failedCount: number
  created: BulkImportCreated[]
  failed: { fullName: string; error: string }[]
}

/** POST /api/admin/employees/bulk-import — add many employees at once, each ACTIVATED with a temporary
 * PIN (no activation link). The employee signs in with phone + temp PIN and is forced to set their own. */
export function bulkImport(payload: BulkInvitePayload) {
  return apiRequest<BulkImportResult | { error: string }>('/api/admin/employees/bulk-import', {
    method: 'POST',
    body: payload,
  })
}

// --- missed-checkout requests (forgot to scan out) -------------------------

export interface MissedCheckoutPending {
  id: string
  employeeName: string
  locationName: string
  attendanceDate: string
  requestedCheckOutAtUtc: string
  reason: string
  requestedAtUtc: string
  /** This employee's self-reports this month — the visibility deterrent. */
  monthlyCount: number
}

/** GET /api/admin/missed-checkout/pending — forgotten-checkout requests awaiting review, scoped to the
 * caller's locations (managers) or all (admin). */
export function getMissedCheckoutPending() {
  return apiRequest<MissedCheckoutPending[]>('/api/admin/missed-checkout/pending')
}

/** POST .../approve — write the requested check-out onto the record and recompute that day. */
export function approveMissedCheckout(id: string) {
  return apiRequest<{ status: string } | { error: string }>(`/api/admin/missed-checkout/${id}/approve`, {
    method: 'POST',
  })
}

/** POST .../reject — decline the request (the day stays open for the admin to handle). */
export function rejectMissedCheckout(id: string) {
  return apiRequest<{ status: string } | { error: string }>(`/api/admin/missed-checkout/${id}/reject`, {
    method: 'POST',
  })
}

/** What the server read out of an uploaded .xlsx. Every field the single-employee form collects —
 *  Rol/Filial arrive as the NAMES the admin typed, resolved server-side at import. */
export interface ParsedXlsxRow {
  fullName: string
  phoneNumber: string | null
  position: string | null
  fatherName: string | null
  birthYear: number | null
  email: string | null
  roleName: string | null
  locationName: string | null
}

/** GET /api/admin/employees/xlsx-template — download a ready-to-fill .xlsx with the expected columns.
 * Returns false on failure. Authed, so it fetches the blob rather than using a plain link. */
export async function downloadXlsxTemplate(): Promise<boolean> {
  const token = getToken()
  try {
    const res = await fetch(`${API_BASE_URL}/api/admin/employees/xlsx-template`, {
      headers: token ? { Authorization: `Bearer ${token}` } : {},
    })
    if (!res.ok) return false
    const blob = await res.blob()
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = 'isciler-sablon.xlsx'
    document.body.appendChild(a)
    a.click()
    a.remove()
    URL.revokeObjectURL(url)
    return true
  } catch {
    return false
  }
}

/** POST /api/admin/employees/parse-xlsx — upload an .xlsx and get its rows back (parsing only, creates
 * nothing). Multipart, so it can't ride the JSON apiRequest helper. */
export async function parseXlsx(file: File): Promise<{ status: number; rows: ParsedXlsxRow[] }> {
  const form = new FormData()
  form.append('file', file)
  const token = getToken()
  try {
    const res = await fetch(`${API_BASE_URL}/api/admin/employees/parse-xlsx`, {
      method: 'POST',
      headers: token ? { Authorization: `Bearer ${token}` } : {},
      body: form,
    })
    const data = res.ok ? await res.json() : null
    return { status: res.status, rows: (data?.rows as ParsedXlsxRow[]) ?? [] }
  } catch {
    return { status: 0, rows: [] }
  }
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

// --- PIN reset requests ("PIN-i unutdum") ----------------------------------

export interface PendingPinReset {
  requestId: string
  employeeId: string
  employeeName: string
  phoneNumber: string | null
  email: string | null
  requestedAtUtc: string
}

export function getPendingPinResets() {
  return apiRequest<PendingPinReset[]>('/api/admin/pin-resets')
}

/** Resets the employee's PIN and closes the request; returns the temporary PIN to pass on. */
export function resolvePinReset(id: string) {
  return apiRequest<{ tempPin: string } | { error: string }>(`/api/admin/pin-resets/${id}/resolve`, {
    method: 'POST',
  })
}

export function dismissPinReset(id: string) {
  return apiRequest<{ status: string } | { error: string }>(`/api/admin/pin-resets/${id}/dismiss`, {
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

// --- super-admin: the companies themselves ----------------------------------

/** One tenant, with the numbers that say whether it is actually in use. */
export interface SuperTenant {
  id: string
  slug: string
  displayName: string
  color: string | null
  logoUrl: string | null
  isActive: boolean
  createdAtUtc: string
  host: string
  employeeCount: number
  locationCount: number
  /** "yyyy-MM-dd" of the last scan, or null if nobody has ever scanned. */
  lastScanDate: string | null
  plan: string | null
  maxEmployees: number | null
  maxLocations: number | null
  /** Feature keys turned OFF for this tenant (see /super/features for the catalogue). */
  disabledFeatures: string[]
}

export interface CreateTenantInput {
  slug: string
  displayName?: string
  adminName?: string
  adminPhone?: string
  /** 4 digits; omit to have one generated. */
  adminPin?: string
  locationName?: string
  latitude?: number
  longitude?: number
}

export interface CreateTenantResult {
  id: string
  slug: string
  host: string
  adminPhone: string
  /** Shown once — there is no way to read it back, only to reset it. */
  tempPin: string
}

/** Whether this account may manage tenants. Asked before showing the menu item, so the panel never
 *  offers a screen that would only 403. */
export function getIsSuperAdmin() {
  return apiRequest<{ isSuperAdmin: boolean }>('/api/super/me')
}

export function getSuperTenants() {
  return apiRequest<SuperTenant[] | { error: string }>('/api/super/tenants')
}

export interface SuperDashboard {
  totalTenants: number
  activeTenants: number
  totalEmployees: number
  checkInsToday: number
  checkInsThisMonth: number
  attention: { id: string; slug: string; displayName: string; reason: string }[]
}

/** GET /api/super/dashboard — platform-wide KPIs + a short "needs attention" list. */
export function getSuperDashboard() {
  return apiRequest<SuperDashboard | { error: string }>('/api/super/dashboard')
}

export interface SuperAuditEntry {
  id: string
  actorName: string
  action: string
  targetTenantId: string | null
  targetTenantSlug: string | null
  details: string | null
  ipAddress: string | null
  createdAtUtc: string
}

/** GET /api/super/audit — the platform action trail, most recent first. */
export function getSuperAudit(take = 100) {
  return apiRequest<SuperAuditEntry[] | { error: string }>(`/api/super/audit?take=${take}`)
}

export interface SuperUser {
  id: string
  tenantId: string
  tenantSlug: string | null
  tenantName: string | null
  fullName: string
  phone: string | null
  email: string | null
  role: string
  isActive: boolean
  mustChangePin: boolean
  lastActiveAtUtc: string | null
}

/** GET /api/super/users?q= — find any employee across all tenants (name/phone/email, min 2 chars). */
export function searchSuperUsers(q: string) {
  return apiRequest<SuperUser[] | { error: string }>(`/api/super/users?q=${encodeURIComponent(q)}`)
}

/** Reset a person's PIN (returns a one-time temp PIN) and revoke their sessions. */
export function resetSuperUserPin(id: string) {
  return apiRequest<{ id: string; tempPin: string } | { error: string }>(`/api/super/users/${id}/reset-pin`, { method: 'POST' })
}

/** Turn an account back on (undo a kill-switch that left a tenant headless). */
export function reactivateSuperUser(id: string) {
  return apiRequest<{ id: string; isActive: boolean } | { error: string }>(`/api/super/users/${id}/reactivate`, { method: 'POST' })
}

/** Invalidate every token the person holds, without touching their PIN. */
export function revokeSuperUserSessions(id: string) {
  return apiRequest<{ id: string } | { error: string }>(`/api/super/users/${id}/revoke-sessions`, { method: 'POST' })
}

export interface ImpersonateResult {
  token: string
  tenantSlug: string
  tenantName: string
  adminName: string
  expiresInMinutes: number
}

/** POST /api/super/tenants/{id}/impersonate — mint a short-lived session as this company's admin. */
export function impersonateTenant(id: string) {
  return apiRequest<ImpersonateResult | { error: string }>(`/api/super/tenants/${id}/impersonate`, { method: 'POST' })
}

export interface SuperFeature {
  key: string
  label: string
}

/** GET /api/super/features — the togglable feature catalogue (key + Azerbaijani label). */
export function getSuperFeatures() {
  return apiRequest<SuperFeature[] | { error: string }>('/api/super/features')
}

export interface TenantPlanInput {
  plan: string | null
  maxEmployees: number | null
  maxLocations: number | null
  /** Feature keys to turn OFF. */
  disabledFeatures: string[]
}

/** PUT /api/super/tenants/{id}/plan — set plan, soft limits and disabled features in one call. */
export function setTenantPlan(id: string, input: TenantPlanInput) {
  return apiRequest<{ id: string } | { error: string }>(`/api/super/tenants/${id}/plan`, { method: 'PUT', body: input })
}

export function createTenant(input: CreateTenantInput) {
  return apiRequest<CreateTenantResult | { error: string }>('/api/super/tenants', { method: 'POST', body: input })
}

export function setTenantActive(id: string, isActive: boolean) {
  return apiRequest<{ id: string; isActive: boolean } | { error: string }>(`/api/super/tenants/${id}/active`, {
    method: 'PUT',
    body: { isActive },
  })
}

export function setTenantBranding(id: string, input: { displayName?: string; color?: string; logoUrl?: string }) {
  return apiRequest<{ id: string } | { error: string }>(`/api/super/tenants/${id}/branding`, {
    method: 'PUT',
    body: input,
  })
}

// --- schedules (qrafik) — reusable shift templates for the location form ------

/** A named shift ("növbə"): hours, working days and an optional rotation, assigned to employees.
 *  Live, not a template — editing one changes how everyone on it is judged, past days included. */
export interface Schedule {
  id: string
  name: string
  shiftStart: string // "HH:mm"
  shiftEnd: string
  lateThresholdMinutes: number
  workDaysMask: number
  /** Rotation; null = none and workDaysMask decides. */
  workCycleDays: number | null
  workCycleOnDays: number | null
  workCycleAnchor: string | null
  isOvernight: boolean
}

export type ScheduleInput = Omit<Schedule, 'id' | 'isOvernight'>

export function getSchedules() {
  return apiRequest<Schedule[]>('/api/admin/schedules')
}

export function createSchedule(input: ScheduleInput) {
  return apiRequest<Schedule | { error: string }>('/api/admin/schedules', { method: 'POST', body: input })
}

export function updateSchedule(id: string, input: ScheduleInput) {
  return apiRequest<Schedule | { error: string }>(`/api/admin/schedules/${id}`, { method: 'PUT', body: input })
}

export function deleteSchedule(id: string) {
  return apiRequest<{ deleted: string } | { error: string }>(`/api/admin/schedules/${id}`, { method: 'DELETE' })
}

// --- Aylıq Tabel ------------------------------------------------------------

export interface TabelLegendItem {
  code: string
  label: string
}

export interface TabelRow {
  employeeId: string
  employeeName: string
  position: string | null
  locationName: string
  /** One code per day of the month, index 0 = day 1. Empty string = a day not yet reached. */
  days: string[]
  workedDays: number
  absentDays: number
  leaveDays: number
  workedHours: number
}

export interface TabelReport {
  year: number
  month: number
  scopeLabel: string
  daysInMonth: number
  rows: TabelRow[]
  legend: TabelLegendItem[]
}

/** The monthly timesheet grid. Manager-visible (scoped server-side to their own branch). */
export function getTabel(year: number, month: number, locationId?: string) {
  const q = new URLSearchParams({ year: String(year), month: String(month) })
  if (locationId) q.set('locationId', locationId)
  return apiRequest<TabelReport | { error: string }>(`/api/reports/tabel?${q}`)
}

/** Streams the tabel .xlsx back as a blob and triggers a browser download. */
export async function downloadTabelExcel(year: number, month: number, locationId?: string): Promise<void> {
  const q = new URLSearchParams({ year: String(year), month: String(month) })
  if (locationId) q.set('locationId', locationId)
  const token = getToken()
  const res = await fetch(`${API_BASE_URL}/api/reports/tabel/export?${q}`, {
    headers: token ? { Authorization: `Bearer ${token}` } : {},
  })
  if (!res.ok) throw new Error(`tabel export failed: ${res.status}`)
  const blob = await res.blob()
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `tabel_${year}_${String(month).padStart(2, '0')}.xlsx`
  document.body.appendChild(a)
  a.click()
  a.remove()
  URL.revokeObjectURL(url)
}
