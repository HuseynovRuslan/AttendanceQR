import { apiRequest } from './client'
import { getSummary, type AttendanceReport } from './admin'

export interface AttendanceRecord {
  recordId: string
  attendanceDate: string // "yyyy-MM-dd"
  locationId: string
  checkInAtUtc: string | null
  checkOutAtUtc: string | null
  status: 'OnTime' | 'Late' | 'Absent' | 'Incomplete'
  // Face audit (optional — older backends omit).
  faceMatchScore?: number | null
  faceMatchStatus?: string
}

/** GET /api/attendance/me — this employee's full check-in/out history, newest first. Self-scoped
 * server-side from the JWT, no params. */
export function getMyAttendance() {
  return apiRequest<AttendanceRecord[]>('/api/attendance/me')
}

/** POST /api/attendance/scan-failure — report a scan that never left the phone (no GPS, permission
 * denied, position too coarse). Fire-and-forget: the employee's flow never waits on it, and the
 * server de-duplicates retries — but the attempt now shows up in the admin "Problemlər" screen
 * instead of vanishing. */
export function reportScanFailure(reason: string, accuracyMeters?: number) {
  return apiRequest<void>('/api/attendance/scan-failure', {
    method: 'POST',
    body: { reason, accuracyMeters: accuracyMeters ?? null },
  })
}

export interface MyDeviceStatus {
  bound: boolean
  /** Killed by an admin — a scan will NOT adopt it back; the employee has to ask. */
  revoked: boolean
  deviceLabel: string | null
  boundAtUtc: string | null
  activeDeviceCount: number
  autoBindEnabled: boolean
}

/** GET /api/attendance/me/device — is THIS browser bound to my account? Safari and the installed app
 * are separate contexts, so an employee can be bound in one and not the other without knowing. */
export function getMyDeviceStatus(fingerprint: string) {
  return apiRequest<MyDeviceStatus>(`/api/attendance/me/device?fingerprint=${encodeURIComponent(fingerprint)}`)
}

export interface MyProfile {
  fullName: string
  email: string
  role: string
  position: string | null
  locationName: string | null
}

/** GET /api/attendance/me/profile — the caller's own name/location for the home greeting + menu. */
export function getMyProfile() {
  return apiRequest<MyProfile>('/api/attendance/me/profile')
}

/** GET /api/reports/summary — aggregated totals for this employee over a date range. Employee-role
 * JWTs are forced to their own records server-side regardless of any other param, so this is the
 * same endpoint the admin reports page uses, just naturally self-scoped. */
export function getMySummary(from: string, to: string) {
  return getSummary(from, to)
}

export type { AttendanceReport }

// --- admin: view/correct another employee's raw records ------------------

/** GET /api/attendance/employee/{id} — admin/manager view of one employee's full history. */
export function getEmployeeAttendance(employeeId: string) {
  return apiRequest<AttendanceRecord[] | { error: string }>(`/api/attendance/employee/${employeeId}`)
}

/** Photo-audit: presigned MinIO URLs for a record's check-in selfie + the employee's reference selfie. */
export interface PhotoUrlResponse {
  hasPhoto: boolean
  checkInPhotoUrl: string | null
  checkInPhotoTakenAtUtc: string | null
  referencePhotoUrl: string | null
  faceMatchScore?: number | null
  faceMatchStatus?: string
}

/** GET /api/attendance/{recordId}/photo-url — short-lived (~5 min) presigned URLs for the two photos
 * so the manager/admin can eyeball them side by side. Scoped server-side by LocationScopeRules; the
 * URLs expire, so re-call each time a photo is (re)opened rather than caching them. */
export function getPhotoUrl(recordId: string) {
  return apiRequest<PhotoUrlResponse | { error: string }>(`/api/attendance/${recordId}/photo-url`)
}

export interface AdminAttendanceRecord {
  recordId: string
  employeeId: string
  attendanceDate: string
  checkInAtUtc: string | null
  checkOutAtUtc: string | null
  status: string
}

/** PUT /api/admin/attendance/{recordId} — correct an existing record's check-in/out (either or
 * both; omitted fields are left as-is). Recomputes that date's summary immediately. */
export function adminUpdateRecord(recordId: string, checkInAtUtc?: string, checkOutAtUtc?: string) {
  return apiRequest<AdminAttendanceRecord | { error: string }>(`/api/admin/attendance/${recordId}`, {
    method: 'PUT',
    body: { checkInAtUtc: checkInAtUtc ?? null, checkOutAtUtc: checkOutAtUtc ?? null },
  })
}

/** POST /api/admin/attendance — create a record for a day the employee never scanned at all.
 * checkInAtUtc required; 409 if a record for that (employee, date) already exists. */
export function adminCreateRecord(employeeId: string, date: string, checkInAtUtc: string, checkOutAtUtc?: string) {
  return apiRequest<AdminAttendanceRecord | { error: string }>('/api/admin/attendance', {
    method: 'POST',
    body: { employeeId, date, checkInAtUtc, checkOutAtUtc: checkOutAtUtc ?? null },
  })
}

/** POST /api/admin/attendance/{recordId}/clear-checkout — undo an accidental check-out (record
 * goes back to "checked in, not out" so the employee can check out properly later). */
export function adminClearCheckout(recordId: string) {
  return apiRequest<AdminAttendanceRecord | { error: string }>(`/api/admin/attendance/${recordId}/clear-checkout`, {
    method: 'POST',
  })
}
