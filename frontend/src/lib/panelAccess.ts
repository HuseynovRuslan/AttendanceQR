import type { Role } from './jwt'

/**
 * Who may open which page of the panel — one table, read by both the router and the sidebar.
 *
 * There has only ever been one panel: /admin admits Admin and Manager alike and always has. What
 * forked was the rules, written twice — a `sections` array in AdminLayout deciding the menu, and a
 * scatter of <AdminOnly> wrappers deciding the routes — with nothing tying them together.
 *
 * They drifted, and the drift was invisible. Seven screens were opened server-side and un-gated in
 * the menu without the routes being touched, so a manager saw seven rows that bounced them silently
 * back to the today board. Two commits, nobody noticed, because a silent redirect looks like a
 * mis-tap.
 *
 * So: one row per page, naming its roles. A page opens or closes in one place, and a route with no
 * row is a build-time hole rather than a live one.
 *
 * Since Phase 4 each row also names the permissions that open it in the new model (any one is enough).
 * Until the switch (Phase 5) the ROLES still decide — the owner's rule: no real decision changes while a
 * company is in Legacy or Shadow mode — and the permissions only say what will open after it, which the
 * menu shows as such. A test holds the two answers equal for today's admins and managers, so the switch
 * cannot take a page away from either of them.
 */

export type PanelSection = 'daily' | 'people' | 'reports' | 'setup' | 'company'

export interface PanelPage {
  /** Path relative to /admin — matches the <Route path="…"> exactly. */
  path: string
  /** The page's name in the menu and on «Mənim sahəm». */
  label: string
  /** False for what is reached through another page (a profile, a print view) or only redirects. */
  menu?: boolean
  roles: Role[]
  /** The permission model's answer: any one of these opens the page. Empty — every panel user. */
  perms: string[]
}

/** What the panel needs from GET /api/me/access to decide a page. */
export interface PanelAccess {
  panel: boolean
  permissions: { key: string }[]
  /** The company's permission mode. In 'Enforced' the permissions open the pages — the roles no longer do. */
  mode?: string
  /** Holds the Company administrator role company-wide — whose the admin-only pages are in Enforced. */
  companyAdmin?: boolean
}

const BOTH: Role[] = ['Admin', 'Manager']
const ADMIN: Role[] = ['Admin']

/**
 * Why each page sits where it does.
 *
 * A manager gets what is a VIEW OF THE BRANCHES THEY RUN, once the server narrows it to them. They do
 * not get what is company-wide by nature — a broadcast has no per-branch audience, a job-title rename
 * rewrites every employee's title, a holiday moves the payroll divisor for everyone, the subscription
 * is one contract — because such a page cannot be scoped, only hidden, and hiding is not a boundary.
 */
