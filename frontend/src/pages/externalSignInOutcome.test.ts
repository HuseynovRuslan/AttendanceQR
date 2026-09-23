import { describe, expect, it } from 'vitest'
import {
  EXPIRED_MESSAGE,
  IMPERSONATION_MESSAGE,
  NOT_CONFIGURED_MESSAGE,
  NOT_ELIGIBLE_MESSAGE,
  SERVER_MESSAGE,
  cancelUrl,
  classifySignIn,
  readApp,
  readCode,
  readReturnUrl,
  readSignInQr,
} from './externalSignInOutcome'

const meydan = readApp('meydan')!

describe('readApp', () => {
  it('knows MEYDAN and nothing that is not registered', () => {
    expect(meydan.name).toBe('MEYDAN')
    expect(readApp('kitabxana')).toBeNull()
    expect(readApp('constructor')).toBeNull()
    expect(readApp('__proto__')).toBeNull()
    expect(readApp(null)).toBeNull()
  })
})

describe('readCode', () => {
  it('accepts hex of the right length, lower-cased, and nothing else', () => {
    expect(readCode('0123456789ABCDEF0123456789abcdef')).toBe('0123456789abcdef0123456789abcdef')
    expect(readCode(' 0123456789abcdef ')).toBe('0123456789abcdef')
    expect(readCode('0123456789abcde')).toBeNull()
    expect(readCode('zz23456789abcdef')).toBeNull()
    expect(readCode('0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0')).toBeNull()
    expect(readCode(null)).toBeNull()
  })
})

describe('readReturnUrl', () => {
  it('accepts only https on one of the app’s own hosts', () => {
    // Where the platform lives now, and the address it moved from: both are ours, and the old one redirects here.
    expect(readReturnUrl('https://prizma.qrlog.az/login/qrlog', meydan)).toBe('https://prizma.qrlog.az/login/qrlog')
    expect(readReturnUrl('https://meydan.qrlog.az/login/qrlog', meydan)).toBe('https://meydan.qrlog.az/login/qrlog')
    expect(readReturnUrl('http://prizma.qrlog.az/login/qrlog', meydan)).toBeNull()
    expect(readReturnUrl('https://prizma.qrlog.az.evil.example/', meydan)).toBeNull()
    expect(readReturnUrl('https://meydan.qrlog.az.evil.example/', meydan)).toBeNull()
    expect(readReturnUrl('https://evil.example/?x=meydan.qrlog.az', meydan)).toBeNull()
    expect(readReturnUrl('https://book.qrlog.az/', meydan)).toBeNull()
    expect(readReturnUrl('javascript:alert(1)', meydan)).toBeNull()
    expect(readReturnUrl('https://prizma.qrlog.az/', null)).toBeNull()
  })

  it('adds cancelled=1 without losing the rest', () => {
    expect(cancelUrl('https://meydan.qrlog.az/login/qrlog')).toBe('https://meydan.qrlog.az/login/qrlog?cancelled=1')
    expect(cancelUrl('https://meydan.qrlog.az/login/qrlog?x=1')).toBe('https://meydan.qrlog.az/login/qrlog?x=1&cancelled=1')
  })
})

describe('classifySignIn', () => {
  it('only 204 is a sign-in; every refusal names a way out', () => {
    expect(classifySignIn(204, null, 'MEYDAN')).toEqual({ kind: 'signed-in' })
    expect(classifySignIn(409, { error: 'CodeExpired' }, 'MEYDAN')).toEqual({ kind: 'refused', message: EXPIRED_MESSAGE('MEYDAN') })
    expect(classifySignIn(400, { error: 'InvalidCode' }, 'MEYDAN')).toEqual({ kind: 'refused', message: EXPIRED_MESSAGE('MEYDAN') })
    expect(classifySignIn(403, { error: 'NotEligible' }, 'MEYDAN')).toEqual({ kind: 'refused', message: NOT_ELIGIBLE_MESSAGE })
    expect(classifySignIn(403, { error: 'NotDuringImpersonation' }, 'MEYDAN')).toEqual({ kind: 'refused', message: IMPERSONATION_MESSAGE })
    expect(classifySignIn(503, { error: 'NotConfigured' }, 'MEYDAN')).toEqual({ kind: 'refused', message: NOT_CONFIGURED_MESSAGE })
    expect(classifySignIn(404, { error: 'UnknownApp' }, 'MEYDAN')).toEqual({ kind: 'refused', message: NOT_CONFIGURED_MESSAGE })
    expect(classifySignIn(502, { error: 'AppUnreachable' }, 'MEYDAN')).toEqual({ kind: 'refused', message: SERVER_MESSAGE })
    expect(classifySignIn(500, 'garbage', 'MEYDAN')).toEqual({ kind: 'refused', message: SERVER_MESSAGE })
  })
})

describe('readSignInQr', () => {
  const code = '0123456789abcdef0123456789abcdef'

  it("takes MEYDAN's sign-in QR — QRLog's approval page with the ticket code — and returns only the code", () => {
    expect(readSignInQr(`https://app.qrlog.az/signin/meydan?code=${code}`, meydan)).toBe(code)
    expect(readSignInQr(`  https://app.qrlog.az/signin/meydan?code=${code.toUpperCase()}\n`, meydan)).toBe(code)
  })

  it('never follows a QR that is not exactly that', () => {
    const wrong = [
      `http://app.qrlog.az/signin/meydan?code=${code}`, // not https
      `https://app.qrlog.az.evil.example/signin/meydan?code=${code}`, // someone else's host
      `https://evil.example/signin/meydan?code=${code}`,
      `https://bax.qrlog.az/signin/meydan?code=${code}`, // a tenant host is not where apps point
      `https://app.qrlog.az:8443/signin/meydan?code=${code}`,
      `https://user@app.qrlog.az/signin/meydan?code=${code}`,
      `https://app.qrlog.az/signin/kitabxana?code=${code}`, // another app
      `https://app.qrlog.az/signin/meydan/?code=${code}`,
      `https://app.qrlog.az/SIGNIN/meydan?code=${code}`,
      `https://app.qrlog.az/signin/meydan?code=${code}&return=https%3A%2F%2Fevil.example`, // anything extra
      `https://app.qrlog.az/signin/meydan?code=${code}&code=${code}`,
      `https://app.qrlog.az/signin/meydan?code=${code}#x`,
      'https://app.qrlog.az/signin/meydan?code=not-hex', // not a ticket code
      'https://app.qrlog.az/signin/meydan?code=abc',
      'https://app.qrlog.az/signin/meydan',
      'https://book.qrlog.az/qr/0123456789abcdef', // Kitabxana's QR
      'QRLOG:ATTENDANCE:1234', // an attendance code
      'javascript:alert(1)',
      '',
    ]
    for (const text of wrong) expect(readSignInQr(text, meydan), text).toBeNull()
    expect(readSignInQr(`https://app.qrlog.az/signin/meydan?code=${code}`, null)).toBeNull()
  })

  it('knows how MEYDAN is listed under Xidmətlər', () => {
    expect(meydan.serviceName).toBe('MEYDAN v1')
    expect(meydan.serviceLine).toBe('Müsabiqələrdə iştirak etmək üçün QRLog hesabınızla daxil olun.')
  })
})
