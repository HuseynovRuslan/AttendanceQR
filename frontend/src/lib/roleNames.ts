/**
 * The names roles are SHOWN under — the owner's hierarchy of 2026-11-12:
 * Company Admin → Structure Manager → Timekeeper → Employee, in English everywhere in the panel.
 *
 * Display only. Who is what is decided by the server (RoleDisplay: `displayRole` on /api/me/access, the labels
 * of /api/me/role-labels, `displayRole` on the console's impersonation list); every right is still decided by
 * the role and the assignments. The technical ids — Admin / Manager / Employee, structure_operator, timekeeper,
 * legacy_manager — are untouched.
 *
 * A company that has no Structure Manager keeps its branch managers under their old title («Menecer»): nobody's
 * title changes until their company adopts the hierarchy.
 */

export type DisplayRole = 'companyAdmin' | 'structureManager' | 'timekeeper' | 'manager' | 'employee'

export const DISPLAY_ROLE: Record<DisplayRole, string> = {
  companyAdmin: 'Company Admin',
  structureManager: 'Structure Manager',
  timekeeper: 'Timekeeper',
  manager: 'Menecer',
  employee: 'Employee',
}

type LegacyRole = 'Admin' | 'Manager' | 'Employee'

function isDisplayRole(key: unknown): key is DisplayRole {
  return typeof key === 'string' && key in DISPLAY_ROLE
}

/** A person's role, by the server's key; by today's role when the server sent none. */
export function displayRoleName(key: string | null | undefined, role?: LegacyRole | string | null): string {
  if (isDisplayRole(key)) return DISPLAY_ROLE[key]
  if (role === 'Admin') return DISPLAY_ROLE.companyAdmin
  if (role === 'Manager') return DISPLAY_ROLE.manager
  if (role === 'Employee') return DISPLAY_ROLE.employee
  return role ?? ''
}

/** The words of a role picker (Admin / Manager / Employee). In a company with a Structure Manager the branch
 *  manager role is offered as Timekeeper; elsewhere it keeps its old title. */
export function roleOptionName(role: LegacyRole, hierarchy: boolean): string {
  if (role === 'Admin') return DISPLAY_ROLE.companyAdmin
  if (role === 'Employee') return DISPLAY_ROLE.employee
  return hierarchy ? DISPLAY_ROLE.timekeeper : DISPLAY_ROLE.manager
}

// The built-in roles' default names (AccessRoleTemplates) and what they are shown as. Only the DEFAULT name is
// replaced: a company that renamed its copy of a role keeps its own name.
const TEMPLATE_NAME: Record<string, { stored: string; shown: string; onlyInHierarchy?: boolean }> = {
  company_admin: { stored: 'Şirkət administratoru', shown: 'Company Admin' },
  structure_operator: { stored: 'Struktur operatoru', shown: 'Structure Manager' },
  timekeeper: { stored: 'Tabelçi', shown: 'Timekeeper' },
  legacy_manager: { stored: 'Filial meneceri (köhnə qayda)', shown: 'Timekeeper (köhnə qayda)', onlyInHierarchy: true },
}

/** A role definition's name on the Səlahiyyətlər screens and wherever an assignment is listed. */
export function roleDefinitionName(name: string | null | undefined, templateKey: string | null | undefined, hierarchy = false): string {
  const stored = name ?? ''
  const t = templateKey ? TEMPLATE_NAME[templateKey] : undefined
  if (!t || stored !== t.stored) return stored
  if (t.onlyInHierarchy && !hierarchy) return stored
  return t.shown
}
