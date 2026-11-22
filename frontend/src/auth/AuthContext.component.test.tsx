// @vitest-environment jsdom
import { act, cleanup, render, screen } from '@testing-library/react'
import { MemoryRouter, useLocation } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { AuthProvider, useAuth } from './AuthContext'
import { apiRequest } from '../api/client'
import { changePassword } from '../api/auth'
import { listProfiles, saveProfile } from '../lib/profiles'

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
  const { pathname } = useLocation()
  return (
    <>
      <div data-testid="who">{auth.employeeId ?? '-'}</div>
      <div data-testid="at">{pathname}</div>
    </>
  )
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

describe('a token the server refuses', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('drops that account from the crew phone’s saved profiles — and only that one', async () => {
    // The handler read the active slot after it had been emptied, found nothing, and left the dead
    // account in the switcher: a row that looks like a way in, discovered at the poster.
    saveProfile({ employeeId: 'A', name: 'Aynur', token: WORKER_A, addedAtMs: 1 })
    saveProfile({ employeeId: 'B', name: 'Bəxtiyar', token: WORKER_B, addedAtMs: 2 })
    localStorage.setItem('attendanceqr.jwt', WORKER_A)
    vi.stubGlobal('fetch', vi.fn(async () => ({ status: 401, text: async () => '' })))
    renderAuth()
    await act(async () => {})

    await act(async () => {
      await apiRequest('/api/attendance/today')
    })

    expect(listProfiles().map((p) => p.employeeId)).toEqual(['B'])
    expect(screen.getByTestId('who').textContent).toBe('-')
    expect(screen.getByTestId('at').textContent).toBe('/login')
  })

  it('is not a wrong CURRENT PIN in «PIN-i dəyiş» — that session, and its saved row, stay', async () => {
    // change-password answers 401 InvalidCurrentPassword on a perfectly good token. Read as a dead
    // session it signed the worker out AND dropped their saved row, so somebody unsure of their PIN who
    // guessed wrong was left on /login with nothing to tap — needing the very PIN they could not
    // remember to record the day's check-in.
    saveProfile({ employeeId: 'A', name: 'Aynur', token: WORKER_A, addedAtMs: 1 })
    localStorage.setItem('attendanceqr.jwt', WORKER_A)
    vi.stubGlobal('fetch', vi.fn(async () => ({
      status: 401,
      text: async () => JSON.stringify({ error: 'InvalidCurrentPassword' }),
    })))
    renderAuth()
    await act(async () => {})

    let answer: Awaited<ReturnType<typeof changePassword>> | null = null
    await act(async () => {
      answer = await changePassword('1111', '4826')
    })

    // The sheet still gets the code it turns into «Cari PIN yanlışdır».
    expect(answer!.data).toEqual({ error: 'InvalidCurrentPassword' })
    expect(listProfiles().map((p) => p.employeeId)).toEqual(['A'])
    expect(localStorage.getItem('attendanceqr.jwt')).toBe(WORKER_A)
    expect(screen.getByTestId('who').textContent).toBe('A')
    expect(screen.getByTestId('at').textContent).toBe('/')
  })
})
