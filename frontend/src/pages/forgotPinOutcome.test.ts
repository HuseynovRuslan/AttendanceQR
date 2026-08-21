import { describe, expect, it } from 'vitest'
import {
  BUSY_MESSAGE,
  SERVER_MESSAGE,
  classifyAdminRequest,
  classifyVerify,
} from './forgotPinOutcome'

/**
 * These two functions decide what a locked-out employee is told. The promises they carry:
 *  - "Sorğu göndərildi" is never shown on a call the server refused. (It can still be shown for a
 *    request the server silently dropped — the endpoint answers identically on purpose — which is why
 *    that screen's copy stops short of promising an admin will act.)
 *  - A verify that never got a trustworthy answer is NOT reported as "we could not recognise you" —
 *    it is a connection problem, and the way out is to retry, not to take five more selfies.
 */

describe('classifyVerify', () => {
  it('accepts only a 200 that carries verified:true AND a PIN', () => {
    expect(classifyVerify(200, { verified: true, pin: '4821' })).toEqual({ kind: 'verified', pin: '4821' })
  })

  it('treats the server saying no as a rejection — the retry/admin screen', () => {
    expect(classifyVerify(200, { verified: false }).kind).toBe('rejected')
  })

  it('never invents a success out of a malformed 200', () => {
    // verified with no PIN, or a body that is not the shape we expect: there is nothing to show, so
    // it must land on the screen that offers the admin queue, not on a blank "your PIN is …".
    expect(classifyVerify(200, { verified: true }).kind).toBe('rejected')
    expect(classifyVerify(200, null).kind).toBe('rejected')
    expect(classifyVerify(200, { verified: 'true', pin: 1234 }).kind).toBe('rejected')
  })

  it('does not blame the employee for a server that failed', () => {
    for (const status of [500, 502, 503, 400]) {
      expect(classifyVerify(status, null)).toEqual({ kind: 'unreachable', message: SERVER_MESSAGE })
    }
  })

  it('says "wait" on a throttle rather than "we could not recognise you"', () => {
    expect(classifyVerify(429, null)).toEqual({ kind: 'unreachable', message: BUSY_MESSAGE })
  })
})

describe('classifyAdminRequest', () => {
  it('confirms only on the 200 { ok: true } the endpoint answers when it accepted the call', () => {
    expect(classifyAdminRequest(200, { ok: true })).toEqual({ kind: 'accepted' })
    expect(classifyAdminRequest(204, null)).toEqual({ kind: 'accepted' })
  })

  it('never promises an admin reset on a failed call', () => {
    // apiRequest resolves (it does not throw) on a non-2xx, which is exactly how the 📨 screen used
    // to appear after a 500 with nothing filed at all.
    expect(classifyAdminRequest(500, null)).toEqual({ kind: 'failed', message: SERVER_MESSAGE })
    expect(classifyAdminRequest(502, null).kind).toBe('failed')
    expect(classifyAdminRequest(429, null)).toEqual({ kind: 'failed', message: BUSY_MESSAGE })
  })

  it('does not confirm a 2xx that carries an error body', () => {
    expect(classifyAdminRequest(200, { error: 'InvalidIdentifier' }).kind).toBe('failed')
    expect(classifyAdminRequest(200, { ok: false }).kind).toBe('failed')
  })

  it('every failure message is a full Azerbaijani sentence with a next step', () => {
    for (const status of [400, 429, 500, 503]) {
      const outcome = classifyAdminRequest(status, null)
      expect(outcome.kind).toBe('failed')
      if (outcome.kind === 'failed') {
        expect(outcome.message).toMatch(/cəhd edin\.$/)
      }
    }
  })

  it('cannot tell a filed request from one the server silently dropped', () => {
    // The endpoint answers 200 { ok: true } with no write at all for an unknown identifier, a typo,
    // or a per-IP throttle. This is documented, not a bug to fix here — it is why the 📨 screen shows
    // the submitted number and offers a second route instead of promising a reset.
    expect(classifyAdminRequest(200, { ok: true }).kind).toBe('accepted')
  })
})
