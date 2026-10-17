// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { EmployeesPage } from './EmployeesPage'
import { AdminRoute } from '../../components/AdminRoute'

/**
 * The one staff list (Phase 4), rendered in a browser-like DOM for an admin and for a branch manager. The
 * network is a fake answering by path — the shapes are the server's; what each role is sent is the
 * server's own rule (a manager's roster has no salary, no role, and a per-row «may I change this»).
 */

type Answer = { status: number; data: unknown }
const net = vi.hoisted(() => ({
  role: 'Admin' as 'Admin' | 'Manager' | 'Employee',
  routes: {} as Record<string, Answer>,
  calls: [] as { method: string; path: string; body: unknown }[],
}))

vi.mock('../../auth/AuthContext', () => ({
  useAuth: () => ({
    token: 't', isAuthenticated: true, role: net.role, employeeId: 'me', email: 'sinaq@test.local',
    mustChangePin: false, saveToken: () => {}, logout: () => {}, profiles: [], switchProfile: () => {},
  }),
}))
vi.mock('../../api/client', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../api/client')>()),
  apiRequest: vi.fn(async (path: string, opts?: { method?: string; body?: unknown }) => {
    const method = opts?.method ?? 'GET'
    net.calls.push({ method, path, body: opts?.body })
    const key = `${method} ${path.split('?')[0]}`
    return net.routes[key] ?? { status: 404, data: { error: 'NotMocked' } }
  }),
}))

afterEach(() => {
  cleanup()
  net.routes = {}
  net.calls = []
})

const OK = (data: unknown): Answer => ({ status: 200, data })

function adminRow(id: string, fullName: string) {
  return {
    id, fullName, fatherName: null, position: 'Bağban', birthYear: null, phoneNumber: '501112233', role: 'Employee',
    locationId: 'b1', locationName: 'Park', managedLocationIds: [], managedLocationNames: [], isActive: true,
    activated: true, mustChangePin: false, lastActiveAtUtc: null, hasDevice: false, deviceLabel: null, boundAtUtc: null,
  }
}

function managerRow(id: string, fullName: string, over: Record<string, unknown> = {}) {
  return {
    id, fullName, firstName: null, lastName: null, fatherName: null, position: 'Bağban', phoneNumber: '501112233',
    email: null, locationId: 'b1', locationName: 'Park', birthDate: null, birthYear: null, workStart: null, workEnd: null,
    scheduleId: null, workCycleDays: null, workCycleOnDays: null, workCycleAnchor: null, photoExempt: false,
    canFieldCheckIn: false, canShareDevice: false, isActive: true, activated: true, manageable: true, isColleague: false,
    qrlessCheckInOverride: null, requireGeofenceOverride: null,
    ...over,
  }
}

function adminRoutes(list: Answer) {
  net.role = 'Admin'
  net.routes = {
    'GET /api/admin/employees': list,
    'GET /api/admin/employees/stats': OK({ total: 1, activated: 1, notStarted: 0, noPush: 0, noDevice: 0, leftCount: 0 }),
    'GET /api/admin/locations': OK([{ id: 'b1', name: 'Park' }]),
    'GET /api/admin/schedules': OK([]),
    'GET /api/tenant/group-companies': OK([]),
  }
}

function managerRoutes(roster: Answer) {
  net.role = 'Manager'
  net.routes = {
    'GET /api/manager/employees': roster,
    'GET /api/manager/locations': OK([{ id: 'b1', name: 'Park' }]),
    'GET /api/manager/schedules': OK([]),
    'GET /api/manager/positions': OK([{ name: 'Bağban' }]),
    'GET /api/tenant/group-companies': OK([]),
  }
}

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/admin/employees']}>
      <EmployeesPage />
    </MemoryRouter>,
  )
}

/** The text of the list's own empty/failed row, once the first answer is in. */
async function emptyRowText() {
  await waitFor(() => expect(screen.queryByText('Yüklənir…')).toBeNull())
  const cell = document.querySelector('tbody td[colspan]')
  return cell?.textContent?.trim() ?? ''
}

