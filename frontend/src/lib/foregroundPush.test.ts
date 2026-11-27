import { afterEach, describe, expect, it } from 'vitest'
import {
  LIVE_COVERS_MS,
  coveredByLive,
  isAttendanceUrl,
  noteLiveItems,
  onForegroundPush,
  receiveForegroundPush,
  resetForegroundPush,
  type ForegroundPush,
} from './foregroundPush'

/**
 * A push that reaches the Android app while it is open is shown by the page — once. What the live channel has just put
 * on screen as a pop-up is not shown again; everything else is.
 */

const T0 = 1_000_000
const arrival = (id = 'p1'): ForegroundPush => ({
  title: 'İşçi giriş etdi',
  body: 'X işə giriş etdi.\nSaat parkı · 14:55',
  url: `/admin/employees/${id}`,
})
const summary: ForegroundPush = { title: 'Davamiyyət', body: '12 yeni bildiriş: 12 çıxış', url: '/admin/notifications' }
const testPush: ForegroundPush = { title: 'QRLog', body: 'Sınaq bildirişi', url: '/home' }

function shown(push: ForegroundPush, now: number): ForegroundPush[] {
  const got: ForegroundPush[] = []
  const off = onForegroundPush((p) => got.push(p))
  receiveForegroundPush(push, now)
  off()
  return got
}

afterEach(() => resetForegroundPush())

describe('foreground push', () => {
  it('shows a push nothing else has shown — the test push, a reminder, an attendance push with no live pop-up', () => {
    expect(shown(testPush, T0)).toEqual([testPush])
    expect(shown({ title: 'İş vaxtınız başlayır', body: '10 dəqiqəyə növbəniz başlayır.', url: '/scan' }, T0)).toHaveLength(1)
    // Somebody who switched the live pop-up off gets no live items — their attendance push is the only word.
    expect(shown(arrival(), T0)).toHaveLength(1)
    expect(shown(summary, T0)).toHaveLength(1)
  })

  it('does not repeat an arrival the live channel has just shown, and shows it again once that is long past', () => {
    noteLiveItems([{ url: '/admin/employees/p1' }], T0)
    expect(shown(arrival('p1'), T0 + 5_000)).toEqual([])
    // Somebody else's arrival was not on screen.
    expect(shown(arrival('p2'), T0 + 5_000)).toHaveLength(1)
    expect(shown(arrival('p1'), T0 + LIVE_COVERS_MS + 1)).toHaveLength(1)
  })

  it('treats a summary push as covered while the pop-ups it sums up are recent', () => {
    noteLiveItems([{ url: '/admin/employees/p1' }, { url: '/admin/employees/p2' }], T0)
    expect(coveredByLive(summary, T0 + 65_000)).toBe(true)
    expect(coveredByLive(summary, T0 + LIVE_COVERS_MS + 1)).toBe(false)
  })

  it('never lets a live pop-up hide a push that is not about attendance', () => {
    noteLiveItems([{ url: '/home' }], T0)
    expect(coveredByLive(testPush, T0)).toBe(false)
    expect(shown(testPush, T0)).toHaveLength(1)
  })

  it('ignores an empty push and stops telling a listener that left', () => {
    expect(shown({ title: '', body: '', url: null }, T0)).toEqual([])
    const got: ForegroundPush[] = []
    const off = onForegroundPush((p) => got.push(p))
    off()
    receiveForegroundPush(testPush, T0)
    expect(got).toEqual([])
  })

  it('reads the attendance feed the way the server and the service worker do', () => {
    expect(isAttendanceUrl('/admin/employees/abc')).toBe(true)
    expect(isAttendanceUrl('/admin/notifications')).toBe(true)
    expect(isAttendanceUrl('/admin/live')).toBe(false)
    expect(isAttendanceUrl('/scan')).toBe(false)
    expect(isAttendanceUrl(null)).toBe(false)
  })
})
