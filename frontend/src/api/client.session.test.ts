import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { apiRequest, endedSessionNotice, forgetEndedSessionNotice, setUnauthorizedHandler } from './client'

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

function respond(status: number, body: unknown) {
  vi.stubGlobal('fetch', vi.fn(async () => ({ status, text: async () => JSON.stringify(body) })))
}

const SUPPORT = { tenantName: 'Bakı Abadlıq Xidməti', adminName: 'Vüqar Bəbirov' }

function impersonating(info: object = SUPPORT, from: string | null = '/tenants') {
  localStorage.setItem('attendanceqr.jwt', 'impersonation-token')
  localStorage.setItem('attendanceqr.jwt.super', 'operator-token')
  localStorage.setItem('attendanceqr.impersonation', JSON.stringify(info))
  if (from) localStorage.setItem('attendanceqr.impersonation.from', from)
}

describe('a support session the server no longer accepts', () => {
  let signedOut = 0
  let navigations: string[] = []

  beforeEach(() => {
    vi.stubGlobal('localStorage', memoryStorage())
    vi.stubGlobal('sessionStorage', memoryStorage())
    navigations = []
    vi.stubGlobal('window', { location: { pathname: '/admin/today', set href(v: string) { navigations.push(v) } } })
    signedOut = 0
    setUnauthorizedHandler(() => { signedOut++ })
  })
  afterEach(() => vi.unstubAllGlobals())

  it("hands the operator back their own panel, as the banner's «Çıx» does", async () => {
    // 03.10.2026: the board said «Məlumat yüklənmədi» under a support banner for nearly two hours
    // after the session had run out, because the refusal was a 400 and only a 401 ended a session.
    impersonating()
    respond(400, { error: 'TenantUnresolved' })

    await apiRequest('/api/reports/today')

    expect(localStorage.getItem('attendanceqr.jwt')).toBe('operator-token')
    expect(localStorage.getItem('attendanceqr.jwt.super')).toBeNull()
    expect(localStorage.getItem('attendanceqr.impersonation')).toBeNull()
    expect(navigations).toEqual(['/tenants'])
    expect(signedOut).toBe(0)
    expect(endedSessionNotice()).toBe('Dəstək rejiminin vaxtı bitdi.')
  })

  it('goes back to wherever the session was started — the group board as much as the console', async () => {
    impersonating({ ...SUPPORT, readOnly: true }, '/hq')
    respond(400, { error: 'TenantUnresolved' })

    await apiRequest('/api/reports/today')

    expect(navigations).toEqual(['/hq'])
    expect(endedSessionNotice()).toBe('Baxış sessiyasının vaxtı bitdi.')
  })

  it('says it once', async () => {
    impersonating()
    respond(400, { error: 'TenantUnresolved' })
    await apiRequest('/api/reports/today')

    forgetEndedSessionNotice()

    expect(endedSessionNotice()).toBeNull()
  })

  it('ends the session once, however many requests the dead token had in flight', async () => {
    // The board asks for several things at once, and every one of them comes back refused.
    impersonating()
    respond(400, { error: 'TenantUnresolved' })

    await Promise.all([apiRequest('/a'), apiRequest('/b'), apiRequest('/c')])

    expect(navigations).toHaveLength(1)
    expect(localStorage.getItem('attendanceqr.jwt')).toBe('operator-token')
  })

  it("never touches an employee's or an admin's own session", async () => {
    // Their tokens do not expire, a TenantUnresolved on one means something else, and signing people
    // out is the one thing this product does not do.
    localStorage.setItem('attendanceqr.jwt', 'own-token')
    respond(400, { error: 'TenantUnresolved' })

    await apiRequest('/api/reports/today')

    expect(signedOut).toBe(0)
    expect(navigations).toEqual([])
    expect(localStorage.getItem('attendanceqr.jwt')).toBe('own-token')
    expect(endedSessionNotice()).toBeNull()
  })

  it('leaves a support session alone on any other refusal', async () => {
    impersonating()
    respond(400, { error: 'ValidationFailed' })

    await apiRequest('/api/admin/employees', { method: 'POST', body: {} })

    expect(navigations).toEqual([])
    expect(localStorage.getItem('attendanceqr.jwt')).toBe('impersonation-token')
  })

  it('does not end the session for a call made as another saved profile', async () => {
    impersonating()
    respond(400, { error: 'TenantUnresolved' })

    await apiRequest('/api/attendance/scan', { method: 'POST', body: {}, token: 'someone-else' })

    expect(navigations).toEqual([])
    expect(localStorage.getItem('attendanceqr.jwt')).toBe('impersonation-token')
  })
})
