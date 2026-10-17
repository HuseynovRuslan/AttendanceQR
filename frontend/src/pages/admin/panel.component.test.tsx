// @vitest-environment jsdom
import { act, cleanup, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { MyAccess } from '../../api/me'
import { AccessProvider } from '../../auth/AccessContext'
import { PanelPage } from '../../components/AdminRoute'
import { AdminLayout } from './AdminLayout'
import { MyAreaPage } from './MyAreaPage'

/**
 * The panel as each kind of person sees it (Phase 4) — rendered, not just computed: the real sidebar, the
 * real «Mənim sahəm», the real AccessProvider. Only the network is fake: GET /api/me/access answers with
 * the access below, exactly as the server builds it from the permission model.
 */

const state = vi.hoisted(() => ({
  role: 'Employee' as 'Admin' | 'Manager' | 'Employee',
  access: null as unknown,
  calls: 0,
}))

vi.mock('../../auth/AuthContext', () => ({
  useAuth: () => ({
    token: 't', isAuthenticated: true, role: state.role, employeeId: 'me', email: 'sinaq@test.local',
    mustChangePin: false, saveToken: () => {}, logout: () => {}, profiles: [], switchProfile: () => {},
  }),
}))
vi.mock('../../api/me', () => ({
  getMyAccess: vi.fn(async () => {
    state.calls++
    return { status: 200, data: state.access }
  }),
}))
vi.mock('../../branding/BrandingContext', () => ({
  FEATURE: { Payroll: 'payroll', Announcements: 'announcements' },
  useBranding: () => ({ displayName: 'Sınaq şirkəti' }),
  useFeatureEnabled: () => true,
}))
vi.mock('../../components/BrandLogo', () => ({ BrandLogo: () => null }))
vi.mock('../../components/NotificationBell', () => ({ NotificationBell: () => null }))
vi.mock('../../api/notifications', () => ({ getSidebarBadges: vi.fn(async () => ({ status: 200, data: null })) }))

afterEach(() => {
  cleanup()
  state.calls = 0
})

// The permission sets the seeder writes (AccessRoleTemplates.cs).
const ALL = [
  'dashboard.view', 'attendance.view', 'attendance.photos.view', 'attendance.export', 'attendance.correct',
  'attendance.fraud', 'attendance.maintenance', 'employees.view', 'employees.contacts.view', 'employees.create',
  'employees.import', 'employees.update', 'employees.deactivate', 'employees.delete', 'employees.reset_pin',
  'employees.change_phone', 'employees.reinvite', 'employees.salary', 'employees.reference_photo',
  'employees.reset_attendance', 'devices.view', 'devices.approve', 'devices.revoke', 'devices.share_grant',
  'field_checkin.grant', 'checkin_rules.override', 'field_visits.view', 'field_visits.manage', 'field_visits.discard',
  'leaves.view', 'leaves.manage', 'schedules.view', 'schedules.manage', 'schedules.assign', 'payroll.view',
  'payroll.export', 'locations.view', 'locations.create', 'locations.activate', 'locations.update',
  'locations.geofence_manage', 'locations.qr_print', 'locations.qr_invalidate', 'structures.view', 'structures.manage',
  'assignments.view', 'assignments.manage', 'assignments.cross_area', 'roles.manage', 'notifications.staff_alerts',
  'tasks.use', 'holidays.manage', 'positions.manage', 'announcements.manage', 'votes.manage', 'equipment.view',
  'equipment.manage', 'billing.view', 'diag.view',
]
const LEGACY_MANAGER = [
  'dashboard.view', 'attendance.view', 'attendance.photos.view', 'attendance.export', 'attendance.correct',
  'attendance.fraud', 'employees.view', 'employees.contacts.view', 'employees.create', 'employees.update',
  'employees.deactivate', 'devices.view', 'devices.approve', 'devices.revoke', 'devices.share_grant',
  'field_checkin.grant', 'checkin_rules.override', 'field_visits.view', 'field_visits.manage', 'leaves.view',
  'leaves.manage', 'schedules.view', 'schedules.manage', 'schedules.assign', 'payroll.view', 'locations.view',
  'locations.update', 'locations.geofence_manage', 'locations.qr_print', 'notifications.staff_alerts', 'tasks.use',
  'employees.reset_pin', 'employees.change_phone',
]
const OPERATOR = [
  'dashboard.view', 'attendance.view', 'attendance.photos.view', 'attendance.export', 'employees.view',
  'employees.contacts.view', 'employees.create', 'employees.update', 'employees.deactivate', 'employees.reset_pin',
  'employees.change_phone', 'devices.view', 'devices.approve', 'devices.revoke', 'schedules.view', 'schedules.manage',
  'schedules.assign', 'leaves.view', 'leaves.manage', 'locations.view', 'locations.qr_print', 'field_visits.view',
  'field_visits.manage', 'assignments.view', 'notifications.staff_alerts',
]
const TIMEKEEPER = [
  'dashboard.view', 'attendance.view', 'attendance.export', 'employees.view', 'employees.contacts.view',
  'employees.create', 'employees.update', 'employees.reset_pin', 'devices.view', 'devices.approve', 'schedules.view',
  'schedules.manage', 'schedules.assign', 'leaves.view', 'leaves.manage', 'field_visits.view', 'notifications.staff_alerts',
]
const OBSERVER = [
  'dashboard.view', 'attendance.view', 'attendance.export', 'employees.view', 'leaves.view', 'schedules.view',
  'locations.view', 'field_visits.view',
]

const B1 = { id: 'b1', name: 'Mərdəkan filialı', structureId: 's1' }
const B2 = { id: 'b2', name: 'Bilgəh filialı', structureId: 's1' }

function access(
  legacyRole: 'Admin' | 'Manager' | 'Employee',
  perms: string[],
  roleName: string,
  template: string,
  branches: { id: string; name: string; structureId: string | null }[],
  scope: 'Company' | 'Structure' | 'Branch' = 'Branch',
): MyAccess {
  return {
    mode: 'Shadow',
    legacyRole,
    panel: true,
    permissions: perms.map((key) => ({ key, label: key, company: scope === 'Company', locationIds: [] })),
    assignments: [{
      roleId: template, roleName, template, scope,
      structureId: scope === 'Structure' ? 's1' : null,
      structureName: scope === 'Structure' ? 'Aeroport yolu' : null,
      locationIds: scope === 'Branch' ? branches.map((b) => b.id) : [],
    }],
    branches,
  }
}

async function renderPanel(role: 'Admin' | 'Manager' | 'Employee', a: MyAccess, path = '/admin/my-area') {
  state.role = role
  state.access = a
  const view = render(
    <MemoryRouter initialEntries={[path]}>
      <AccessProvider>
        <Routes>
          <Route path="/admin" element={<AdminLayout />}>
            <Route path="my-area" element={<PanelPage path="my-area"><MyAreaPage /></PanelPage>} />
            <Route path="employees" element={<PanelPage path="employees"><div>İŞÇİ SİYAHISI AÇILDI</div></PanelPage>} />
            <Route path="today" element={<PanelPage path="today"><div>DAVAMİYYƏT AÇILDI</div></PanelPage>} />
          </Route>
        </Routes>
      </AccessProvider>
    </MemoryRouter>,
  )
  await waitFor(() => expect(state.calls).toBeGreaterThan(0))
  await screen.findByText(/sinaq@test\.local/)
  return view
}

/** The links the sidebar offers today, and the greyed «Keçiddən sonra açılacaq» items under them. */
function sidebar(container: HTMLElement) {
  const nav = container.querySelector('.sidebar-nav')!
  const links = [...nav.querySelectorAll('a.nav-item')].map((a) => a.textContent!.trim())
  const later = [...nav.querySelectorAll('.nav-later .nav-item')].map((d) => d.textContent!.trim())
  const hrefs = Object.fromEntries([...nav.querySelectorAll('a.nav-item')].map((a) => [a.textContent!.trim(), a.getAttribute('href')]))
  return { links, later, hrefs }
}

describe('the menu of somebody whose panel comes from a new role', () => {
  it('a structure operator: their area and the assignments page now, the daily pages after the switch', async () => {
    const { container } = await renderPanel('Employee', access('Employee', OPERATOR, 'Struktur operatoru', 'structure_operator', [B1, B2], 'Structure'))
    await waitFor(() => expect(sidebar(container).links).toContain('Səlahiyyətlər'))
    const { links, later } = sidebar(container)
    expect(links).toEqual(['Mənim sahəm', 'Səlahiyyətlər'])
    for (const page of ['Bugünkü davamiyyət', 'İşçilər', 'PIN sıfırlama', 'Cihazlar', 'Növbələr', 'Filiallar'])
      expect(later).toContain(page)
    // Never what their role does not carry.
    for (const page of ['Maaş', 'Abunəlik', 'Vəzifələr', 'Tapşırıqlar', 'Qeyri-iş günləri'])
      expect([...links, ...later]).not.toContain(page)
  })

  it('a timekeeper: their area only, no assignments page', async () => {
    const { container } = await renderPanel('Employee', access('Employee', TIMEKEEPER, 'Tabelçi', 'timekeeper', [B1]))
    await waitFor(() => expect(sidebar(container).later.length).toBeGreaterThan(0))
    const { links, later } = sidebar(container)
    expect(links).toEqual(['Mənim sahəm'])
    for (const page of ['Bugünkü davamiyyət', 'İşçilər', 'PIN sıfırlama', 'Növbələr'])
      expect(later).toContain(page)
    for (const page of ['Səlahiyyətlər', 'Filiallar', 'Maaş'])
      expect([...links, ...later]).not.toContain(page)
    expect(container.textContent).toContain('Tabelçi')
    expect(container.textContent).toContain('Mərdəkan filialı')
  })

  it('an observer: only screens that look — nothing that changes a person', async () => {
    const { container } = await renderPanel('Employee', access('Employee', OBSERVER, 'Müşahidəçi', 'observer', [B1, B2]))
    await waitFor(() => expect(sidebar(container).later.length).toBeGreaterThan(0))
    const { links, later } = sidebar(container)
    expect(links).toEqual(['Mənim sahəm'])
    for (const page of ['Bugünkü davamiyyət', 'İşçilər', 'Hesabat', 'Filiallar'])
      expect(later).toContain(page)
    for (const page of ['PIN sıfırlama', 'Cihazlar', 'Səlahiyyətlər', 'Maaş', 'Tapşırıqlar'])
      expect([...links, ...later]).not.toContain(page)
  })
})

describe('an observer cannot edit', () => {
  it('opening the staff list says it opens after the switch — the list itself is not rendered', async () => {
    await renderPanel('Employee', access('Employee', OBSERVER, 'Müşahidəçi', 'observer', [B1]), '/admin/employees')
    expect(await screen.findByText('Bu bölmə keçiddən sonra açılacaq')).toBeTruthy()
    expect(screen.queryByText('İŞÇİ SİYAHISI AÇILDI')).toBeNull()
  })

  it('a page the role does not carry at all is refused, not promised', async () => {
    // An observer's role has no attendance-correction screen; «today» it carries, so pick one it lacks.
    await renderPanel('Employee', { ...access('Employee', ['dashboard.view'], 'Baxış', 'observer', [B1]) }, '/admin/today')
    expect(await screen.findByText('Bu səhifəyə icazəniz yoxdur')).toBeTruthy()
    expect(screen.queryByText('DAVAMİYYƏT AÇILDI')).toBeNull()
  })
})

describe('today’s admins and managers keep their panel', () => {
  it('an admin: every company page, nothing «after the switch»', async () => {
    const { container } = await renderPanel('Admin', access('Admin', ALL, 'Şirkət administratoru', 'company_admin', [B1, B2], 'Company'))
    await waitFor(() => expect(sidebar(container).links).toContain('Abunəlik'))
    const { links, later } = sidebar(container)
    for (const page of ['İdarəetmə paneli', 'Tapşırıqlar', 'Elanlar', 'Bugünkü davamiyyət', 'İşçilər', 'Məzuniyyət / İcazə',
      'Vəzifələr', 'Kompüter inventarizasiyası', 'Maaş', 'Filiallar', 'Qeyri-iş günləri', 'Səlahiyyətlər', 'Abunəlik', 'Mənim sahəm'])
      expect(links).toContain(page)
    expect(later).toEqual([])
  })

  it('a branch manager: the one staff list, their pages, none of the company-wide ones', async () => {
    const { container } = await renderPanel('Manager', access('Manager', LEGACY_MANAGER, 'Filial meneceri (köhnə qayda)', 'legacy_manager', [B1]))
    await waitFor(() => expect(sidebar(container).links).toContain('İşçilər'))
    const { links, later, hrefs } = sidebar(container)
    for (const page of ['İdarəetmə paneli', 'Tapşırıqlar', 'Bugünkü davamiyyət', 'İşçilər', 'Məzuniyyət / İcazə', 'Maaş',
      'Filiallar', 'Növbələr', 'PIN sıfırlama', 'Cihazlar', 'Mənim sahəm'])
      expect(links).toContain(page)
    for (const page of ['Vəzifələr', 'Abunəlik', 'Qeyri-iş günləri', 'Səlahiyyətlər', 'Elanlar', 'Kompüter inventarizasiyası'])
      expect(links).not.toContain(page)
    expect(hrefs['İşçilər']).toBe('/admin/employees')
    expect(hrefs['Məzuniyyət / İcazə']).toBe('/admin/leaves')
    expect(later).toEqual([])
  })

  it('a manager opens the staff list itself, not a promise', async () => {
    await renderPanel('Manager', access('Manager', LEGACY_MANAGER, 'Filial meneceri (köhnə qayda)', 'legacy_manager', [B1]), '/admin/employees')
    expect(await screen.findByText('İŞÇİ SİYAHISI AÇILDI')).toBeTruthy()
  })
})

describe('a role or a branch given or taken shows without signing out', () => {
  it('the next return to the tab re-reads access: a new branch and the assignments page appear', async () => {
    const { container } = await renderPanel('Employee', access('Employee', TIMEKEEPER, 'Tabelçi', 'timekeeper', [B1]))
    await waitFor(() => expect(container.textContent).toContain('Mərdəkan filialı'))
    expect(sidebar(container).links).not.toContain('Səlahiyyətlər')
    expect(container.textContent).not.toContain('Bilgəh filialı')

    // An admin makes them an operator of the structure, both branches included.
    state.access = access('Employee', OPERATOR, 'Struktur operatoru', 'structure_operator', [B1, B2], 'Structure')
    Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => 'visible' })
    await act(async () => {
      document.dispatchEvent(new Event('visibilitychange'))
    })

    await waitFor(() => expect(sidebar(container).links).toContain('Səlahiyyətlər'))
    expect(container.textContent).toContain('Bilgəh filialı')
    expect(container.textContent).toContain('Struktur operatoru')
  })

  it('taken away, the panel narrows again on the next read', async () => {
    const { container } = await renderPanel('Employee', access('Employee', OPERATOR, 'Struktur operatoru', 'structure_operator', [B1, B2], 'Structure'))
    await waitFor(() => expect(sidebar(container).links).toContain('Səlahiyyətlər'))

    state.access = access('Employee', TIMEKEEPER, 'Tabelçi', 'timekeeper', [B1])
    Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => 'visible' })
    await act(async () => {
      document.dispatchEvent(new Event('visibilitychange'))
    })

    await waitFor(() => expect(sidebar(container).links).not.toContain('Səlahiyyətlər'))
    expect(container.textContent).not.toContain('Bilgəh filialı')
  })
})
