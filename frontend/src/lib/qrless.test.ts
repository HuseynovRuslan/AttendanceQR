import { describe, expect, it } from 'vitest'
import { mayPassOutsideFence, qrlessRoute, recallFence, recallQrless, rememberFence, rememberQrless, type KeyValueStore } from './qrless'

function memoryStore(): KeyValueStore & { map: Map<string, string> } {
  const map = new Map<string, string>()
  return { map, getItem: (k) => map.get(k) ?? null, setItem: (k, v) => void map.set(k, v) }
}

describe('qrlessRoute — decided by place, the way the server decides', () => {
  const OWN = 'loc-socar'
  const OTHER = 'loc-depo'

  it('inside their own poster-less branch → selfie', () => {
    expect(qrlessRoute({ known: true, ownLocationId: OWN, insideId: OWN })).toBe('selfie')
  })

  it("inside ANOTHER branch's fence → the QR camera, because that branch has a poster the server accepts", () => {
    // The review case: a Socar-1 driver sent to the depot. The server fences an empty token to
    // Socar-1 and would answer OutsideRadius with a distance to a branch he was not at.
    expect(qrlessRoute({ known: true, ownLocationId: OWN, insideId: OTHER })).toBe('camera')
  })

  it('inside no known fence at all (the escape hatch) → selfie; the server is the judge either way', () => {
    expect(qrlessRoute({ known: true, ownLocationId: OWN, insideId: null })).toBe('selfie')
    expect(qrlessRoute({ known: true, ownLocationId: OWN, insideId: undefined })).toBe('selfie')
  })

  it('an older server that sends no branch ids → selfie (the fallback list IS the own branch)', () => {
    expect(qrlessRoute({ known: true, ownLocationId: undefined, insideId: 'anything' })).toBe('selfie')
  })

  it('a branch WITH a poster never routes to the selfie, whatever the fence says', () => {
    expect(qrlessRoute({ known: false, ownLocationId: OWN, insideId: OWN })).toBe('camera')
  })

  it('never learned the branch fact → the camera, the default every scan ever made has had', () => {
    expect(qrlessRoute({ known: null, ownLocationId: OWN, insideId: OWN })).toBe('camera')
  })
})

describe('the remembered branch fact', () => {
  it('round-trips per employee — a crew phone carries several people', () => {
    const store = memoryStore()
    rememberQrless('a', true, store)
    rememberQrless('b', false, store)
    expect(recallQrless('a', store)).toBe(true)
    expect(recallQrless('b', store)).toBe(false)
    expect(recallQrless('c', store)).toBeNull()
  })

  it('is null, not false, for a phone that has never heard — unknown must not read as "has a poster"', () => {
    expect(recallQrless('a', memoryStore())).toBeNull()
    expect(recallQrless(null, memoryStore())).toBeNull()
  })

  it('a blocked storage never throws into the scan flow', () => {
    const broken: KeyValueStore = {
      getItem: () => { throw new Error('quota') },
      setItem: () => { throw new Error('quota') },
    }
    expect(() => rememberQrless('a', true, broken)).not.toThrow()
    expect(recallQrless('a', broken)).toBeNull()
  })
})

describe('the fence switch, as the phone sees it', () => {
  it('lets the scan through ONLY when the branch is known to measure rather than refuse', () => {
    // The bug this rule exists for: the wall was switched off in the admin panel, the server would
    // have recorded the check-in, and the phone refused it before the request was ever sent. The
    // test worker at «Aeroport yolu» met «Yeriniz təsdiqlənmədi» with zero rows in the database.
    expect(mayPassOutsideFence(false)).toBe(true)
  })

  it('refuses when the branch is fenced — every ordinary branch, unchanged', () => {
    expect(mayPassOutsideFence(true)).toBe(false)
  })

  it('refuses when the phone has never heard — unknown must not open a fence', () => {
    // Being wrong this way costs one tap on «Yenə də cəhd et». Being wrong the other way would stop
    // every branch in the company refusing anything.
    expect(mayPassOutsideFence(null)).toBe(false)
  })

  it('remembers the fence fact per employee, separately from the poster fact', () => {
    const map = new Map<string, string>()
    const store: KeyValueStore = { getItem: (k) => map.get(k) ?? null, setItem: (k, v) => void map.set(k, v) }

    rememberQrless('a', true, store)
    rememberFence('a', false, store)
    rememberFence('b', true, store)

    expect(recallQrless('a', store)).toBe(true)
    expect(recallFence('a', store)).toBe(false)
    expect(recallFence('b', store)).toBe(true)
    expect(recallFence('c', store)).toBeNull()
  })
})
