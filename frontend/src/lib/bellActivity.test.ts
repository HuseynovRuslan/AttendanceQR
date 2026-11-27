import { describe, expect, it } from 'vitest'
import { activityBesideFeed, feedCoversOffice, toastOf } from './bellActivity'

const prefs = (types: string[], branchMode: 'all' | 'selected' = 'all', inApp = true) => ({
  eligible: true,
  types: ['attendance.check_in', 'attendance.check_out', 'attendance.late'].map((key) => ({
    key, label: key, description: '', enabled: types.includes(key),
  })),
  branchMode,
  channels: { inApp, push: false, web: true },
})

const office = (message: string, at: string, isIn = true) => ({ message, at, isIn, linkTo: '/admin/today' })
const field = (message: string, at: string) => ({ message, at, isIn: true, linkTo: '/admin/field-visits' })

/** «Son hərəkət» next to the person's own notifications: never the same line twice, never a line lost. */
describe('the bell\'s «Son hərəkət» beside the notifications', () => {
  it('counts as covered only when both check-ins and check-outs reach the center for every branch', () => {
    expect(feedCoversOffice(prefs(['attendance.check_in', 'attendance.check_out']))).toBe(true)
    expect(feedCoversOffice(prefs(['attendance.check_in']))).toBe(false)
    expect(feedCoversOffice(prefs(['attendance.check_in', 'attendance.check_out'], 'selected'))).toBe(false)
    expect(feedCoversOffice(prefs(['attendance.check_in', 'attendance.check_out'], 'all', false))).toBe(false)
    expect(feedCoversOffice(null)).toBe(false)
  })

  it('keeps field visits always and drops the office lines when the notifications cover them', () => {
    const activity = [office('Əliyev Elvin — giriş etdi', '09:25'), field('Quliyev Ramin — sahəyə giriş etdi', '09:20')]
    expect(activityBesideFeed(activity, [], true)).toEqual([activity[1]])
  })

  it('with a partial setting drops only the lines the bell already shows above, and keeps the rest', () => {
    const activity = [
      office('Əliyev Elvin — giriş etdi', '09:25'),
      office('Əliyev Elvin — çıxış etdi', '18:02', false),
      office('Məmmədova Aysel — giriş etdi', '09:31'),
    ]
    const shown = [{ typeKey: 'attendance.check_in', subjectName: 'Əliyev Elvin', occurredAtUtc: '2026-11-24T05:25:00Z' }]
    expect(activityBesideFeed(activity, shown, false).map((a) => a.message)).toEqual([
      'Əliyev Elvin — çıxış etdi',
      'Məmmədova Aysel — giriş etdi',
    ])
    // A lateness line is not a check-in line, and a line without a name matches nothing.
    expect(activityBesideFeed(activity, [{ ...shown[0], typeKey: 'attendance.late' }], false)).toHaveLength(3)
    expect(activityBesideFeed(activity, [{ ...shown[0], subjectName: null }], false)).toHaveLength(3)
  })
})

describe('the live pop-up of a burst', () => {
  const live = (typeKey: string, eventId: string, body: string) => ({
    id: body, typeKey, title: typeKey, body, branchName: 'B1', occurredAtUtc: '2026-11-24T05:25:00Z',
    wasOffline: false, minutes: null, url: '/admin/employees/x', eventId,
  })

  it('shows a check-in and its lateness as the lateness, once', () => {
    const t = toastOf([live('attendance.check_in', 'e1', 'gəldi'), live('attendance.late', 'e1', 'gecikdi')])
    expect(t?.lead.body).toBe('gecikdi')
    expect(t?.others).toBe(0)
  })

  it('shows the newest scan and says how many others came with it', () => {
    const t = toastOf([live('attendance.check_in', 'e1', 'A gəldi'), live('attendance.check_in', 'e2', 'B gəldi'), live('attendance.late', 'e2', 'B gecikdi')])
    expect(t?.lead.body).toBe('B gecikdi')
    expect(t?.others).toBe(1)
    expect(toastOf([])).toBeNull()
  })
})
