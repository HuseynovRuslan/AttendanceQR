import { describe, expect, it } from 'vitest'
import { activeRefusalText, withActive, type ManagerEmployee } from './manager'

const card: ManagerEmployee = {
  id: 'a', fullName: 'Aynur Həsənova', firstName: 'Aynur', lastName: 'Həsənova', fatherName: 'Əli', position: 'Bağban',
  paperEmployer: 'Sənəddəki şirkət', paperSite: 'Sənəddəki ərazi', phoneNumber: '501112233', email: '',
  locationId: 'b1', locationName: 'Park', birthDate: '1990-01-01', birthYear: 1990, workStart: '08:00', workEnd: '17:00',
  scheduleId: 's1', workCycleDays: null, workCycleOnDays: null, workCycleAnchor: null, photoExempt: true,
  canFieldCheckIn: true, qrlessCheckInOverride: false, requireGeofenceOverride: true, canShareDevice: true,
  isActive: true, activated: true, manageable: true, deactivatable: true,
}

describe('withActive — «Deaktiv et» re-sends the whole card (the manager update clears what it is not given)', () => {
  it('changes only «active»', () => {
    const body = withActive(card, false)
    expect(body.isActive).toBe(false)
    expect(body).toMatchObject({
      fullName: 'Aynur Həsənova', firstName: 'Aynur', lastName: 'Həsənova', fatherName: 'Əli', position: 'Bağban',
      phoneNumber: '501112233', locationId: 'b1', birthDate: '1990-01-01', birthYear: 1990,
      workStart: '08:00', workEnd: '17:00', scheduleId: 's1', photoExempt: true, canFieldCheckIn: true,
      qrlessCheckInOverride: false, requireGeofenceOverride: true,
      paperEmployer: 'Sənəddəki şirkət', paperSite: 'Sənəddəki ərazi',
    })
    // An empty e-mail goes as none, never as "" (the server would try to store it).
    expect(body.email).toBeNull()
    expect(withActive(card, true).isActive).toBe(true)
  })

  it('carries nothing a manager may not set', () => {
    const body = withActive(card, false) as unknown as Record<string, unknown>
    for (const never of ['monthlySalary', 'role', 'managedLocationIds', 'canShareDevice', 'id', 'manageable', 'deactivatable'])
      expect(never in body).toBe(false)
  })
})

describe('activeRefusalText — a refusal names its rule', () => {
  it('says why for each refusal the update can give', () => {
    expect(activeRefusalText('DeactivateNotAllowed')).toContain('hüququnuz yoxdur')
    expect(activeRefusalText('CannotDeactivateManager')).toContain('yalnız admin')
    expect(activeRefusalText('ManagerCannotManageRole')).toContain('Admini')
    expect(activeRefusalText('EmployeeNotFound')).toContain('filiallarınızda deyil')
  })

  it('leaves an unknown code to the caller', () => {
    expect(activeRefusalText('Something')).toBeUndefined()
    expect(activeRefusalText(undefined)).toBeUndefined()
  })
})