describe('a refused list is not an empty list', () => {
  it('admin: 403 reads «icazəniz yoxdur», never «Hələ işçi yoxdur»', async () => {
    adminRoutes({ status: 403, data: { error: 'Forbidden' } })
    renderPage()
    const text = await emptyRowText()
    expect(text).toContain('icazəniz yoxdur')
    expect(text).not.toContain('Hələ işçi yoxdur')
  })

  it('admin: a server failure reads «yüklənmədi»', async () => {
    adminRoutes({ status: 500, data: null })
    renderPage()
    expect(await emptyRowText()).toContain('yüklənmədi')
  })

  it('admin: a real empty company still says so', async () => {
    adminRoutes(OK({ items: [], total: 0, page: 1, pageSize: 25 }))
    renderPage()
    expect(await emptyRowText()).toContain('Hələ işçi yoxdur')
  })

  it('manager: their own roster refused reads «icazəniz yoxdur»', async () => {
    managerRoutes({ status: 403, data: { error: 'Forbidden' } })
    renderPage()
    const text = await emptyRowText()
    expect(text).toContain('icazəniz yoxdur')
    expect(text).not.toContain('Hələ işçi yoxdur')
  })
})

describe('the admin screen is the admin screen it was', () => {
  it('reads the admin roster and keeps the company tools', async () => {
    adminRoutes(OK({ items: [adminRow('e1', 'Zaur Əliyev')], total: 1, page: 1, pageSize: 25 }))
    renderPage()
    expect(await screen.findByText('Zaur Əliyev')).toBeTruthy()
    expect(screen.getByText('Referansları sıfırla')).toBeTruthy()
    expect(screen.getByLabelText('Status')).toBeTruthy()
    expect(screen.getByLabelText('Rol')).toBeTruthy()
    expect(net.calls.some((c) => c.path.startsWith('/api/manager/'))).toBe(false)
  })
})

describe('a branch manager on the same screen', () => {
  const ROSTER = [
    managerRow('a', 'Aynur Həsənova', { qrlessCheckInOverride: null }),
    managerRow('m', 'Murad Menecer', { isColleague: true, manageable: true }),
    managerRow('x', 'Xanım Admin', { isColleague: true, manageable: false, phoneNumber: null }),
  ]

  it('reads only their own endpoints and shows no company tool', async () => {
    managerRoutes(OK(ROSTER))
    renderPage()
    expect(await screen.findByText('Aynur Həsənova')).toBeTruthy()
    expect(screen.queryByText('Referansları sıfırla')).toBeNull()
    expect(screen.queryByLabelText('Status')).toBeNull()
    expect(screen.queryByLabelText('Rol')).toBeNull()
    expect(net.calls.filter((c) => c.path.startsWith('/api/admin/'))).toEqual([])
  })

  it('hides the buttons the server would refuse: a row they may not change is view-only', async () => {
    managerRoutes(OK(ROSTER))
    renderPage()
    const adminCell = (await screen.findByText('Xanım Admin')).closest('tr')!
    expect(within(adminCell).getByText('yalnız baxış')).toBeTruthy()
    expect(within(adminCell).queryByText('Redaktə')).toBeNull()
    expect((within(adminCell).getByRole('checkbox') as HTMLInputElement).disabled).toBe(true)

    const own = screen.getByText('Aynur Həsənova').closest('tr')!
    expect(within(own).getByText('Redaktə')).toBeTruthy()
    // A colleague is editable one at a time, but never ticked for a bulk action.
    const colleague = screen.getByText('Murad Menecer').closest('tr')!
    expect(within(colleague).getByText('Redaktə')).toBeTruthy()
    expect((within(colleague).getByRole('checkbox') as HTMLInputElement).disabled).toBe(true)
  })

  it('a save sends the person’s QR-less and GPS overrides back from their own card — and no salary or role', async () => {
    managerRoutes(OK(ROSTER))
    net.routes['GET /api/manager/employees/a'] = OK({
      ...managerRow('a', 'Aynur Həsənova'),
      firstName: 'Aynur', lastName: 'Həsənova', email: 'aynur@test.local',
      qrlessCheckInOverride: true, requireGeofenceOverride: false, canShareDevice: true, photoExempt: true,
    })
    net.routes['PUT /api/manager/employees/a'] = OK({ id: 'a' })
    renderPage()

    const own = (await screen.findByText('Aynur Həsənova')).closest('tr')!
    await act(async () => {
      fireEvent.click(within(own).getByText('Redaktə'))
    })
    const save = await screen.findByRole('button', { name: /Yadda saxla/ })
    // The form says what is stored: QR-less on, and the GPS wall NOT required (false) — «yalnız ölç».
    const shown = [...document.querySelectorAll('form select')].map((el) => {
      const sel = el as HTMLSelectElement
      return sel.options[sel.selectedIndex]?.textContent ?? ''
    })
    expect(shown).toContain('QR-suz — üz və GPS ilə giriş etsin')
    expect(shown.some((t) => t.startsWith('Rədd etmə — yalnız ölç'))).toBe(true)
    await act(async () => {
      fireEvent.click(save)
    })

    await waitFor(() => expect(net.calls.some((c) => c.method === 'PUT')).toBe(true))
    const put = net.calls.find((c) => c.method === 'PUT')!
    expect(put.path).toBe('/api/manager/employees/a')
    const body = put.body as Record<string, unknown>
    expect(body.qrlessCheckInOverride).toBe(true)
    expect(body.requireGeofenceOverride).toBe(false)
    expect(body.photoExempt).toBe(true)
    expect(body.email).toBe('aynur@test.local')
    for (const never of ['monthlySalary', 'role', 'managedLocationIds', 'canShareDevice'])
      expect(never in body).toBe(false)
  })
})

