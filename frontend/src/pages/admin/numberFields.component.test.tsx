// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { EmployeesPage } from './EmployeesPage'
import { LocationsPage } from './LocationsPage'

/**
 * The two admin number fields whose wrong value is SILENT: the monthly salary (empty = «not in payroll»)
 * and a branch's «Ştat» (empty = «clear it»). Both were `type="number"`, which reports "" for «850,5» or
 * «1 200» in Safari, Firefox and an English Chrome — so a mistyped figure was saved as «nothing» under a
 * success message. They are text now, read on save, and refused out loud when they cannot be read.
 */

type Answer = { status: number; data: unknown }
const net = vi.hoisted(() => ({
  routes: {} as Record<string, Answer>,
  calls: [] as { method: string; path: string; body: unknown }[],
}))

vi.mock('../../auth/AuthContext', () => ({
  useAuth: () => ({
    token: 't', isAuthenticated: true, role: 'Admin', employeeId: 'me', email: 'sinaq@test.local',
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
// Leaflet has no business in a DOM without layout; the map is not what is under test.
vi.mock('../../components/LocationMapPicker', () => ({ LocationMapPicker: () => null }))

afterEach(() => {
  cleanup()
  net.routes = {}
  net.calls = []
})

const OK = (data: unknown): Answer => ({ status: 200, data })
const puts = () => net.calls.filter((c) => c.method === 'PUT')

// --- the salary --------------------------------------------------------------------------------------

function employee(monthlySalary: number | null) {
  return {
    id: 'e1', fullName: 'Zaur Əliyev', firstName: 'Zaur', lastName: 'Əliyev', fatherName: null, position: 'Bağban',
    birthYear: null, birthDate: null, email: null, phoneNumber: '501112233', role: 'Employee', locationId: 'b1',
    locationName: 'Park', managedLocationIds: [], managedLocationNames: [], isActive: true, activated: true,
    mustChangePin: false, lastActiveAtUtc: null, hasDevice: false, deviceLabel: null, boundAtUtc: null,
    workStart: null, workEnd: null, scheduleId: null, workCycleDays: null, workCycleOnDays: null,
    monthlySalary, createdAtUtc: '2026-01-01T00:00:00Z',
  }
}

async function openEmployee(monthlySalary: number | null = 800) {
  net.routes = {
    'GET /api/admin/employees': OK({ items: [employee(monthlySalary)], total: 1, page: 1, pageSize: 25 }),
    'GET /api/admin/employees/stats': OK({ total: 1, activated: 1, notStarted: 0, noPush: 0, noDevice: 0, leftCount: 0 }),
    'GET /api/admin/locations': OK([{ id: 'b1', name: 'Park' }]),
    'GET /api/admin/schedules': OK([]),
    'GET /api/tenant/group-companies': OK([]),
    'GET /api/admin/employees/e1': OK(employee(monthlySalary)),
    'PUT /api/admin/employees/e1': OK({ id: 'e1' }),
  }
  render(<MemoryRouter initialEntries={['/admin/employees']}><EmployeesPage /></MemoryRouter>)
  const row = (await screen.findByText('Zaur Əliyev')).closest('tr')!
  await act(async () => {
    fireEvent.click(within(row).getByText('Redaktə'))
  })
  const save = await screen.findByRole('button', { name: /Yadda saxla/ })
  return { save, salary: screen.getByLabelText('Aylıq maaş (AZN)') as HTMLInputElement }
}

async function typeAndSave(input: HTMLInputElement, save: HTMLElement, text: string) {
  await act(async () => {
    fireEvent.change(input, { target: { value: text } })
  })
  await act(async () => {
    fireEvent.click(save)
  })
}

describe('the monthly salary', () => {
  it('is a text field, so the browser can never swap what was typed for ""', async () => {
    const { salary } = await openEmployee()
    expect(salary.type).toBe('text')
    expect(salary.inputMode).toBe('decimal')
    expect(salary.value).toBe('800')
  })

  it.each([
    ['850,5', 850.5],
    ['1 200', 1200],
    ['1 250,50', 1250.5],
    ['900.75', 900.75],
  ])('reads «%s» as %d', async (typed, expected) => {
    const { salary, save } = await openEmployee()
    await typeAndSave(salary, save, typed)
    await waitFor(() => expect(puts()).toHaveLength(1))
    expect((puts()[0].body as Record<string, unknown>).monthlySalary).toBe(expected)
  })

  it('refuses what it cannot read — it is not sent as «no salary»', async () => {
    const { salary, save } = await openEmployee()
    await typeAndSave(salary, save, '8OO')
    expect(await screen.findByText(/Maaş yanlış yazılıb/)).toBeTruthy()
    expect(salary.getAttribute('aria-invalid')).toBe('true')
    expect(puts()).toHaveLength(0)
    expect(screen.queryByText('İşçi yeniləndi')).toBeNull()
  })

  it('refuses an ambiguous «1.250» rather than guessing a thousandfold', async () => {
    const { salary, save } = await openEmployee()
    await typeAndSave(salary, save, '1.250')
    expect(await screen.findByText(/Maaş yanlış yazılıb/)).toBeTruthy()
    expect(puts()).toHaveLength(0)
  })

  it('lets the save through once the figure is corrected', async () => {
    const { salary, save } = await openEmployee()
    await typeAndSave(salary, save, '850 5')
    expect(await screen.findByText(/Maaş yanlış yazılıb/)).toBeTruthy()
    await typeAndSave(salary, save, '850,5')
    await waitFor(() => expect(puts()).toHaveLength(1))
    expect(screen.queryByText(/Maaş yanlış yazılıb/)).toBeNull()
    expect((puts()[0].body as Record<string, unknown>).monthlySalary).toBe(850.5)
  })

  it('still clears on purpose: an emptied field is «not in payroll»', async () => {
    const { salary, save } = await openEmployee()
    await typeAndSave(salary, save, '  ')
    await waitFor(() => expect(puts()).toHaveLength(1))
    expect((puts()[0].body as Record<string, unknown>).monthlySalary).toBeNull()
  })
})

// --- the headcount -----------------------------------------------------------------------------------

function branch(headcount: number | null) {
  return {
    id: 'b1', name: 'Park', geofenceMovedAtUtc: null, geofenceMovedMeters: null, latitude: 40.4, longitude: 49.8,
    radiusMeters: 150, shiftStart: '09:00', shiftEnd: '18:00', lateThresholdMinutes: 15, isActive: true,
    workDaysMask: 126, qrlessCheckIn: false, requireGeofence: true, headcount,
  }
}

async function openBranch(headcount: number | null = 12) {
  net.routes = {
    'GET /api/admin/locations': OK([branch(headcount)]),
    'GET /api/admin/schedules': OK([]),
    'PUT /api/admin/locations/b1': OK(branch(headcount)),
  }
  render(<MemoryRouter initialEntries={['/admin/locations']}><LocationsPage /></MemoryRouter>)
  const dots = await screen.findByRole('button', { name: 'Digər əməliyyatlar' })
  await act(async () => {
    fireEvent.click(dots)
  })
  await act(async () => {
    fireEvent.click(screen.getByRole('menuitem', { name: 'Redaktə' }))
  })
  const save = await screen.findByRole('button', { name: 'Yadda saxla' })
  return { save, headcount: screen.getByLabelText('Ştat (işçi yeri)') as HTMLInputElement }
}

describe('a branch’s «Ştat»', () => {
  it('is a text field', async () => {
    const { headcount } = await openBranch()
    expect(headcount.type).toBe('text')
    expect(headcount.value).toBe('12')
  })

  it('reads «1 200» as 1200', async () => {
    const { headcount, save } = await openBranch()
    await typeAndSave(headcount, save, '1 200')
    await waitFor(() => expect(puts()).toHaveLength(1))
    expect((puts()[0].body as Record<string, unknown>).headcount).toBe(1200)
  })

  it.each(['12,5', 'on iki', '-3'])('refuses «%s» instead of wiping the headcount with 0', async (typed) => {
    const { headcount, save } = await openBranch()
    await typeAndSave(headcount, save, typed)
    expect(await screen.findByText('Ştatı tam ədədlə yazın, məsələn: 12')).toBeTruthy()
    expect(puts()).toHaveLength(0)
  })

  it('still clears on purpose: an emptied field is sent as 0', async () => {
    const { headcount, save } = await openBranch()
    await typeAndSave(headcount, save, '')
    await waitFor(() => expect(puts()).toHaveLength(1))
    expect((puts()[0].body as Record<string, unknown>).headcount).toBe(0)
  })

  it('says the server’s own limit before asking it', async () => {
    const { headcount, save } = await openBranch()
    await typeAndSave(headcount, save, '200 000')
    expect(await screen.findByText('Ştat 0 ilə 100 000 arasında olmalıdır')).toBeTruthy()
    expect(puts()).toHaveLength(0)
  })
})
