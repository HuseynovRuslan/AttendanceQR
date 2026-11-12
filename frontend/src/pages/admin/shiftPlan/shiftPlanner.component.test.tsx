// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { PlanDay, PlanGrid } from '../../../api/shiftPlan'

/**
 * The plan screen against a stand-in API: what a branch's person in charge does with it — change one day, give a
 * day off, reach back into a worked day only after saying so, and write a rotation for several people at once.
 */

const api = vi.hoisted(() => ({
  getPlanLocations: vi.fn(),
  getShiftPlan: vi.fn(),
  applyShiftPlan: vi.fn(),
}))
vi.mock('../../../api/shiftPlan', () => api)
vi.mock('../../../api/admin', () => ({ bulkSchedule: vi.fn() }))

import { ShiftPlanner } from './ShiftPlanner'

const DAY = 'd1'
const NIGHT = 'n1'

function day(date: string, over: Partial<PlanDay> = {}): PlanDay {
  return {
    date, kind: 'base', scheduleId: DAY, shift: 'Səhər', start: '06:00', end: '15:00', overnight: false, working: true,
    holiday: null, leave: null, checkIn: null, checkOut: null, ...over,
  }
}

function grid(from: string): PlanGrid {
  const dates = Array.from({ length: 7 }, (_, i) => {
    const d = new Date(`${from}T00:00:00Z`)
    d.setUTCDate(d.getUTCDate() + i)
    return d.toISOString().slice(0, 10)
  })
  return {
    locationId: 'loc', from, to: dates[6], today: dates[2],
    schedules: [
      { id: DAY, name: 'Səhər', start: '06:00', end: '15:00', overnight: false, locationId: null },
      { id: NIGHT, name: 'Gecə', start: '20:00', end: '06:00', overnight: true, locationId: null },
    ],
    employees: [
      {
        id: 'e1', name: 'Aysel Məmmədova', position: 'Kassir', role: 'Employee', scheduleId: DAY,
        days: dates.map((d, i) => i === 3
          ? day(d, { kind: 'plan', scheduleId: NIGHT, shift: 'Gecə', start: '20:00', end: '06:00', overnight: true })
          : i === 4 ? day(d, { kind: 'rest', scheduleId: null, shift: 'İstirahət', start: null, end: null, working: false })
            : day(d, i === 0 ? { checkIn: '05:58', checkOut: '15:04' } : {})),
      },
      { id: 'e2', name: 'Elçin Quliyev', position: 'Anbardar', role: 'Employee', scheduleId: DAY, days: dates.map((d) => day(d)) },
    ],
  }
}

beforeEach(() => {
  vi.clearAllMocks()
  try { window.localStorage.clear() } catch { /* ignore */ }
  api.getPlanLocations.mockResolvedValue({ status: 200, data: [{ id: 'loc', name: 'Filial A' }] })
  api.getShiftPlan.mockImplementation(async (_loc: string, from: string) => ({ status: 200, data: grid(from) }))
  api.applyShiftPlan.mockResolvedValue({ status: 200, data: { ok: true, set: 1, cleared: 0, unchanged: 0, people: 1, rebuiltDays: 0 } })
})
afterEach(cleanup)

async function openWeek() {
  render(<ShiftPlanner mayStanding={false} />)
  await screen.findAllByText('Aysel Məmmədova')
}

