import type { MyAccess } from '../api/me'
import type { Role } from './jwt'

export const ALL_BRANCHES = 'Bütün filiallar'
export const MY_BRANCHES = 'Mənim filiallarım'
/** When neither the role nor the access answer is at hand. True for everybody, so never wrong. */
export const REACHABLE_BRANCHES = 'Əlçatan filiallar'
/** A chosen branch whose name is in neither list — the choice is still real, just unnamed. */
export const CHOSEN_BRANCH = 'Seçilmiş filial'

type Access = Pick<MyAccess, 'mode' | 'legacyRole' | 'companyAdmin' | 'permissions'> & {
  branches?: MyAccess['branches']
}

/**
 * What «no branch chosen» covers for this person, in words.
 *
 * Decided from the same two facts the server narrows the tabel by (LocationScope): in a company switched
 * to Enforced, whether «attendance.view» is held company-wide; anywhere else, the role — Admin sees every
 * branch, Manager the ones they run. The /api/me/access answer is preferred because it is read from the
 * database; the token's role covers the moment before it arrives, or a request that failed.
 *
 * Not from the report's scopeLabel. That is an English key written for logs («All locations»,
 * «Location <guid>»); printing it put a GUID into the accountant's title, and a heading matched on its
 * exact words goes blank the day somebody rewords it.
 */
export function wholeScopeLabel(role: Role | null, access: Access | null): string {
  if (access?.mode === 'Enforced') {
    const companyWide = access.companyAdmin
      || access.permissions.some((p) => p.key === 'attendance.view' && p.company)
    return companyWide ? ALL_BRANCHES : MY_BRANCHES
  }
  const r = access?.legacyRole ?? role
  if (r === 'Admin') return ALL_BRANCHES
  if (r === 'Manager') return MY_BRANCHES
  return REACHABLE_BRANCHES
}

/** The tabel heading's scope: the chosen branch by name, or what «all» means for this person. Never empty. */
export function tabelScopeLabel(
  locationId: string,
  locations: { id: string; name: string }[],
  role: Role | null,
  access: Access | null,
): string {
  if (!locationId) return wholeScopeLabel(role, access)
  return locations.find((l) => l.id === locationId)?.name
    ?? access?.branches?.find((b) => b.id === locationId)?.name
    ?? CHOSEN_BRANCH
}
