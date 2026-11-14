import { describe, expect, it } from 'vitest'
import {
  ALL_BRANCHES, CHOSEN_BRANCH, MY_BRANCHES, REACHABLE_BRANCHES, tabelScopeLabel, wholeScopeLabel,
} from './tabelScope'

// The tabel heading used to print the server's scopeLabel («All locations», «Location <guid>»). These pin
// the replacement: the words come from the role and /api/me/access — the facts the server scopes by —
// and the heading is never empty, whatever is missing.

const perm = (key: string, company: boolean) => ({ key, label: key, company, locationIds: company ? [] : ['b1'] })
const legacy = (legacyRole: 'Admin' | 'Manager' | 'Employee') =>
  ({ mode: 'Legacy' as const, legacyRole, companyAdmin: false, permissions: [] })
const enforced = (companyAdmin: boolean, permissions: ReturnType<typeof perm>[]) =>
  ({ mode: 'Enforced' as const, legacyRole: 'Manager' as const, companyAdmin, permissions })

describe('wholeScopeLabel', () => {
  it('follows the role outside Enforced — the access answer first, the token while it is missing', () => {
    expect(wholeScopeLabel('Admin', legacy('Admin'))).toBe(ALL_BRANCHES)
    expect(wholeScopeLabel('Manager', legacy('Manager'))).toBe(MY_BRANCHES)
    expect(wholeScopeLabel('Admin', null)).toBe(ALL_BRANCHES)
    expect(wholeScopeLabel('Manager', null)).toBe(MY_BRANCHES)
    // A token from before a demotion: the database's answer wins.
    expect(wholeScopeLabel('Admin', legacy('Manager'))).toBe(MY_BRANCHES)
  })

  it('in Enforced, follows where «attendance.view» is held', () => {
    expect(wholeScopeLabel('Manager', enforced(false, [perm('attendance.view', true)]))).toBe(ALL_BRANCHES)
    expect(wholeScopeLabel('Manager', enforced(false, [perm('attendance.view', false)]))).toBe(MY_BRANCHES)
    expect(wholeScopeLabel('Manager', enforced(true, []))).toBe(ALL_BRANCHES)
    // Another right held company-wide does not widen the tabel.
    expect(wholeScopeLabel('Manager', enforced(false, [perm('employees.update', true), perm('attendance.view', false)]))).toBe(MY_BRANCHES)
  })

  it('never returns nothing — with no role and no access answer it says what is always true', () => {
    expect(wholeScopeLabel(null, null)).toBe(REACHABLE_BRANCHES)
    expect(wholeScopeLabel('Employee', null)).toBe(REACHABLE_BRANCHES)
  })
})

describe('tabelScopeLabel', () => {
  const locations = [{ id: 'a', name: 'Mağaza — 28 May' }]
  const access = { ...legacy('Admin'), branches: [{ id: 'b', name: 'Anbar — Binəqədi', structureId: null }] }

  it('names a chosen branch from the dropdown list, then from the access answer', () => {
    expect(tabelScopeLabel('a', locations, 'Admin', access)).toBe('Mağaza — 28 May')
    expect(tabelScopeLabel('b', locations, 'Admin', access)).toBe('Anbar — Binəqədi')
  })

  it('says «Seçilmiş filial» rather than a GUID or nothing when the name is unknown', () => {
    expect(tabelScopeLabel('afb20653-4bfa-4145-ba40-8020fdd1d7f7', [], 'Admin', null)).toBe(CHOSEN_BRANCH)
  })

  it('without a chosen branch, says what «all» covers for this person', () => {
    expect(tabelScopeLabel('', locations, 'Admin', access)).toBe(ALL_BRANCHES)
    expect(tabelScopeLabel('', locations, 'Manager', null)).toBe(MY_BRANCHES)
  })
})