describe('the week', () => {
  it('shows a planned night, a planned day off and a standing shift apart', async () => {
    await openWeek()
    const row = screen.getAllByText('Aysel Məmmədova')[0].closest('tr')!
    expect(within(row).getByText('Gecə').closest('.sp-chip')!.className).toContain('sp-planned')
    expect(within(row).getByText('İstirahət').closest('.sp-chip')!.className).toContain('sp-rest')
    expect(within(row).getAllByText('Səhər')[0].closest('.sp-chip')!.className).toContain('sp-own')
    expect(within(row).getByText('05:58–15:04')).toBeTruthy()
  })

  it('gives one day off from the cell and saves only that day', async () => {
    await openWeek()
    const g = grid(api.getShiftPlan.mock.calls[0][1])
    const target = g.employees[1].days[5].date
    const cell = screen.getAllByRole('button', { name: /Elçin Quliyev/ }).find((b) => b.getAttribute('aria-label')!.includes(Number(target.slice(8)).toString()))!
    fireEvent.click(cell)
    fireEvent.click(await screen.findByRole('menuitemradio', { name: /İstirahət/ }))

    await waitFor(() => expect(api.applyShiftPlan).toHaveBeenCalledWith({ employeeIds: ['e2'], steps: ['rest'], dates: [target] }))
    expect(await screen.findByText(/Plan yadda saxlandı/)).toBeTruthy()
  })

  it('asks before changing a day already worked, and sends the confirmation only after yes', async () => {
    api.applyShiftPlan
      .mockResolvedValueOnce({ status: 409, data: { error: 'PastDatesNeedConfirm', pastDays: 1 } })
      .mockResolvedValueOnce({ status: 200, data: { ok: true, set: 1, cleared: 0, unchanged: 0, people: 1, rebuiltDays: 1 } })
    await openWeek()
    fireEvent.click(screen.getAllByRole('button', { name: /Elçin Quliyev/ })[0])
    fireEvent.click(await screen.findByRole('menuitemradio', { name: /Gecə/ }))

    const dialog = await screen.findByRole('alertdialog')
    expect(within(dialog).getByText(/keçmiş günü əhatə edir/)).toBeTruthy()
    expect(api.applyShiftPlan).toHaveBeenCalledTimes(1)
    fireEvent.click(within(dialog).getByRole('button', { name: 'Bəli, dəyiş' }))

    await waitFor(() => expect(api.applyShiftPlan).toHaveBeenLastCalledWith(expect.objectContaining({ confirmPast: true, steps: [NIGHT] })))
    expect(await screen.findByText(/1 keçmiş gün yenidən hesablandı/)).toBeTruthy()
  })
})

describe('a plan for several people', () => {
  it('writes «2 days, 2 nights, 2 off» for the people ticked', async () => {
    await openWeek()
    fireEvent.click(screen.getByRole('checkbox', { name: 'Hamısını seç' }))
    fireEvent.click(screen.getByRole('button', { name: 'Seçilənlərə plan yaz' }))

    const drawer = await screen.findByRole('dialog', { name: 'Yeni plan' })
    fireEvent.click(within(drawer).getByRole('radio', { name: /Səhər/ }))
    fireEvent.click(within(drawer).getByRole('button', { name: 'Növbəli' }))
    fireEvent.change(within(drawer).getByLabelText('Qayda'), { target: { value: '2-2-2' } })
    fireEvent.change(within(drawer).getByLabelText('İkinci növbə'), { target: { value: NIGHT } })
    expect(within(drawer).getByText(/2 işçi × /)).toBeTruthy()

    await act(async () => { fireEvent.click(within(drawer).getByRole('button', { name: 'Planı yadda saxla' })) })

    await waitFor(() => expect(api.applyShiftPlan).toHaveBeenCalledWith(expect.objectContaining({
      employeeIds: expect.arrayContaining(['e1', 'e2']),
      steps: [DAY, DAY, NIGHT, NIGHT, 'rest', 'rest'],
    })))
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Yeni plan' })).toBeNull())
  })
})

describe('the day', () => {
  it('sets tomorrow’s shift per person straight from the list', async () => {
    await openWeek()
    fireEvent.click(screen.getByRole('button', { name: 'Gün' }))
    const select = await screen.findByRole('combobox', { name: 'Elçin Quliyev: növbə' })
    fireEvent.change(select, { target: { value: NIGHT } })

    await waitFor(() => expect(api.applyShiftPlan).toHaveBeenCalledWith(expect.objectContaining({ employeeIds: ['e2'], steps: [NIGHT] })))
    const sent = api.applyShiftPlan.mock.calls[0][0]
    expect(sent.dates).toHaveLength(1)
  })
})
