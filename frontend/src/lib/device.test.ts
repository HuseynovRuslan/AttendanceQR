import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { getDeviceFingerprint } from './device'

// The device fingerprint is what every scan is matched against. The one invariant that must never
// break: an id already stored is NEVER replaced — otherwise the durability change would re-bind all
// ~100 live employees at once and every one of them would read as a new device.

const store = new Map<string, string>()

beforeEach(() => {
  store.clear()
  vi.stubGlobal('localStorage', {
    getItem: (k: string) => (store.has(k) ? store.get(k)! : null),
    setItem: (k: string, v: string) => void store.set(k, v),
    removeItem: (k: string) => void store.delete(k),
  })
  // idbSet is fire-and-forget; make indexedDB absent so it rejects and is swallowed.
  vi.stubGlobal('indexedDB', undefined)
})
afterEach(() => vi.unstubAllGlobals())

describe('getDeviceFingerprint', () => {
  it('returns the existing id unchanged — never re-mints', () => {
    store.set('attendanceqr.device', 'existing-id-1234')
    expect(getDeviceFingerprint()).toBe('existing-id-1234')
    expect(getDeviceFingerprint()).toBe('existing-id-1234') // stable across calls
    expect(store.get('attendanceqr.device')).toBe('existing-id-1234')
  })

  it('mints exactly one id on first run and reuses it', () => {
    const first = getDeviceFingerprint()
    expect(first).toBeTruthy()
    expect(getDeviceFingerprint()).toBe(first)
    expect(getDeviceFingerprint()).toBe(first)
  })

  it('a stored id is a valid UUID shape (so old + new callers agree)', () => {
    const id = getDeviceFingerprint()
    expect(id).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i)
  })
})
