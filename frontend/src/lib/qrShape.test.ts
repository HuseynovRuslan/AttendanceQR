import { describe, expect, it } from 'vitest'
import { ForeignQrDetector, looksLikeQrToken } from './qrShape'

/**
 * The one rule under test, from the file itself: a real token must never be rejected. These tokens
 * are built EXACTLY the way the server's `QrTokenService.Generate` builds them —
 * base64url(`{guid}.{version}.{unixSeconds}.{nonce}.{signature}`), nonce and signature themselves
 * base64url, no padding — so if the generator's shape ever changes, the first test fails and this
 * filter is known to be bricking posters before any worker finds out at a wall.
 */
const b64url = (s: string) => btoa(s).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')

const realToken = (guid = 'fedfbfb7-5a15-4465-b190-dcc63a8e7129', version = 3) =>
  b64url(`${guid}.${version}.${(1_790_000_000).toString()}.${b64url('0123456789abcdef')}.${b64url('some-hmac-bytes-here-32-long!!')}`)

describe('looksLikeQrToken', () => {
  it('accepts a token shaped exactly like the server generates', () => {
    expect(looksLikeQrToken(realToken())).toBe(true)
  })

  it('accepts it with surrounding whitespace — some decoders pad the payload', () => {
    expect(looksLikeQrToken(`  ${realToken()}\n`)).toBe(true)
  })

  it('accepts an uppercase GUID — the server\'s Guid.TryParse does', () => {
    expect(looksLikeQrToken(realToken('FEDFBFB7-5A15-4465-B190-DCC63A8E7129'))).toBe(true)
  })

  it('accepts a large version number — nothing here may be stricter than the server', () => {
    expect(looksLikeQrToken(realToken(undefined, 214748364))).toBe(true)
  })

  it('rejects a URL — the commonest foreign QR in the wild', () => {
    expect(looksLikeQrToken('https://example.com/menyu')).toBe(false)
    expect(looksLikeQrToken('WIFI:T:WPA;S:office;P:12345678;;')).toBe(false)
  })

  it('rejects a partial read — a real token with its tail torn off decodes to too few parts', () => {
    expect(looksLikeQrToken(realToken().slice(0, 40))).toBe(false)
  })

  it('rejects plain garbage and the empty string', () => {
    expect(looksLikeQrToken('')).toBe(false)
    expect(looksLikeQrToken('   ')).toBe(false)
    expect(looksLikeQrToken('§±!@#$%^')).toBe(false)
  })

  it('rejects base64 of the wrong inner shape — five parts but no GUID at the front', () => {
    expect(looksLikeQrToken(b64url('not-a-guid.3.1790000000.nonce.sig'))).toBe(false)
    expect(looksLikeQrToken(b64url('a.b.c.d'))).toBe(false)
  })
})

describe('ForeignQrDetector', () => {
  it('stays silent on noise — every bad frame decodes differently', () => {
    const d = new ForeignQrDetector()
    expect(d.seen('garbage-one')).toBe(false)
    expect(d.seen('garbage-two')).toBe(false)
    expect(d.seen('garbage-three')).toBe(false)
  })

  it('speaks up when the same wrong code is read twice — that is aim, not noise', () => {
    const d = new ForeignQrDetector()
    expect(d.seen('https://restoran-menyusu.az')).toBe(false)
    expect(d.seen('https://restoran-menyusu.az')).toBe(true)
  })

  it('goes quiet again after a reset — a fresh camera session starts clean', () => {
    const d = new ForeignQrDetector()
    d.seen('x'); d.seen('x')
    d.reset()
    expect(d.seen('x')).toBe(false)
  })
})
