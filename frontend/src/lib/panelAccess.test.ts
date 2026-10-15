import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import { describe, expect, it } from 'vitest'
import { canOpen, PANEL_PAGES, pagesAfterSwitch, wouldOpen, type PanelAccess } from './panelAccess'

/**
 * The menu and the router must agree about who may open what.
 *
 * They did not. The rules lived twice — a `sections` array building the sidebar and a scatter of
 * `<AdminOnly>` wrappers guarding the routes — with nothing connecting them. Seven screens were opened
 * server-side and un-gated in the menu while the routes stayed shut, so for two commits a manager saw
 * seven rows that bounced them back to the today board. Nobody reported it: a silent redirect is
 * indistinguishable from a mis-tap.
 *
 * The table is now the single source, and this reads the router's source to prove nothing has slipped
 * out from under it again. Reading a file in a unit test is unusual; drift between two files is
 * exactly the thing no unit test would otherwise see.
 */

const APP = readFileSync(join(__dirname, '..', 'App.tsx'), 'utf8')

/** Every `<Route path="…">` nested inside the /admin element tree. */
function panelRoutePaths(): string[] {
  const start = APP.indexOf('path="/admin"')
  expect(start).toBeGreaterThan(-1)
  // The panel tree ends where the catch-all route begins.
  const end = APP.indexOf('path="*"', start)
  const body = APP.slice(start, end > start ? end : undefined)

  const paths = [...body.matchAll(/<Route\s+path="([^"/][^"]*)"/g)].map((m) => m[1]!)
  // index routes and the /admin element itself carry no relative path
  return [...new Set(paths)].filter((p) => p !== '/admin')
}

describe('the access table covers the router', () => {
  it('finds the panel routes at all', () => {
    // Guards the guard: if App.tsx is restructured so the scrape returns nothing, every other
    // assertion below would pass vacuously and this file would go quiet exactly when it mattered.
    expect(panelRoutePaths().length).toBeGreaterThan(15)
  })

  it('has a row for every route', () => {
    const known = new Set(PANEL_PAGES.map((p) => p.path))
    const missing = panelRoutePaths().filter((p) => !known.has(p))

    // A route with no row is refused by canOpen — so this failing means a page nobody can open,
    // which is the safe direction, but still a page somebody added and nobody can reach.
    expect(missing).toEqual([])
  })

  it('has no row for a route that no longer exists', () => {
    const routes = new Set(panelRoutePaths())
    const orphans = PANEL_PAGES.map((p) => p.path).filter((p) => !routes.has(p))

    // The other direction, and the one that rots quietly: a row left behind after a page is deleted
    // keeps a menu entry alive pointing at nothing.
    expect(orphans).toEqual([])
  })
})

describe('who may open what', () => {
  it('lets an admin open everything in the table', () => {
    for (const page of PANEL_PAGES.filter((p) => p.roles.includes('Admin')))
      expect(canOpen(page.path, 'Admin')).toBe(true)
  })

  it('keeps the company-wide pages away from a manager', () => {
    // Not a matter of taste. None of these can be narrowed to one branch: a broadcast has no
    // per-branch audience, a job-title rename rewrites every employee holding it, a holiday moves the
    // payroll divisor for the whole company, and the subscription is one contract.
    for (const path of ['announcements', 'billing', 'positions', 'non-working-days', 'bulk-invite'])
      expect(canOpen(path, 'Manager')).toBe(false)
  })

  it('gives a manager the branch-shaped pages', () => {
    // Each of these is a view of the branches they run, narrowed server-side. The employee card is
    // the one the owner asked for: a manager could not open their own people's profile at all.
    for (const path of ['dashboard', 'today', 'employees/:id', 'open-records', 'payroll', 'locations'])
      expect(canOpen(path, 'Manager')).toBe(true)
  })

  it('refuses an unknown path and a missing role', () => {
    // Fail closed. A route added without a row is then a visible mistake rather than an open door —
    // the original failure ran the other way, and quietly.
    expect(canOpen('some-new-page', 'Admin')).toBe(false)
    expect(canOpen('today', null)).toBe(false)
    expect(canOpen('today', 'Employee')).toBe(false)
  })
})

// ── Phase 4: the permission column must not take anything away when it starts deciding ──────────────

