import { apiRequest } from './client'
import { getSummary, type AttendanceReport } from './admin'

export interface AttendanceRecord {
  recordId: string
  attendanceDate: string // "yyyy-MM-dd"
  locationId: string
  checkInAtUtc: string | null
  checkOutAtUtc: string | null
  status: 'OnTime' | 'Late' | 'Absent' | 'Incomplete'
}

/** GET /api/attendance/me — this employee's full check-in/out history, newest first. Self-scoped
 * server-side from the JWT, no params. */
export function getMyAttendance() {
  return apiRequest<AttendanceRecord[]>('/api/attendance/me')
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
