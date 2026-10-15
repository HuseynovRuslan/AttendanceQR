import { describe, expect, it } from 'vitest'
import type { ManagerEmployee } from '../api/manager'
import { managerFormFrom, splitName } from './managerEmployeeForm'

// The manager's roster saves a spread of this form, and the server clears every field it omits.
// Until 15.10.2026 the check-in overrides were not read back, so each save wiped them.

const row = (over: Partial<ManagerEmployee> = {}): ManagerEmployee => ({
  id: 'e1', fullName: 'Əliyev Vüqar', firstName: null, lastName: null, fatherName: 'Rəşid', position: 'Bağban',
  paperEmployer: 'Abadlıq MMC', paperSite: 'Mərkəzi park', phoneNumber: '501112233', email: null,
  locationId: 'l1', locationName: 'Park', birthDate: '1980-05-01', birthYear: 1980,
  workStart: null, workEnd: null, scheduleId: 's1', workCycleDays: null, workCycleOnDays: null, workCycleAnchor: null,
  photoExempt: false, canFieldCheckIn: true, canShareDevice: false, isActive: true, activated: true,
  ...over,
})

describe('managerFormFrom', () => {
  it.each([
    [true, false],
    [false, true],
    [null, null],
  ])('keeps the check-in overrides as they are (qrless %s, fence %s)', (qrless, fence) => {
    const form = managerFormFrom(row({ qrlessCheckInOverride: qrless, requireGeofenceOverride: fence }))

    expect(form.qrlessCheckInOverride).toBe(qrless)
    expect(form.requireGeofenceOverride).toBe(fence)
  })

  it('reads a missing override as «follow the branch», never as undefined', () => {
    const form = managerFormFrom(row())

    expect(form.qrlessCheckInOverride).toBeNull()
    expect(form.requireGeofenceOverride).toBeNull()
  })

  it('leaves nothing undefined — an undefined key is dropped from the JSON and cleared on the server', () => {
    const form = managerFormFrom(row({ qrlessCheckInOverride: true, requireGeofenceOverride: false }))

    const missing = Object.entries(form).filter(([, v]) => v === undefined).map(([k]) => k)
    expect(missing).toEqual([])
    for (const key of ['paperEmployer', 'paperSite', 'canFieldCheckIn', 'photoExempt', 'scheduleId', 'isActive'])
      expect(form).toHaveProperty(key)
  })

  it('hides the legacy placeholder email and splits a name the row never split', () => {
    const form = managerFormFrom(row({ email: 'x@baki.local' }))

    expect(form.email).toBeNull()
    expect([form.firstName, form.lastName]).toEqual(['Əliyev', 'Vüqar'])
  })
})

describe('splitName', () => {
  it('prefers the stored parts', () => {
    expect(splitName('Vüqar', 'Əliyev', 'whatever')).toEqual({ first: 'Vüqar', last: 'Əliyev' })
  })

  it('takes the last token as the surname', () => {
    expect(splitName(null, null, 'Məmməd Rəşid Əliyev')).toEqual({ first: 'Məmməd Rəşid', last: 'Əliyev' })
  })
})
