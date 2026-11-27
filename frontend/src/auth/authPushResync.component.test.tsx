// @vitest-environment jsdom
// Signing in (or receiving a new token after a PIN change) re-binds the device's push subscription in the
// background — the session never waits for it, and a resync that never settles changes nothing for the person.
import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, render } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'

const seen = vi.hoisted(() => ({ tokens: [] as (string | null)[] }))

vi.mock('../lib/push', () => ({
  // Never settles: whatever the push service does, the sign-in must not be waiting on it.
  resyncPushForToken: vi.fn((t: string | null) => {
    seen.tokens.push(t)
    return new Promise(() => {})
  }),
}))
vi.mock('../api/attendance', () => ({ getMyProfile: vi.fn(async () => ({ status: 403, data: null })) }))

import { AuthProvider, useAuth } from './AuthContext'

function jwt(claims: Record<string, string>): string {
  const b64 = (o: object) => btoa(JSON.stringify(o)).replace(/=+$/, '').replace(/\+/g, '-').replace(/\//g, '_')
  return `${b64({ alg: 'HS256' })}.${b64(claims)}.sig`
}

let auth: ReturnType<typeof useAuth> | null = null
function Probe() {
  auth = useAuth()
  return <span>{auth.isAuthenticated ? 'in' : 'out'}</span>
}

afterEach(() => {
  cleanup()
  localStorage.clear()
  seen.tokens = []
  auth = null
})

describe('AuthProvider and the push binding', () => {
  it('asks for a resync with each new token and is signed in without waiting for it', () => {
    const first = jwt({ sub: 'emp-1', tv: '1', role: 'Employee' })
    localStorage.setItem('attendanceqr.jwt', first)
    const view = render(
      <MemoryRouter>
        <AuthProvider>
          <Probe />
        </AuthProvider>
      </MemoryRouter>,
    )
    expect(view.container.textContent).toBe('in')
    expect(seen.tokens).toEqual([first])

    // A PIN change hands back a token with the next TokenVersion — the binding follows it.
    const second = jwt({ sub: 'emp-1', tv: '2', role: 'Employee' })
    act(() => auth!.saveToken(second))
    expect(view.container.textContent).toBe('in')
    expect(seen.tokens).toEqual([first, second])
  })
})
