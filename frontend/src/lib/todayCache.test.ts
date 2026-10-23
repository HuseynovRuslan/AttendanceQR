import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { AttendanceRecord } from '../api/attendance'
import { knownToday, rememberToday } from './todayCache'

/**
 * What the phone says about today when it cannot ask.
 *
 * Reported from the field: with no signal the card read «Hələ giriş etməmisiniz» to people who had
 * checked in at 07:38; they scanned again and that scan closed their day. The phone now repeats what
 * the server said last — for today only, per person — and otherwise admits it does not know.
 */

const store = new Map<string, string>()
beforeEach(() => {
  store.clear()
  vi.stubGlobal('localStorage', {
    getItem: (k: string) => store.get(k) ?? null,
    setItem: (k: string, v: string) => void store.set(k, v),
    removeItem: (k: string) => void store.delete(k),
  })
  vi.useFakeTimers()
  vi.setSystemTime(new Date('2026-09-18T04:00:00Z'))   // 08:00 in Baku
})
afterEach(() => {
  vi.useRealTimers()
  vi.unstubAllGlobals()
})

const rec = { recordId: 'r', attendanceDate: '2026-09-18', checkInAtUtc: '2026-09-18T03:38:00Z', checkOutAtUtc: null } as unknown as AttendanceRecord

describe('the last word from the server about today', () => {
  it('is what the phone repeats with no signal, with when it was said', () => {
    rememberToday('emp-1', rec)

    expect(knownToday('emp-1')).toEqual({ record: rec, atMs: Date.parse('2026-09-18T04:00:00Z') })
  })

  it('«no scan yet» is remembered as such — a real answer, not the absence of one', () => {
    rememberToday('emp-1', null)

    expect(knownToday('emp-1')).toEqual({ record: null, atMs: expect.any(Number) })
  })

  it('is never yesterday\'s', () => {
    rememberToday('emp-1', rec)
    vi.setSystemTime(new Date('2026-09-18T21:00:00Z'))   // 01:00 on the 19th in Baku

    expect(knownToday('emp-1')).toBeNull()
  })

  it('belongs to one person — a crew phone does not show A\'s day to B', () => {
    rememberToday('emp-1', rec)

    expect(knownToday('emp-2')).toBeNull()
  })

  it('nothing remembered is nothing known', () => {
    expect(knownToday('emp-1')).toBeNull()
    expect(knownToday(null)).toBeNull()
  })
})
