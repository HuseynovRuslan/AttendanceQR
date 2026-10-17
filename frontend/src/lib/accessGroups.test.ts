import { describe, expect, it } from 'vitest'
import { groupPermissions } from './accessGroups'

describe('groupPermissions', () => {
  it('puts every key in exactly one group, in a fixed order, with the rights about rights last', () => {
    const keys = [
      'roles.manage', 'attendance.view', 'employees.update', 'dashboard.view', 'holidays.manage',
      'assignments.manage', 'devices.approve', 'field_visits.view', 'payroll.view',
    ]
    const groups = groupPermissions(keys)

    expect(groups.flatMap((g) => g.keys).sort()).toEqual([...keys].sort())
    expect(groups.map((g) => g.title)).toEqual([
      'Panel və davamiyyət', 'İşçilər', 'Telefonlar', 'Səyyar iş və giriş qaydaları', 'Maaş',
      'Şirkət ayarları', 'Struktur və səlahiyyətlər',
    ])
    expect(groups[0].keys).toEqual(['attendance.view', 'dashboard.view'])
  })

  it('never drops a key it does not know — it goes to «Digər»', () => {
    const groups = groupPermissions(['attendance.view', 'future.thing'])
    expect(groups[groups.length - 1]).toEqual({ title: 'Digər', keys: ['future.thing'] })
  })
})