export const PANEL_PAGES: PanelPage[] = [
  // ── The day ────────────────────────────────────────────────────────────────────────────────────
  { path: 'dashboard', label: 'İdarəetmə paneli', roles: BOTH, perms: ['dashboard.view'] },
  { path: 'today', label: 'Bugünkü davamiyyət', roles: BOTH, perms: ['attendance.view'] },
  { path: 'problems', label: 'Problemlər', roles: BOTH, perms: ['attendance.view'] },
  // A wrong shift is fixed by whoever knows when the crew actually works — the branch manager. The
  // endpoint narrows their view to their own branches and plain employees, the same as every board.
  { path: 'shift-mismatch', label: 'Növbə uyğunsuzluğu', roles: BOTH, perms: ['schedules.view'] },
  { path: 'geofence-fit', label: 'Radius uyğunsuzluğu', roles: BOTH, perms: ['locations.view'] },
  { path: 'field-visits', label: 'Sahə ziyarətləri', roles: BOTH, perms: ['field_visits.view'] },
  { path: 'tasks', label: 'Tapşırıqlar', roles: BOTH, perms: ['tasks.use'] },

  // ── People ─────────────────────────────────────────────────────────────────────────────────────
  // One staff list for both since Phase 4: the page reads the admin roster for an admin and the
  // manager's own (their branches, no salary, no role) for a manager — it is no longer an admin
  // screen that a manager's 403 would draw as «Hələ işçi yoxdur».
  { path: 'employees', label: 'İşçilər', roles: BOTH, perms: ['employees.view'] },
  // The headline gap: a manager could not open one of their own people's cards at all. Served by
  // GET /api/manager/employees/{id}, which carries no salary and no role.
  { path: 'employees/:id', label: 'İşçi profili', menu: false, roles: BOTH, perms: ['employees.view'] },
  // Open to a manager now that the page reads the register through their own endpoints — their
  // branches, plain staff, and the leave rows that belong to them. It was ADMIN because a 403 here
  // renders as "Qeyd yoxdur", a confident empty register rather than an error, and this is the
  // register that decides whether a day counts as unexcused absence.
  { path: 'leaves', label: 'Məzuniyyət / İcazə', roles: BOTH, perms: ['leaves.view'] },
  { path: 'birthdays', label: 'Doğum günləri', roles: BOTH, perms: ['employees.view'] },
  { path: 'pin-resets', label: 'PIN sıfırlama', roles: BOTH, perms: ['employees.reset_pin'] },
  { path: 'device-changes', label: 'Cihazlar', roles: BOTH, perms: ['devices.view'] },
  { path: 'open-records', label: 'Çıxışı unudulan günlər', roles: BOTH, perms: ['attendance.view'] },
  // Mass account creation, and account deletion by another name.
  { path: 'bulk-invite', label: 'Toplu əlavə', roles: ADMIN, perms: ['employees.import'] },
  // The IT equipment register. Company-wide by nature: its "İşlədiyi ərazi" is free text naming
  // offices and sites that are not branches in the attendance system, so there is nothing here for
  // LocationScopeRules to narrow — it could only be hidden, and hiding is not a boundary.
  { path: 'equipment', label: 'Kompüter inventarizasiyası', roles: ADMIN, perms: ['equipment.view'] },
  // The manager's old roster and leave screens. Both are redirects now — to the one staff list and
  // the one register — kept as rows because a manager may have them bookmarked or open in a tab.
  { path: 'my-employees', label: 'İşçilər (köhnə ünvan)', menu: false, roles: BOTH, perms: ['employees.view'] },
  { path: 'my-leaves', label: 'Məzuniyyət (köhnə ünvan)', menu: false, roles: BOTH, perms: ['leaves.view'] },

  // ── Reports ────────────────────────────────────────────────────────────────────────────────────
  { path: 'reports', label: 'Hesabat', roles: BOTH, perms: ['attendance.view'] },
  { path: 'tabel', label: 'Aylıq tabel', roles: BOTH, perms: ['attendance.view'] },
  // Owner's call, 2026-08-28. A branch manager who cannot see what their own staff are owed has to
  // ask the admin about every question of it. The server drops peers' and the admin's own salary from
  // a manager's table — the attribute alone was never the boundary.
  { path: 'payroll', label: 'Maaş', roles: BOTH, perms: ['payroll.view'] },

  // ── Setup ──────────────────────────────────────────────────────────────────────────────────────
  { path: 'locations', label: 'Filiallar', roles: BOTH, perms: ['locations.view'] },
  { path: 'locations/:locationId/print-qr', label: 'QR çapı', menu: false, roles: BOTH, perms: ['locations.qr_print'] },
  { path: 'schedules', label: 'Növbələr', roles: BOTH, perms: ['schedules.view'] },

  // ── Company-wide: admin only ───────────────────────────────────────────────────────────────────
  // A broadcast reaches every employee; there is no per-branch audience to scope it to.
  { path: 'announcements', label: 'Elanlar', roles: ADMIN, perms: ['announcements.manage'] },
  // What the company pays and whether it is settled — one contract, not a branch's business.
  { path: 'billing', label: 'Abunəlik', roles: ADMIN, perms: ['billing.view'] },
  // Renaming or merging a job title rewrites it on every employee holding it.
  { path: 'positions', label: 'Vəzifələr', roles: ADMIN, perms: ['positions.manage'] },
  // Who holds which role where. The one page the model opens before the switch (the owner's decision
  // of 2026-10-15): the API behind it admits whoever holds «assignments.view», whatever their role
  // today, so a structure operator makes their own timekeepers. See canOpen.
  { path: 'access', label: 'Səlahiyyətlər', roles: ADMIN, perms: ['assignments.view'] },
  // A holiday changes the payroll divisor for the whole company.
  { path: 'non-working-days', label: 'Qeyri-iş günləri', roles: ADMIN, perms: ['holidays.manage'] },
  { path: 'vote', label: 'Ayın işçisi', roles: ADMIN, perms: ['votes.manage'] },

  // ── Everybody in the panel ─────────────────────────────────────────────────────────────────────
  // What I hold and where, and what opens for me after the switch. The landing page of somebody who
  // has a role in the new model but no panel role today.
  { path: 'my-area', label: 'Mənim sahəm', roles: BOTH, perms: [] },
]

