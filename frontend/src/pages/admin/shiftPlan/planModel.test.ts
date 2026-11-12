import { describe, expect, it } from 'vitest'
import type { PlanDay } from '../../../api/shiftPlan'
import {
  addDays, bandOf, cellsOf, initials, maskOf, rangeLabel, rangeOf, rotationSteps, ROTATIONS, shortRestDays, step, weekday, WORKWEEK,
} from './planModel'

const day = (date: string, start: string | null, end: string | null, overnight = false, working = true): PlanDay => ({
  date, kind: 'plan', scheduleId: 's', shift: 'x', start, end, overnight, working,
  holiday: null, leave: null, checkIn: null, checkOut: null,
})

describe('dates', () => {
  it('walks over month and year ends without a time zone in the way', () => {
    expect(addDays('2026-12-31', 1)).toBe('2027-01-01')
    expect(addDays('2026-03-01', -1)).toBe('2026-02-28')
    expect(weekday('2026-11-16')).toBe(1) // a Monday
  })

  it('gives each view its days', () => {
    expect(rangeOf('day', '2026-11-18')).toEqual({ from: '2026-11-18', to: '2026-11-18' })
    expect(rangeOf('week', '2026-11-18')).toEqual({ from: '2026-11-16', to: '2026-11-22' })
    expect(rangeOf('week', '2026-11-22')).toEqual({ from: '2026-11-16', to: '2026-11-22' }) // Sunday ends the week
    expect(rangeOf('month', '2026-02-10')).toEqual({ from: '2026-02-01', to: '2026-02-28' })
  })

  it('steps a view by its own length', () => {
    expect(step('day', '2026-11-18', 1)).toBe('2026-11-19')
    expect(step('week', '2026-11-18', -1)).toBe('2026-11-11')
    expect(step('month', '2026-12-15', 1)).toBe('2027-01-01')
  })

  it('names a range the way people say it', () => {
    expect(rangeLabel('week', '2026-11-16', '2026-11-22')).toBe('16–22 noyabr')
    expect(rangeLabel('week', '2026-10-26', '2026-11-01')).toBe('26 oktyabr – 1 noyabr')
    expect(rangeLabel('month', '2026-11-01', '2026-11-30')).toBe('Noyabr 2026')
    expect(rangeLabel('day', '2026-11-20', '2026-11-20')).toBe('Cümə, 20 noyabr')
  })
})

describe('shifts at a glance', () => {
  it('colours a shift by the part of the day it starts in', () => {
    expect(bandOf('06:00', false)).toBe('morning')
    expect(bandOf('10:00', false)).toBe('day')
    expect(bandOf('16:00', false)).toBe('evening')
    expect(bandOf('20:00', true)).toBe('night')
    expect(bandOf('16:00', true)).toBe('evening') // an evening shift that ends after midnight is still the evening
    expect(bandOf('22:00', true)).toBe('night')
  })

  it('shortens a name to two letters for the month view', () => {
    expect(initials('Gecə')).toBe('Ge')
    expect(initials('Gecə növbəsi')).toBe('GN')
  })
})

describe('rest between shifts', () => {
  it('flags a start that comes too soon after a night', () => {
    const days = [day('2026-11-16', '20:00', '06:00', true), day('2026-11-17', '08:00', '17:00')]
    expect([...shortRestDays(days)]).toEqual(['2026-11-17'])
  })

  it('leaves an ordinary week and a day off between alone', () => {
    const week = [day('2026-11-16', '09:00', '18:00'), day('2026-11-17', '09:00', '18:00')]
    expect(shortRestDays(week).size).toBe(0)
    const withRest = [day('2026-11-16', '20:00', '06:00', true), day('2026-11-17', null, null, false, false), day('2026-11-18', '08:00', '17:00')]
    expect(shortRestDays(withRest).size).toBe(0)
  })
})

describe('rotations', () => {
  it('turns «2 day, 2 night, 2 off» into six steps', () => {
    const preset = ROTATIONS.find((r) => r.id === '2-2-2')!
    expect(rotationSteps(preset, 'D', 'N')).toEqual(['D', 'D', 'N', 'N', 'rest', 'rest'])
  })

  it('counts the person-days a plan writes', () => {
    expect(maskOf([1, 2, 3, 4, 5])).toBe(WORKWEEK)
    expect(cellsOf(30, '2026-11-16', '2026-11-22', null, false)).toBe(210)
    expect(cellsOf(30, '2026-11-16', '2026-11-22', WORKWEEK, false)).toBe(150) // weekend left as it is
    expect(cellsOf(30, '2026-11-16', '2026-11-22', WORKWEEK, true)).toBe(210) // weekend written as rest
  })
})
