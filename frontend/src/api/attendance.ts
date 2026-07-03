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
