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

function respond(status: number, body = '') {
  vi.stubGlobal('fetch', vi.fn(async () => ({ status, text: async () => body })))
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

  it('is not told about a wrong CURRENT PIN — that 401 comes back on a good token', async () => {
    // POST /api/auth/change-password answers 401 InvalidCurrentPassword without anything being wrong
    // with the session. Bounced, a worker who mistyped their old PIN in «PIN-i dəyiş» was signed out and
    // their crew-phone row dropped, instead of being told «Cari PIN yanlışdır».
    localStorage.setItem('attendanceqr.jwt', 'good-token')
    respond(401, JSON.stringify({ error: 'InvalidCurrentPassword' }))

    const res = await apiRequest('/api/auth/change-password', { method: 'POST', body: {} })

    expect(res).toEqual({ status: 401, data: { error: 'InvalidCurrentPassword' } })
    expect(told).toEqual([])
    expect(getToken()).toBe('good-token')
  })

  it('is still told when the 401 names the token itself', async () => {
    localStorage.setItem('attendanceqr.jwt', 'dead-token')
    respond(401, JSON.stringify({ error: 'InvalidToken' }))

    await apiRequest('/api/auth/change-password', { method: 'POST', body: {} })

    expect(told).toEqual(['dead-token'])
    expect(getToken()).toBeNull()
  })

  it('is still told about a 401 whose body cannot be read — and the call still fails as it did', async () => {
    // Only a body that positively says "the token is fine" spares the session. Anything the client
    // cannot read is the dead session it always was, or a phone would sit on an unusable token.
    localStorage.setItem('attendanceqr.jwt', 'dead-token')
    respond(401, '<html>Unauthorized</html>')

    await expect(apiRequest('/api/attendance/today')).rejects.toThrow(SyntaxError)

    expect(told).toEqual(['dead-token'])
    expect(getToken()).toBeNull()
  })
})
