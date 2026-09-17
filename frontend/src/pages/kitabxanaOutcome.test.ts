import { describe, expect, it } from 'vitest'
import {
  EXPIRED_MESSAGE,
  NO_PHONE_MESSAGE,
  SERVER_MESSAGE,
  classifySignIn,
  readCode,
} from './kitabxanaOutcome'

/**
 * This screen vouches for the employee to another site, so the promises are narrow:
 *  - "Təsdiqləndi" is shown for a 204 and for nothing else. Anything the server refused has to say so,
 *    because the employee is looking at a quiz screen in another tab that will simply keep waiting.
 *  - Only a hex code from the address is ever sent onwards; anything else is treated as no code at all.
 */

describe('readCode', () => {
  it('accepts the hex code the quiz mints, lowercased', () => {
    expect(readCode('0123456789ABCDEF')).toBe('0123456789abcdef')
  })

  it('refuses anything that is not that code', () => {
    expect(readCode(null)).toBeNull()
    expect(readCode('')).toBeNull()
    expect(readCode('abc')).toBeNull() // too short to be one
    expect(readCode('0123456789abcdeg')).toBeNull() // g is not hex
    expect(readCode('../../etc/passwd')).toBeNull()
    expect(readCode('0123456789abcdef;drop')).toBeNull()
  })
})

describe('classifySignIn', () => {
  it('reports a sign-in only for 204', () => {
    expect(classifySignIn(204, null)).toEqual({ kind: 'signed-in' })
    expect(classifySignIn(200, { ok: true }).kind).toBe('refused')
  })

  it('tells someone whose code ran out to get a fresh one', () => {
    expect(classifySignIn(409, { error: 'CodeExpired' })).toEqual({ kind: 'refused', message: EXPIRED_MESSAGE })
    expect(classifySignIn(400, { error: 'InvalidCode' })).toEqual({ kind: 'refused', message: EXPIRED_MESSAGE })
  })

  it('does not blame the connection for a decision the server made', () => {
    expect(classifySignIn(400, { error: 'NoPhoneNumber' })).toEqual({ kind: 'refused', message: NO_PHONE_MESSAGE })
    expect(classifySignIn(502, { error: 'KitabxanaUnreachable' })).toEqual({ kind: 'refused', message: SERVER_MESSAGE })
  })
})
