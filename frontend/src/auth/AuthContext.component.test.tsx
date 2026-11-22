// @vitest-environment jsdom
import { act, cleanup, render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { AuthProvider, useAuth } from './AuthContext'

/**
 * The session holder, in a browser-like DOM. What is under test is what it does AROUND the token: on a
 * crew phone the device's notifications must follow whoever is signed in.
 */

const push = vi.hoisted(() => ({ syncs: 0 }))
vi.mock('../lib/push', () => ({
  syncPushOwner: () => {
    push.syncs++
    return Promise.resolve()
  },
}))
vi.mock('../api/attendance', () => ({
  getMyProfile: async () => ({ status: 200, data: { fullName: 'Zaur Əliyev' } }),
}))

const jwt = (claims: object) => `h.${btoa(JSON.stringify(claims))}.s`
const WORKER_A = jwt({ sub: 'A', role: 'Employee' })
const WORKER_B = jwt({ sub: 'B', role: 'Employee' })

let auth: ReturnType<typeof useAuth> | null = null
function Probe() {
  auth = useAuth()
  return <div data-testid="who">{auth.employeeId ?? '-'}</div>
}

function renderAuth() {
  return render(
    <MemoryRouter>
      <AuthProvider>
        <Probe />
      </AuthProvider>
    </MemoryRouter>,
  )
}

beforeEach(() => {
  localStorage.clear()
  push.syncs = 0
  auth = null
})
afterEach(() => cleanup())

describe('notifications follow whoever is signed in', () => {
  it('on app start with somebody signed in — which is also where a profile switch lands', async () => {
    localStorage.setItem('attendanceqr.jwt', WORKER_A)
    renderAuth()
    await act(async () => {})
    expect(push.syncs).toBe(1)
  })

  it('not while nobody is signed in, and at once after a login', async () => {
    renderAuth()
    await act(async () => {})
    expect(push.syncs).toBe(0)

    await act(async () => {
      auth!.saveToken(WORKER_B)
    })

    expect(screen.getByTestId('who').textContent).toBe('B')
    expect(push.syncs).toBe(1)
  })
})
