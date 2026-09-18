import { afterEach, describe, expect, it, vi } from 'vitest'
import { todayAnswered, whyUnreachable } from './attendance'

/**
 * Staging, 2026-09-18 14:18: «📴 İnternet yoxdur» on a phone with full Wi-Fi. /me/today answers 204
 * when there is no scan yet today, and only 200 was read as an answer — so everybody who had not yet
 * checked in was told they had no connection.
 */
describe('reading /me/today', () => {
  it('204 «no scan yet» is an answer, not a lost connection', () => {
    expect(todayAnswered(204)).toBe(true)
    expect(todayAnswered(200)).toBe(true)
  })

  it('an error status is not an answer', () => {
    expect(todayAnswered(500)).toBe(false)
    expect(todayAnswered(401)).toBe(false)
    expect(todayAnswered(0)).toBe(false)
  })
})

describe('what the phone blames', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('says «no internet» only when the phone itself says it is offline', () => {
    vi.stubGlobal('navigator', { onLine: false })
    expect(whyUnreachable()).toBe('offline')
  })

  it('otherwise it is the server that did not answer', () => {
    vi.stubGlobal('navigator', { onLine: true })
    expect(whyUnreachable()).toBe('server')
  })
})
