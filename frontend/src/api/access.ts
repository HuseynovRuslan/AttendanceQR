import { apiRequest } from './client'

/**
 * Roles, assignments and structures — /api/admin/access and /api/admin/structures (Phase 3 of
 * docs/strukturlar-ve-selahiyyetler.md). Every answer is the permission model's own, read from the database on
 * every request: a role given or taken away counts from the person's next request, nobody is signed out.
 */

export type AccessScope = 'Company' | 'Structure' | 'Branch'

export interface PermissionInfo {
  key: string
  label: string
  /** Meaningful only for the whole company — a holiday, the job titles — never given over a branch. */
  companyOnly: boolean
  /** A right about rights (assigning, roles) — left out when two roles' levels are compared. */
  meta: boolean
}

export interface RoleTemplateInfo {
  key: string
  name: string
  description: string
  isLocked: boolean
  isLegacy: boolean
}

export interface AccessRoleRow {
  id: string
  name: string
  description: string | null
  templateKey: string | null
  /** Defined by the code: the company administrator and today's two roles. Never edited here. */
  isLocked: boolean
  /** Today's manager roles — they follow the employee form's role and branches, nothing else. */
  isLegacy: boolean
  permissions: string[]
  activeAssignments: number
  archived: boolean
}

export interface AssignmentRow {
  id: string
  employeeId: string
  employeeName: string | null
  employeeHome: string | null
  roleId: string
  roleName: string | null
  templateKey: string | null
  scope: AccessScope
  structureId: string | null
  locationId: string | null
  area: string
  /** Mirrors today's Employee.Role / ManagedLocations — changed on the employee form, not here. */
  system: boolean
  createdAtUtc: string
  createdByName: string | null
  revokedAtUtc: string | null
  revokedByName: string | null
  /** May THIS caller take it away — the revoke's own rule, asked in advance. */
  canRevoke: boolean
}

export interface PersonAccess {
  employeeId: string
  fullName: string
  isOperator: boolean
  assignments: AssignmentRow[]
  permissions: { key: string; label: string; company: boolean; branches: string[] }[]
}

export interface AssignableRole {
  roleId: string
  name: string
  templateKey: string | null
  company: boolean
  structureIds: string[]
  locationIds: string[]
}

export interface Assignable {
  employeeActive: boolean
  /** Why nothing can be offered, when nothing can. */
  reason: string | null
  roles: AssignableRole[]
  structures: { id: string; name: string }[]
  branches: { id: string; name: string; structureId: string | null }[]
}

export interface PersonHit {
  id: string
  fullName: string
  home: string | null
  position: string | null
  isOperator: boolean
}

export interface StructureBranch {
  id: string
  name: string
  isActive: boolean
}

export interface StructureRow {
  id: string
  name: string
  archived: boolean
  createdAtUtc: string
  locations: StructureBranch[]
}

type Err = { error: string; permission?: string | null; count?: number }

export const getAccessCatalogue = () =>
  apiRequest<{ permissions: PermissionInfo[]; templates: RoleTemplateInfo[] }>('/api/admin/access/catalogue')

export const getAccessRoles = (includeArchived = false) =>
  apiRequest<AccessRoleRow[]>(`/api/admin/access/roles${includeArchived ? '?includeArchived=true' : ''}`)

export const createAccessRole = (body: { name: string; description: string | null; permissions: string[] }) =>
  apiRequest<AccessRoleRow | Err>('/api/admin/access/roles', { method: 'POST', body })

export const updateAccessRole = (id: string, body: { name: string; description: string | null; permissions: string[] }) =>
  apiRequest<AccessRoleRow | Err>(`/api/admin/access/roles/${id}`, { method: 'PUT', body })

export const archiveAccessRole = (id: string) =>
  apiRequest<AccessRoleRow | Err>(`/api/admin/access/roles/${id}/archive`, { method: 'POST' })

export const restoreAccessRole = (id: string) =>
  apiRequest<AccessRoleRow | Err>(`/api/admin/access/roles/${id}/restore`, { method: 'POST' })

export function getAssignments(filter: { employeeId?: string; roleId?: string; includeRevoked?: boolean } = {}) {
  const q = new URLSearchParams()
  if (filter.employeeId) q.set('employeeId', filter.employeeId)
  if (filter.roleId) q.set('roleId', filter.roleId)
  if (filter.includeRevoked) q.set('includeRevoked', 'true')
  const qs = q.toString()
  return apiRequest<AssignmentRow[]>(`/api/admin/access/assignments${qs ? `?${qs}` : ''}`)
}

export const getPersonAccess = (employeeId: string) =>
  apiRequest<PersonAccess | Err>(`/api/admin/access/people/${employeeId}`)

export const searchAccessPeople = (search: string) =>
  apiRequest<PersonHit[]>(`/api/admin/access/people?search=${encodeURIComponent(search)}`)

export const getAssignable = (employeeId: string) =>
  apiRequest<Assignable | Err>(`/api/admin/access/assignable?employeeId=${employeeId}`)

