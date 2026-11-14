import type { MyAccess } from '../api/me'

export const ALL_BRANCHES = 'Bütün filiallar'
export const MY_BRANCHES = 'Mənim filiallarım'
/** Before /api/me/access has answered (or when it failed). True for everybody, so never wrong. */
export const REACHABLE_BRANCHES = 'Əlçatan filiallar'
/** A chosen branch whose name is in neither list — the choice is still real, just unnamed. */
export const CHOSEN_BRANCH = 'Seçilmiş filial'

type Access = Pick<MyAccess, 'mode' | 'legacyRole' | 'companyAdmin' | 'permissions'> & {
  branches?: MyAccess['branches']
}

/**
 * What «no branch chosen» covers for this person, in words.
 *
 * Decided from the same facts the server narrows the tabel by (LocationScope), all read from
 * /api/me/access: in a company switched to Enforced, whether «attendance.view» is held company-wide;
 * anywhere else, the role the request carries (`legacyRole`, the token's own) — Admin sees every branch,
 * Manager the ones they run. Until that answer is in, the company's MODE is unknown, and in Enforced the
 * token's role is not what decides — so the heading says only what is true for everybody rather than
 * guess.
 *
 * Not from the report's scopeLabel. That is an English key written for logs («All locations»,
 * «Location <guid>»); printing it put a GUID into the accountant's title, and a heading matched on its
 * exact words goes blank the day somebody rewords it.
 */
export function wholeScopeLabel(access: Access | null): string {
  if (!access) return REACHABLE_BRANCHES
  if (access.mode === 'Enforced') {
    const companyWide = access.companyAdmin
      || access.permissions.some((p) => p.key === 'attendance.view' && p.company)
    return companyWide ? ALL_BRANCHES : MY_BRANCHES
  }
  if (access.legacyRole === 'Admin') return ALL_BRANCHES
  if (access.legacyRole === 'Manager') return MY_BRANCHES
  return REACHABLE_BRANCHES
}

/** The tabel heading's scope: the chosen branch by name, or what «all» means for this person. Never empty. */
export function tabelScopeLabel(
  locationId: string,
  locations: { id: string; name: string }[],
  access: Access | null,
): string {
  if (!locationId) return wholeScopeLabel(access)
  return locations.find((l) => l.id === locationId)?.name
    ?? access?.branches?.find((b) => b.id === locationId)?.name
    ?? CHOSEN_BRANCH
}
