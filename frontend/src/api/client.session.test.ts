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

function impersonating(info: object = SUPPORT) {
  localStorage.setItem('attendanceqr.jwt', 'impersonation-token')
  localStorage.setItem('attendanceqr.jwt.super', 'operator-token')
  localStorage.setItem('attendanceqr.impersonation', JSON.stringify(info))
}

describe('a support session the server no longer accepts', () => {
  let bounced = 0

  beforeEach(() => {
    vi.stubGlobal('localStorage', memoryStorage())
    vi.stubGlobal('sessionStorage', memoryStorage())
    bounced = 0
    setUnauthorizedHandler(() => { bounced++ })
  })
  afterEach(() => vi.unstubAllGlobals())

  it('ends the session and tells the login screen why', async () => {
    // 03.10.2026: the board said «Məlumat yüklənmədi» under a support banner for nearly two hours
    // after the session had run out, because the refusal was a 400 and only a 401 ended a session.
    impersonating()
    respond(400, { error: 'TenantUnresolved' })

    await apiRequest('/api/reports/today')

    expect(bounced).toBe(1)
    expect(localStorage.getItem('attendanceqr.jwt')).toBeNull()
    // The operator's own stashed token goes too, exactly as a 401 takes it — see clearToken.
    expect(localStorage.getItem('attendanceqr.jwt.super')).toBeNull()
    expect(localStorage.getItem('attendanceqr.impersonation')).toBeNull()
    expect(endedSessionNotice()).toBe('Dəstək rejiminin vaxtı bitdi. Təhlükəsizlik üçün yenidən daxil olun.')
  })

  it('calls a view session a view session', async () => {
    impersonating({ ...SUPPORT, readOnly: true })
    respond(400, { error: 'TenantUnresolved' })

    await apiRequest('/api/reports/today')

    expect(endedSessionNotice()).toBe('Baxış sessiyasının vaxtı bitdi. Yenidən daxil olun.')
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

    expect(bounced).toBe(1)
  })

  it("never ends an employee's or an admin's own session this way", async () => {
    // Their tokens do not expire, a TenantUnresolved on one means something else, and signing people
    // out is the one thing this product does not do.
    localStorage.setItem('attendanceqr.jwt', 'own-token')
    respond(400, { error: 'TenantUnresolved' })

    await apiRequest('/api/reports/today')

    expect(bounced).toBe(0)
    expect(localStorage.getItem('attendanceqr.jwt')).toBe('own-token')
    expect(endedSessionNotice()).toBeNull()
  })

  it('leaves a support session alone on any other refusal', async () => {
    impersonating()
    respond(400, { error: 'ValidationFailed' })

    await apiRequest('/api/admin/employees', { method: 'POST', body: {} })

    expect(bounced).toBe(0)
    expect(localStorage.getItem('attendanceqr.jwt')).toBe('impersonation-token')
  })

  it('does not end the session for a call made as another saved profile', async () => {
    impersonating()
    respond(400, { error: 'TenantUnresolved' })

    await apiRequest('/api/attendance/scan', { method: 'POST', body: {}, token: 'someone-else' })

    expect(bounced).toBe(0)
  })
})