const BY_PATH = new Map(PANEL_PAGES.map((p) => [p.path, p]))

/** The pages the permission model opens before the switch, for whoever holds what they ask. */
const MODEL_GATED = new Set(['access', 'my-area'])

function holds(access: PanelAccess | null | undefined, perms: string[]): boolean {
  if (!access?.panel) return false
  if (perms.length === 0) return true
  return access.permissions.some((p) => perms.includes(p.key))
}

/**
 * What the permission model opens — the panel of a company switched to Enforced, and what «after the switch»
 * means anywhere else. A page today's admin alone may open is the company-wide shape of something (the
 * admin's staff list, a holiday, the subscription): it stays the Company administrator's, exactly as the
 * server keeps its endpoints. Everything else opens for whoever holds one of its permissions.
 */
function modelOpens(page: PanelPage, access: PanelAccess | null | undefined): boolean {
  if (!holds(access, page.perms)) return false
  if (MODEL_GATED.has(page.path) || page.roles.includes('Manager')) return true
  return !!access?.companyAdmin
}

/**
 * May this person open this page TODAY?
 *
 * Their role decides, as it always has — except for the two pages the model opens already (see
 * MODEL_GATED), which open for whoever holds what they need.
 *
 * An unknown path is refused rather than allowed. A route added without a row here is then a visible
 * mistake in development instead of an open door in production — the failure the table exists to end
 * ran the other way round, and quietly.
 */
export function canOpen(path: string, role: Role | null | undefined, access?: PanelAccess | null): boolean {
  const page = BY_PATH.get(path)
  if (!page) return false
  // A company switched to Enforced (Phase 5): the permission model opens every page, for everybody —
  // the role in the token decides nothing any more, the same as on the server.
  if (access?.mode === 'Enforced') return modelOpens(page, access)
  if (role && page.roles.includes(role)) return true
  return MODEL_GATED.has(path) && holds(access, page.perms)
}

/** Would the permission model open this page — what the panel will look like after the switch. */
export function wouldOpen(path: string, access: PanelAccess | null | undefined): boolean {
  const page = BY_PATH.get(path)
  return !!page && modelOpens(page, access)
}

/** Every path this person may open today — the sidebar filters against this. */
export function pagesFor(role: Role | null | undefined, access?: PanelAccess | null): Set<string> {
  return new Set(PANEL_PAGES.filter((p) => canOpen(p.path, role, access)).map((p) => p.path))
}

/** Pages the model would open for this person that their role does not open yet — «keçiddən sonra». */
export function pagesAfterSwitch(role: Role | null | undefined, access: PanelAccess | null | undefined): Set<string> {
  // After the switch there is no «after the switch».
  if (access?.mode === 'Enforced') return new Set()
  return new Set(
    PANEL_PAGES.filter((p) => !canOpen(p.path, role, access) && wouldOpen(p.path, access)).map((p) => p.path),
  )
}
