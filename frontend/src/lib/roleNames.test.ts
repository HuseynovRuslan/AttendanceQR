import { describe, expect, it } from 'vitest'
import { DISPLAY_ROLE, displayRoleName, roleDefinitionName, roleOptionName } from './roleNames'

describe('role names (display only)', () => {
  it('shows the server key in English', () => {
    expect(displayRoleName('companyAdmin')).toBe('Company Admin')
    expect(displayRoleName('structureManager', 'Manager')).toBe('Structure Manager')
    expect(displayRoleName('timekeeper', 'Manager')).toBe('Timekeeper')
    expect(displayRoleName('employee')).toBe('Employee')
  })

  it('a manager in a company without a Structure Manager keeps the old title', () => {
    expect(displayRoleName('manager', 'Manager')).toBe('Menecer')
    expect(displayRoleName(undefined, 'Manager')).toBe('Menecer')
  })

  it('falls back to today\'s role when the server sent no key', () => {
    expect(displayRoleName(null, 'Admin')).toBe('Company Admin')
    expect(displayRoleName(undefined, 'Employee')).toBe('Employee')
    expect(displayRoleName('nonsense', 'Admin')).toBe('Company Admin')
  })

  it('role pickers say Timekeeper only where the company has the hierarchy', () => {
    expect(roleOptionName('Manager', true)).toBe('Timekeeper')
    expect(roleOptionName('Manager', false)).toBe('Menecer')
    expect(roleOptionName('Admin', false)).toBe('Company Admin')
    expect(roleOptionName('Employee', true)).toBe('Employee')
  })

  it('renames a built-in role only under its default name', () => {
    expect(roleDefinitionName('Struktur operatoru', 'structure_operator')).toBe('Structure Manager')
    expect(roleDefinitionName('Şirkət administratoru', 'company_admin')).toBe('Company Admin')
    expect(roleDefinitionName('Tabelçi', 'timekeeper')).toBe('Timekeeper')
    // A company's own name for its copy stays its own.
    expect(roleDefinitionName('Sahə rəhbəri', 'structure_operator')).toBe('Sahə rəhbəri')
    // A role with no template is the company's own.
    expect(roleDefinitionName('Mühasib', null)).toBe('Mühasib')
  })

  it('the legacy manager role reads as Timekeeper only in a company with the hierarchy', () => {
    expect(roleDefinitionName('Filial meneceri (köhnə qayda)', 'legacy_manager', true)).toBe('Timekeeper (köhnə qayda)')
    expect(roleDefinitionName('Filial meneceri (köhnə qayda)', 'legacy_manager', false)).toBe('Filial meneceri (köhnə qayda)')
  })

  it('has exactly the four approved names plus the old manager title', () => {
    expect(Object.values(DISPLAY_ROLE)).toEqual(['Company Admin', 'Structure Manager', 'Timekeeper', 'Menecer', 'Employee'])
  })
})