export const assignRole = (body: {
  employeeId: string
  roleId: string
  scope: AccessScope
  structureId?: string | null
  locationIds?: string[] | null
}) => apiRequest<{ created: AssignmentRow[]; alreadyHeld: number } | Err>('/api/admin/access/assignments', { method: 'POST', body })

export const revokeAssignment = (id: string) =>
  apiRequest<AssignmentRow | Err>(`/api/admin/access/assignments/${id}/revoke`, { method: 'POST' })

// ── Structures (Phase 1 API) ─────────────────────────────────────────────────────────────────────

export const getStructures = () =>
  apiRequest<{ structures: StructureRow[]; unassigned: StructureBranch[] }>('/api/admin/structures')

export const createStructure = (name: string) =>
  apiRequest<unknown>('/api/admin/structures', { method: 'POST', body: { name } })

export const renameStructure = (id: string, name: string) =>
  apiRequest<unknown>(`/api/admin/structures/${id}`, { method: 'PUT', body: { name } })

export const archiveStructure = (id: string) =>
  apiRequest<unknown>(`/api/admin/structures/${id}/archive`, { method: 'POST' })

export const restoreStructure = (id: string) =>
  apiRequest<unknown>(`/api/admin/structures/${id}/restore`, { method: 'POST' })

/** Puts branches into a structure, or takes them out (structureId null). */
export const moveBranches = (structureId: string | null, locationIds: string[]) =>
  apiRequest<unknown>('/api/admin/structures/assign', { method: 'PUT', body: { structureId, locationIds } })

// ── What a refusal means, in the panel's words ───────────────────────────────────────────────────

const ACCESS_ERROR: Record<string, string> = {
  SelfGrant: 'Özünüzə rol verə bilməzsiniz.',
  SelfRevoke: 'Öz rolunuzu geri ala bilməzsiniz.',
  CannotManageOperator: 'Platforma operatorunun hüquqları buradan dəyişmir.',
  OutOfScope: 'Bu sahədə rol vermək hüququnuz yoxdur.',
  AssigneeOutOfScope: 'Bu işçi sizin sahənizdə işləmir. Başqa sahənin işçisinə rol vermək üçün ayrıca hüquq lazımdır.',
  NotBelowYou: 'Yalnız özünüzdən aşağı səviyyəli rol verə bilərsiniz. Bərabər rolu şirkət administratoru verir.',
  AssigneeNotBelowYou: 'Sizinlə eyni və ya yuxarı səviyyədə olan şəxsin rolunu dəyişə bilməzsiniz.',
  PermissionNotHeld: 'Bu rolda sizdə olmayan hüquq var',
  CompanyOnlyPermission: 'Bu hüquq yalnız bütün şirkət üzrə verilə bilər',
  UnknownPermission: 'Naməlum hüquq',
  LegacyRole: 'Bu rol işçi formasındakı vəzifə və filiallardan avtomatik gəlir — orada dəyişin.',
  SystemAssignment: 'Bu hüquq işçinin rolundan və filiallarından gəlir — işçi formasında dəyişin.',
  NotDuringImpersonation: 'Dəstək sessiyasında rol verilmir və geri alınmır.',
  EmployeeInactive: 'İşçi deaktivdir.',
  EmployeeNotFound: 'İşçi tapılmadı.',
  RoleNotFound: 'Rol tapılmadı — səhifəni yeniləyin.',
  StructureNotFound: 'Struktur tapılmadı.',
  LocationNotFound: 'Filial tapılmadı.',
  ScopeMismatch: 'Sahə düzgün seçilməyib.',
  TooMany: 'Bir dəfədə ən çox 100 filial seçmək olar.',
  AlreadyHeld: 'Bu hüquq artıq verilib.',
  AlreadyRevoked: 'Artıq geri alınıb.',
  AssignmentNotFound: 'Təyinat tapılmadı — səhifəni yeniləyin.',
  RoleLocked: 'Bu rol kodla müəyyən olunur və dəyişdirilmir.',
  RoleArchived: 'Rol arxivdədir.',
  RoleInUse: 'Rol hələ kiməsə verilib — əvvəl təyinatları geri alın.',
  NameTaken: 'Bu adda rol artıq var.',
  NameRequired: 'Rolun adını yazın (ən azı 2 hərf).',
  NoPermissions: 'Ən azı bir hüquq seçin.',
  DescriptionTooLong: 'Təsvir çox uzundur.',
  CompanyOnlyPermissionInAreaAssignment:
    'Bu rol struktur və ya filial üzrə verilib — ona yalnız şirkət üzrə olan hüquq əlavə etmək olmaz.',
}

/** The Azerbaijani sentence for a refusal from these endpoints; the permission's own name where one is named. */
export function accessErrorText(data: unknown, labels: Record<string, string> = {}): string {
  const err = data && typeof data === 'object' && 'error' in data ? (data as Err) : null
  if (!err) return 'Əməliyyat alınmadı.'
  const base = ACCESS_ERROR[err.error] ?? 'Əməliyyat alınmadı.'
  return err.permission ? `${base}: «${labels[err.permission] ?? err.permission}».` : base
}

export const isAccessError = (data: unknown): data is Err =>
  !!data && typeof data === 'object' && 'error' in data