/** The whole catalogue — AccessPermissions.cs. A company administrator holds every key, company-wide. */
const ALL_KEYS = [
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

/** Today's manager as the seeder writes them (AccessRoleTemplates.cs): «Filial meneceri (köhnə qayda)» over
 *  their branches, and «PIN köməkçisi» company-wide. Copied by hand — if the template changes, change it here. */
const LEGACY_MANAGER = [
  'dashboard.view', 'attendance.view', 'attendance.photos.view', 'attendance.export', 'attendance.correct',
  'attendance.fraud', 'employees.view', 'employees.contacts.view', 'employees.create', 'employees.update',
  'employees.deactivate', 'devices.view', 'devices.approve', 'devices.revoke', 'devices.share_grant',
  'field_checkin.grant', 'checkin_rules.override', 'field_visits.view', 'field_visits.manage', 'leaves.view',
  'leaves.manage', 'schedules.view', 'schedules.manage', 'schedules.assign', 'payroll.view', 'locations.view',
  'locations.update', 'locations.geofence_manage', 'locations.qr_print', 'notifications.staff_alerts', 'tasks.use',
]
const PIN_HELPER = ['employees.reset_pin', 'employees.change_phone']
const TIMEKEEPER = [
  'dashboard.view', 'attendance.view', 'attendance.export', 'employees.view', 'employees.contacts.view',
  'employees.create', 'employees.update', 'employees.reset_pin', 'devices.view', 'devices.approve', 'schedules.view',
  'schedules.manage', 'schedules.assign', 'leaves.view', 'leaves.manage', 'field_visits.view', 'notifications.staff_alerts',
]

const holding = (keys: string[]): PanelAccess => ({ panel: true, permissions: keys.map((key) => ({ key })) })

describe('the switch takes nothing away from today’s admins and managers', () => {
  // The owner's third check for Phase 4. When the permission column starts deciding (Phase 5), every page
  // an admin or a manager opens today must still open, and nothing must open that does not today.
  it('opens for an admin exactly what the role opens', () => {
    const differ = PANEL_PAGES.filter((p) => wouldOpen(p.path, holding(ALL_KEYS)) !== canOpen(p.path, 'Admin'))
    expect(differ.map((p) => p.path)).toEqual([])
  })

  it('opens for a manager exactly what the role opens', () => {
    const manager = holding([...LEGACY_MANAGER, ...PIN_HELPER])
    const differ = PANEL_PAGES.filter((p) => wouldOpen(p.path, manager) !== canOpen(p.path, 'Manager'))
    expect(differ.map((p) => p.path)).toEqual([])
  })

  it('gives a manager the one staff list the admin has', () => {
    expect(canOpen('employees', 'Manager')).toBe(true)
    expect(canOpen('my-employees', 'Manager')).toBe(true) // a redirect to it, for old bookmarks
  })
})

describe('somebody whose panel comes only from a new role', () => {
  it('opens their area, and nothing else until the switch', () => {
    const keeper = holding(TIMEKEEPER)
    expect(canOpen('my-area', 'Employee', keeper)).toBe(true)
    expect(canOpen('today', 'Employee', keeper)).toBe(false)
    expect(canOpen('employees', 'Employee', keeper)).toBe(false)
  })

  it('shows what will open after the switch — and never what their role does not carry', () => {
    const later = pagesAfterSwitch('Employee', holding(TIMEKEEPER))
    for (const path of ['today', 'employees', 'leaves', 'schedules', 'pin-resets', 'device-changes'])
      expect(later.has(path)).toBe(true)
    for (const path of ['payroll', 'billing', 'positions', 'access', 'tasks', 'locations'])
      expect(later.has(path)).toBe(false)
  })

  it('opens the access page now for whoever may see assignments — the one door opened early', () => {
    expect(canOpen('access', 'Employee', holding(['assignments.view']))).toBe(true)
    expect(canOpen('access', 'Manager', holding([...LEGACY_MANAGER, ...PIN_HELPER]))).toBe(false)
  })

  it('opens nothing for somebody the panel is not for', () => {
    expect(canOpen('my-area', 'Employee', { panel: false, permissions: [] })).toBe(false)
    expect(canOpen('my-area', 'Employee', null)).toBe(false)
  })
})

