import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { changePassword } from './auth'
import { setUnauthorizedHandler } from './client'

/**
 * «PIN-i dəyiş» with a mistyped CURRENT PIN.
 *
 * The person is signed in and their session is fine — they only typed the old PIN wrong. The server
 * answers that with an error code, and the sheet has a sentence ready for it («Cari PIN yanlışdır»).
 * What must not happen is the session ending: every 401 signs the device out (AuthContext's handler),
 * so if a wrong current PIN comes back as a 401 the worker is thrown to the login screen — and on a
 * crew phone their saved profile is dropped too — for a typo.
 */

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

describe('a wrong current PIN on «PIN-i dəyiş»', () => {
  let signedOut = 0

  beforeEach(() => {
    vi.stubGlobal('localStorage', memoryStorage())
    vi.stubGlobal('sessionStorage', memoryStorage())
    localStorage.setItem('attendanceqr.jwt', 'a-perfectly-valid-session')
    signedOut = 0
    setUnauthorizedHandler(() => { signedOut++ })
  })
  afterEach(() => vi.unstubAllGlobals())

  it("says the PIN is wrong and keeps the person signed in", async () => {
    // What AuthController.ChangePassword answers a wrong current PIN with: 400, never 401.
    vi.stubGlobal('fetch', vi.fn(async () => ({
      status: 400,
      text: async () => JSON.stringify({ error: 'InvalidCurrentPassword', remaining: 7 }),
    })))

    const { data } = await changePassword('1111', '2580')

    expect(data && 'error' in data ? data.error : null).toBe('InvalidCurrentPassword')
    expect(signedOut).toBe(0)
    expect(localStorage.getItem('attendanceqr.jwt')).toBe('a-perfectly-valid-session')
  })
})
