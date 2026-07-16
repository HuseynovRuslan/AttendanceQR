import type { AttendanceRecord } from '../api/attendance'

// Attendance-domain helpers. The time/date formatters that used to live here moved to lib/format.ts,
// where someone looking for a time formatter can actually find them.

export function todayStr(): string {
  return new Date().toISOString().slice(0, 10)
}

export function firstName(fullName: string | null | undefined): string {
  return fullName ? fullName.trim().split(/\s+/)[0] : ''
}

export function initials(fullName: string | null | undefined): string {
  if (!fullName) return '?'
  const p = fullName.trim().split(/\s+/)
  return ((p[0]?.[0] ?? '') + (p[1]?.[0] ?? '')).toUpperCase() || '?'
}

export type TodayState =
  | { kind: 'none' }
  | { kind: 'in'; checkIn: string }
  | { kind: 'done'; checkIn: string; checkOut: string }

export function todayState(records: AttendanceRecord[]): TodayState {
  const rec = records.find((r) => r.attendanceDate === todayStr())
  if (!rec?.checkInAtUtc) return { kind: 'none' }
  if (!rec.checkOutAtUtc) return { kind: 'in', checkIn: rec.checkInAtUtc }
  return { kind: 'done', checkIn: rec.checkInAtUtc, checkOut: rec.checkOutAtUtc }
}