describe('a company switched to Enforced: the list follows the role in the model', () => {
  const TIMEKEEPER = ['dashboard.view', 'attendance.view', 'employees.view', 'employees.contacts.view', 'employees.create',
    'employees.update', 'employees.reset_pin', 'devices.view', 'devices.approve', 'schedules.view', 'schedules.manage',
    'schedules.assign', 'leaves.view', 'leaves.manage', 'field_visits.view', 'notifications.staff_alerts']
  const OBSERVER = ['dashboard.view', 'attendance.view', 'employees.view', 'leaves.view', 'schedules.view',
    'locations.view', 'field_visits.view']

  /** Behind the real door: plain staff by token waits for /api/me/access, as in the app. */
  function renderEnforced(tokenRole: 'Admin' | 'Manager' | 'Employee', perms: string[], companyAdmin = false) {
    net.role = tokenRole
    net.routes['GET /api/me/access'] = OK({
      mode: 'Enforced', legacyRole: tokenRole, panel: true, companyAdmin,
      permissions: perms.map((key) => ({ key, label: key, company: companyAdmin, locationIds: companyAdmin ? [] : ['b1'] })),
      assignments: [], branches: [{ id: 'b1', name: 'Park', structureId: null }],
    })
    return render(
      <MemoryRouter initialEntries={['/admin/employees']}>
        <AdminRoute><EmployeesPage /></AdminRoute>
      </MemoryRouter>,
    )
  }

  it('an observer reads their branch through the narrowed endpoints and gets nothing to press', async () => {
    managerRoutes(OK([managerRow('a', 'Aynur Həsənova', { manageable: false, phoneNumber: null })]))
    renderEnforced('Employee', OBSERVER)
    const row = (await screen.findByText('Aynur Həsənova')).closest('tr')!
    expect(within(row).getByText('yalnız baxış')).toBeTruthy()
    expect(screen.queryByText('İşçi əlavə et')).toBeNull()
    expect(screen.queryByText('PIN sıfırla · nömrəni dəyiş')).toBeNull()
    expect(net.calls.filter((c) => c.path.startsWith('/api/admin/'))).toEqual([])
  })

  it('a timekeeper adds and edits their own people — the manager’s desk, not the admin’s', async () => {
    managerRoutes(OK([managerRow('a', 'Aynur Həsənova')]))
    net.routes['GET /api/manager/credential-targets'] = OK([])
    renderEnforced('Employee', TIMEKEEPER)
    const row = (await screen.findByText('Aynur Həsənova')).closest('tr')!
    expect(within(row).getByText('Redaktə')).toBeTruthy()
    expect(screen.getByText('İşçi əlavə et')).toBeTruthy()
    expect(screen.getByText('PIN sıfırla · nömrəni dəyiş')).toBeTruthy()
    expect(net.calls.filter((c) => c.path.startsWith('/api/admin/'))).toEqual([])
  })

  it('the Company administrator keeps the admin’s list and its company tools', async () => {
    adminRoutes(OK({ items: [adminRow('a', 'Aynur Həsənova')], total: 1, page: 1, pageSize: 50 }))
    renderEnforced('Admin', ['employees.view', 'employees.create', 'employees.reset_pin', 'employees.reference_photo'], true)
    expect(await screen.findByText('Aynur Həsənova')).toBeTruthy()
    expect(screen.getByText('Referansları sıfırla')).toBeTruthy()
    expect(net.calls.some((c) => c.path.startsWith('/api/admin/employees'))).toBe(true)
    expect(net.calls.filter((c) => c.path.startsWith('/api/manager/'))).toEqual([])
  })
})
