import { describe, expect, it } from 'vitest'
import type { ManagerEmployee } from '../api/manager'
import { filterRoster, rosterPage, rosterSelection, rosterStats, toRosterRow } from './managerRoster'

function person(over: Partial<ManagerEmployee> & { id: string; fullName: string }): ManagerEmployee {
  return {
    firstName: null, lastName: null, fatherName: null, position: null, phoneNumber: null, email: null,
    locationId: 'park', locationName: 'Park', birthDate: null, birthYear: null, workStart: null, workEnd: null,
    scheduleId: null, workCycleDays: null, workCycleOnDays: null, workCycleAnchor: null,
    photoExempt: false, canFieldCheckIn: false, canShareDevice: false, isActive: true, activated: true,
    manageable: true, isColleague: false,
    ...over,
  }
}

const ROSTER: ManagerEmployee[] = [
  person({ id: 'z', fullName: 'Zaur Əliyev', position: 'Bağban', phoneNumber: '501112233' }),
  person({ id: 'e', fullName: 'Əli Məmmədov', locationId: 'meydan', locationName: 'Meydan', activated: false }),
  person({ id: 'a', fullName: 'Aynur Həsənova', canShareDevice: true }),
  person({ id: 'm', fullName: 'Murad Menecer', isColleague: true, manageable: true }),
  person({ id: 'x', fullName: 'Xanım Admin', isColleague: true, manageable: false }),
  person({ id: 'l', fullName: 'Leyla Çıxıb', isActive: false }),
]

const ALL = { search: '', locationId: null, showLeft: false }

describe('a manager’s staff on the shared list', () => {
  it('orders names the Azerbaijani way — Ə after A, and X between H and M', () => {
    expect(filterRoster(ROSTER, ALL).map((e) => e.id)).toEqual(['a', 'e', 'x', 'm', 'z'])
  })

  it('keeps leavers on their own list, as the admin roster does', () => {
    expect(filterRoster(ROSTER, { ...ALL, showLeft: true }).map((e) => e.id)).toEqual(['l'])
  })

  it('searches name, phone, job title and branch', () => {
    expect(filterRoster(ROSTER, { ...ALL, search: 'bağban' }).map((e) => e.id)).toEqual(['z'])
    expect(filterRoster(ROSTER, { ...ALL, search: '5011' }).map((e) => e.id)).toEqual(['z'])
    expect(filterRoster(ROSTER, { ...ALL, search: 'meydan' }).map((e) => e.id)).toEqual(['e'])
  })

  it('narrows by branch', () => {
    expect(filterRoster(ROSTER, { ...ALL, locationId: 'meydan' }).map((e) => e.id)).toEqual(['e'])
  })

  it('pages like the server does and counts the whole match', () => {
    const p2 = rosterPage(ROSTER, { ...ALL, page: 2, pageSize: 2 })
    expect(p2.total).toBe(5)
    expect(p2.items.map((r) => r.id)).toEqual(['x', 'm'])
  })

  it('carries the server’s answer about who may be changed', () => {
    const rows = rosterPage(ROSTER, { ...ALL, page: 1, pageSize: 25 }).items
    expect(rows.find((r) => r.id === 'x')!.manageable).toBe(false)
    expect(rows.find((r) => r.id === 'm')!.isColleague).toBe(true)
    expect(rows.find((r) => r.id === 'a')!.manageable).toBe(true)
  })

  it('never puts a salary or a role a manager was not sent into the row', () => {
    const row = toRosterRow(ROSTER[0]!)
    expect('monthlySalary' in row).toBe(false)
    expect(row.managedLocationIds).toEqual([])
  })

  it('counts active staff by branch, and leavers company-wide on the list', () => {
    expect(rosterStats(ROSTER, null)).toMatchObject({ total: 5, activated: 4, notStarted: 1, leftCount: 1 })
    expect(rosterStats(ROSTER, 'meydan')).toMatchObject({ total: 1, activated: 0, notStarted: 1 })
  })

  it('selects for a bulk action only the people the bulk action can reach', () => {
    // A colleague is editable one at a time; the server's bulk grant skips them, so the count must too.
    expect(rosterSelection(ROSTER, ALL).map((r) => r.id)).toEqual(['a', 'e', 'z'])
    expect(rosterSelection(ROSTER, ALL).find((r) => r.id === 'a')!.canShareDevice).toBe(true)
  })
})
