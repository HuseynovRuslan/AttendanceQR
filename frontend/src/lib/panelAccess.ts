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
 */

export type PanelSection = 'daily' | 'people' | 'reports' | 'setup' | 'company'

export interface PanelPage {
  /** Path relative to /admin — matches the <Route path="…"> exactly. */
  path: string
  roles: Role[]
}

const BOTH: Role[] = ['Admin', 'Manager']
const ADMIN: Role[] = ['Admin']
const MANAGER: Role[] = ['Manager']

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
  { path: 'dashboard', roles: BOTH },
  { path: 'today', roles: BOTH },
  { path: 'problems', roles: BOTH },
  { path: 'field-visits', roles: BOTH },
  { path: 'tasks', roles: BOTH },

  // ── People ─────────────────────────────────────────────────────────────────────────────────────
  // ADMIN until the shared roster is role-branched. Opening the route was premature: EmployeesPage's
  // only data source is the admin roster, which 403s for a manager — and the page renders that as
  // "Hələ işçi yoxdur", a confident empty list rather than an error. A false zero on a staff list is
  // worse than a locked door. The manager's own roster is my-employees until they merge.
  { path: 'employees', roles: ADMIN },
  // The headline gap: a manager could not open one of their own people's cards at all. Served by
  // GET /api/manager/employees/{id}, which carries no salary and no role.
  { path: 'employees/:id', roles: BOTH },
  // Same, and it matters more here: a 403 renders as "Qeyd yoxdur", and this is the register that
  // decides whether a day counts as unexcused absence. Managers use my-leaves.
  { path: 'leaves', roles: ADMIN },
  { path: 'birthdays', roles: BOTH },
  { path: 'pin-resets', roles: BOTH },
  { path: 'device-changes', roles: BOTH },
  { path: 'open-records', roles: BOTH },
  // Mass account creation, and account deletion by another name.
  { path: 'bulk-invite', roles: ADMIN },
  // The manager's own roster/leave screens. They exist because the shared ones were admin-only; once
  // those are role-branched these become redirects. Manager-only meanwhile, so an admin typing the
  // URL gets an explanation rather than a page of 403s.
  { path: 'my-employees', roles: MANAGER },
  { path: 'my-leaves', roles: MANAGER },

  // ── Reports ────────────────────────────────────────────────────────────────────────────────────
  { path: 'reports', roles: BOTH },
  { path: 'tabel', roles: BOTH },
  // Owner's call, 2026-08-28. A branch manager who cannot see what their own staff are owed has to
  // ask the admin about every question of it. The server drops peers' and the admin's own salary from
  // a manager's table — the attribute alone was never the boundary.
  { path: 'payroll', roles: BOTH },

  // ── Setup ──────────────────────────────────────────────────────────────────────────────────────
  { path: 'locations', roles: BOTH },
  { path: 'locations/:locationId/print-qr', roles: BOTH },
  { path: 'schedules', roles: BOTH },

  // ── Company-wide: admin only ───────────────────────────────────────────────────────────────────
  // A broadcast reaches every employee; there is no per-branch audience to scope it to.
  { path: 'announcements', roles: ADMIN },
  // What the company pays and whether it is settled — one contract, not a branch's business.
  { path: 'billing', roles: ADMIN },
  // Renaming or merging a job title rewrites it on every employee holding it.
  { path: 'positions', roles: ADMIN },
  // A holiday changes the payroll divisor for the whole company.
  { path: 'non-working-days', roles: ADMIN },
  { path: 'vote', roles: ADMIN },
]

const BY_PATH = new Map(PANEL_PAGES.map((p) => [p.path, p]))

/**
 * May this role open this page?
 *
 * An unknown path is refused rather than allowed. A route added without a row here is then a visible
 * mistake in development instead of an open door in production — the failure the table exists to end
 * ran the other way round, and quietly.
 */
export function canOpen(path: string, role: Role | null | undefined): boolean {
  if (!role) return false
  const page = BY_PATH.get(path)
  if (!page) return false
  return page.roles.includes(role)
}

/** Every path a role may open — the sidebar filters against this. */
export function pagesFor(role: Role | null | undefined): Set<string> {
  return new Set(PANEL_PAGES.filter((p) => role && p.roles.includes(role)).map((p) => p.path))
}
