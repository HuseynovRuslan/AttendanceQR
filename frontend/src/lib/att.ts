import type { AttendanceRecord } from '../api/attendance'

export function todayStr(): string {
  return new Date().toISOString().slice(0, 10)
}

export function fmtTime(iso: string | null): string {
  return iso ? new Date(iso).toLocaleTimeString('az-AZ', { hour: '2-digit', minute: '2-digit' }) : '—'
}

export function fmtDate(dateStr: string): string {
  const [y, m, d] = dateStr.split('-')
  return `${d}.${m}.${y}`
}

export function minutesBetween(aIso: string, bIso: string): number {
  return Math.round((new Date(bIso).getTime() - new Date(aIso).getTime()) / 60_000)
}

export function fmtDuration(startIso: string, endIso: string): string {
  const m = Math.max(0, minutesBetween(startIso, endIso))
  return `${Math.floor(m / 60)} saat ${m % 60} dəqiqə`
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
  | { kind: 'in'; checkIn: string; late: boolean }
  | { kind: 'done'; checkIn: string; checkOut: string }

export function todayState(records: AttendanceRecord[]): TodayState {
  const rec = records.find((r) => r.attendanceDate === todayStr())
  if (!rec?.checkInAtUtc) return { kind: 'none' }
  if (!rec.checkOutAtUtc) return { kind: 'in', checkIn: rec.checkInAtUtc, late: rec.status === 'Late' }
  return { kind: 'done', checkIn: rec.checkInAtUtc, checkOut: rec.checkOutAtUtc }
}
