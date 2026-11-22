import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { apiRequest, getToken, setUnauthorizedHandler } from './client'

/** A Storage good enough for these tests — the browser's is not there under node. */
function memoryStorage(): Storage {
  const m = new Map<string, string>()
  return {
    get length() { return m.size },
    clear: () => m.clear(),
    getItem: (k) => (m.has(k) ? m.get(k)! : null),
    key: (i) => [...m.keys()][i] ?? null,
    removeItem: (k) => { m.delete(k) },
    setItem: (k, v) => { m.set(k, String(v)) },
  }
}

function respond(status: number) {
  vi.stubGlobal('fetch', vi.fn(async () => ({ status, text: async () => '' })))
}

describe('a 401 says which token died', () => {
  let told: (string | null)[] = []
  let slotWhenTold: (string | null)[] = []

  beforeEach(() => {
    vi.stubGlobal('localStorage', memoryStorage())
    told = []
    slotWhenTold = []
    setUnauthorizedHandler((dead) => {
      told.push(dead)
      slotWhenTold.push(getToken())
    })
  })
  afterEach(() => vi.unstubAllGlobals())

  it('hands the handler the refused token, though the active slot is already empty', async () => {
    // The handler used to read getToken() for itself — after clearToken() — and always found null, so
    // the dead account stayed in the crew phone's switcher.
    localStorage.setItem('attendanceqr.jwt', 'dead-token')
    respond(401)

    await apiRequest('/api/attendance/today')

    expect(told).toEqual(['dead-token'])
    expect(slotWhenTold).toEqual([null])
    expect(getToken()).toBeNull()
  })

  it('is not told about a 401 on a call made as another saved profile', async () => {
    localStorage.setItem('attendanceqr.jwt', 'holder-token')
    respond(401)

    await apiRequest('/api/attendance/scan', { method: 'POST', body: {}, token: 'someone-else' })

    expect(told).toEqual([])
    expect(getToken()).toBe('holder-token')
  })
})
