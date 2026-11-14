import { describe, expect, it } from 'vitest'
import {
  ALL_BRANCHES, CHOSEN_BRANCH, MY_BRANCHES, REACHABLE_BRANCHES, tabelScopeLabel, wholeScopeLabel,
} from './tabelScope'

// The tabel heading used to print the server's scopeLabel («All locations», «Location <guid>»). These pin
// the replacement: the words come from /api/me/access — the facts the server scopes by — and the heading
// is never empty, whatever is missing.

const perm = (key: string, company: boolean) => ({ key, label: key, company, locationIds: company ? [] : ['b1'] })
const legacy = (legacyRole: 'Admin' | 'Manager' | 'Employee') =>
  ({ mode: 'Legacy' as const, legacyRole, companyAdmin: false, permissions: [] })
const enforced = (companyAdmin: boolean, permissions: ReturnType<typeof perm>[], legacyRole: 'Admin' | 'Manager' = 'Manager') =>
  ({ mode: 'Enforced' as const, legacyRole, companyAdmin, permissions })

describe('wholeScopeLabel', () => {
  it('follows the role the request carries outside Enforced — what the server scopes by there', () => {
    expect(wholeScopeLabel(legacy('Admin'))).toBe(ALL_BRANCHES)
    expect(wholeScopeLabel(legacy('Manager'))).toBe(MY_BRANCHES)
    expect(wholeScopeLabel({ ...legacy('Manager'), mode: 'Shadow' as const })).toBe(MY_BRANCHES)
  })

  it('in Enforced, follows where «attendance.view» is held — not the token role', () => {
    expect(wholeScopeLabel(enforced(false, [perm('attendance.view', true)]))).toBe(ALL_BRANCHES)
    expect(wholeScopeLabel(enforced(false, [perm('attendance.view', false)]))).toBe(MY_BRANCHES)
    expect(wholeScopeLabel(enforced(true, []))).toBe(ALL_BRANCHES)
    // A token-Admin whose model role was narrowed: the model decides.
    expect(wholeScopeLabel(enforced(false, [perm('attendance.view', false)], 'Admin'))).toBe(MY_BRANCHES)
    // Another right held company-wide does not widen the tabel.
    expect(wholeScopeLabel(enforced(false, [perm('employees.update', true), perm('attendance.view', false)]))).toBe(MY_BRANCHES)
  })

  it('says only what is true for everybody until the access answer is in — the mode is unknown then', () => {
    expect(wholeScopeLabel(null)).toBe(REACHABLE_BRANCHES)
    expect(wholeScopeLabel(legacy('Employee'))).toBe(REACHABLE_BRANCHES)
  })
})

describe('tabelScopeLabel', () => {
  const locations = [{ id: 'a', name: 'Mağaza — 28 May' }]
  const access = { ...legacy('Admin'), branches: [{ id: 'b', name: 'Anbar — Binəqədi', structureId: null }] }

  it('names a chosen branch from the dropdown list, then from the access answer', () => {
    expect(tabelScopeLabel('a', locations, access)).toBe('Mağaza — 28 May')
    expect(tabelScopeLabel('b', locations, access)).toBe('Anbar — Binəqədi')
  })

  it('says «Seçilmiş filial» rather than a GUID or nothing when the name is unknown', () => {
    expect(tabelScopeLabel('afb20653-4bfa-4145-ba40-8020fdd1d7f7', [], null)).toBe(CHOSEN_BRANCH)
  })

  it('without a chosen branch, says what «all» covers for this person', () => {
    expect(tabelScopeLabel('', locations, access)).toBe(ALL_BRANCHES)
    expect(tabelScopeLabel('', locations, legacy('Manager'))).toBe(MY_BRANCHES)
    expect(tabelScopeLabel('', locations, null)).toBe(REACHABLE_BRANCHES)
  })
})
