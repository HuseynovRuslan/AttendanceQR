import type { AttendanceRecord } from '../api/attendance'
import { companyDate } from './att'

/**
 * Today's record as the server last told this phone, kept per person.
 *
 * With no signal the app has nothing to ask, and it used to fill that silence with a claim: the home
 * card and the scan page both read «Hələ giriş etməmisiniz» to somebody who had checked in at 07:38.
 * They believed it, scanned again, and that second tap — queued, replayed later — became their
 * CHECK-OUT at 07:44; the real exit that evening was refused. What the phone knew last is what it
 * should say, labelled with when it knew it; and when it knows nothing it must say that, not «none».
 *
 * localStorage, not IndexedDB: one small row per person, read synchronously during render. Scoped to
 * the company date, so yesterday's answer is never shown as today's.
 */

const KEY = 'attendanceqr.today.'

export interface KnownToday {
  /** Null means the server said: no scan yet today. */
  record: AttendanceRecord | null
  /** When the server said it. */
  atMs: number
}

export function rememberToday(employeeId: string | null | undefined, record: AttendanceRecord | null): void {
  if (!employeeId) return
  try {
    localStorage.setItem(KEY + employeeId, JSON.stringify({ date: companyDate(), record, atMs: Date.now() }))
  } catch {
    // Private mode or a full quota — the app still works, it just cannot remember.
  }
}

export function knownToday(employeeId: string | null | undefined): KnownToday | null {
  if (!employeeId) return null
  try {
    const raw = localStorage.getItem(KEY + employeeId)
    if (!raw) return null
    const v = JSON.parse(raw) as { date?: string; record?: AttendanceRecord | null; atMs?: number }
    if (v.date !== companyDate() || typeof v.atMs !== 'number') return null
    return { record: v.record ?? null, atMs: v.atMs }
  } catch {
    return null
  }
}
