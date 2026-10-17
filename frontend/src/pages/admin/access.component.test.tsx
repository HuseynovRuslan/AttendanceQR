// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../auth/AccessContext'
import { AccessPage } from './AccessPage'

/**
 * «Səlahiyyətlər» for whoever the access API admits since Phase 4 — an admin, or an operator holding the
 * right to see assignments. Each sees the buttons their rights reach and no others; the server decides
 * every one of them again.
 */

const state = vi.hoisted(() => ({
  role: 'Employee' as 'Admin' | 'Manager' | 'Employee',
  perms: [] as string[],
  company: false,
  meCalls: 0,
}))

vi.mock('../../auth/AuthContext', () => ({
  useAuth: () => ({
    token: 't', isAuthenticated: true, role: state.role, employeeId: 'me', email: 'sinaq@test.local',
    mustChangePin: false, saveToken: () => {}, logout: () => {}, profiles: [], switchProfile: () => {},
  }),
}))
vi.mock('../../api/me', () => ({
  getMyAccess: vi.fn(async () => {
    state.meCalls++
    return {
      status: 200,
      data: {
        mode: 'Shadow', legacyRole: state.role, panel: true,
        permissions: state.perms.map((key) => ({ key, label: key, company: state.company, locationIds: [] })),
        assignments: [], branches: [],
      },
    }
  }),
}))
vi.mock('../../api/client', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../api/client')>()),
  apiRequest: vi.fn(async (path: string) => {
    const p = path.split('?')[0]
    if (p === '/api/admin/access/catalogue')
      return { status: 200, data: { permissions: [], templates: [] } }
    if (p === '/api/admin/access/assignments') return { status: 200, data: [] }
    if (p === '/api/admin/access/roles')
      return {
        status: 200,
        data: [{
          id: 'r1', name: 'Tabelçi', description: null, templateKey: 'timekeeper', isLocked: false, isLegacy: false,
          permissions: ['attendance.view'], activeAssignments: 2, archived: false,
        }],
      }
    if (p === '/api/admin/structures') return { status: 200, data: { structures: [], unassigned: [] } }
    return { status: 404, data: { error: 'NotMocked' } }
  }),
}))

afterEach(() => {
  cleanup()
  state.meCalls = 0
})

async function renderAccess(role: 'Admin' | 'Employee', perms: string[], company = false) {
  state.role = role
  state.perms = perms
  state.company = company
  render(
    <MemoryRouter>
      <AccessProvider>
        <AccessPage />
      </AccessProvider>
    </MemoryRouter>,
  )
  await waitFor(() => expect(state.meCalls).toBeGreaterThan(0))
  // The assignments list has answered once its empty message is on screen.
  await screen.findByText(/Hələ heç kimə yeni rol verilməyib|Heç nə tapılmadı/)
}

const tabs = () => screen.getAllByRole('tab').map((t) => t.textContent)

async function openRoles() {
  await act(async () => {
    fireEvent.click(screen.getByRole('tab', { name: 'Rollar' }))
  })
  await screen.findByText('Tabelçi')
}

describe('an operator who may only look at assignments', () => {
  it('sees the assignments, gives no role, edits no role, and has no structures screen', async () => {
    await renderAccess('Employee', ['assignments.view', 'employees.view'])
    expect(tabs()).toEqual(['Təyinatlar', 'Rollar'])
    expect(screen.queryByText('+ Rol ver')).toBeNull()
    await openRoles()
    expect(screen.queryByText('+ Yeni rol')).toBeNull()
    expect(screen.queryByText('Redaktə')).toBeNull()
    expect(screen.queryByText('Arxivlə')).toBeNull()
  })
})

describe('an operator the company let assign', () => {
  it('may give a role — and still edits no role, which is company-wide work', async () => {
    await renderAccess('Employee', ['assignments.view', 'assignments.manage'])
    expect(await screen.findByText('+ Rol ver')).toBeTruthy()
    expect(tabs()).toEqual(['Təyinatlar', 'Rollar'])
    await openRoles()
    expect(screen.queryByText('+ Yeni rol')).toBeNull()
    expect(screen.queryByText('Redaktə')).toBeNull()
  })
})

describe('the company administrator', () => {
  it('keeps everything the page had in Phase 3', async () => {
    await renderAccess('Admin', ['assignments.view', 'assignments.manage', 'roles.manage', 'structures.manage'], true)
    expect(tabs()).toEqual(['Təyinatlar', 'Rollar', 'Strukturlar'])
    expect(screen.getByText('+ Rol ver')).toBeTruthy()
    await openRoles()
    expect(screen.getByText('+ Yeni rol')).toBeTruthy()
    expect(screen.getByText('Redaktə')).toBeTruthy()
  })
})
