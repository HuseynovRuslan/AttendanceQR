// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { PlanApply, PlanDay, PlanGrid } from '../../../api/shiftPlan'

/**
 * The plan screen against a stand-in API: what a branch's person in charge does with it — change one day and take
 * it back, give a day off, reach back into a worked day only after saying so, check a bulk change before it is
 * written, and read who changed what.
 */

const api = vi.hoisted(() => ({
  getPlanLocations: vi.fn(),
  getShiftPlan: vi.fn(),
  applyShiftPlan: vi.fn(),
  getPlanChanges: vi.fn(),
  undoPlanChange: vi.fn(),
}))
vi.mock('../../../api/shiftPlan', () => api)

import { ShiftPlanner } from './ShiftPlanner'

const DAY = 'd1'
const NIGHT = 'n1'
const SAVED = { status: 200, data: { ok: true, set: 1, cleared: 0, unchanged: 0, people: 1, rebuiltDays: 0, rebuildPending: 0, changeId: 'c1' } }
const PREVIEW = {
  status: 200,
  data: {
    preview: true, standing: false, people: 2, days: 7, newDays: 10, replacedDays: 4, clearedDays: 0, unchangedDays: 0, pastDays: 0,
    changes: [{ from: 'Səhər 06:00–15:00', to: 'Gecə 20:00–06:00', count: 4 }, { from: 'Öz növbəsi: Günorta 12:00–21:00', to: 'Gecə 20:00–06:00', count: 10 }],
  },
}

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
  api.applyShiftPlan.mockImplementation(async (body: PlanApply) => (body.preview ? PREVIEW : SAVED))
  api.undoPlanChange.mockResolvedValue({ status: 200, data: { ok: true, reverted: 1, kept: 0, rebuiltDays: 0, rebuildPending: 0, changeId: 'u1' } })
  api.getPlanChanges.mockResolvedValue({
    status: 200,
    data: [{
      id: 'c9', at: '2026-11-12 14:32', by: 'Ruslan Hüseynov', summary: '1 işçi, 14.11–14.11.2026: Gecə', people: 1, days: 1,
      isUndo: false, undoneAt: null, undoneBy: null, undoable: true, more: 0,
      items: [{ employee: 'Aysel Məmmədova', date: '2026-11-14', standing: false, before: 'Səhər 06:00–15:00', after: 'Gecə 20:00–06:00' }],
    }],
  })
})
afterEach(cleanup)

async function openWeek() {
  render(<ShiftPlanner />)
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

  it('gives one day off from the cell, saves only that day, and can take it back', async () => {
    await openWeek()
    const g = grid(api.getShiftPlan.mock.calls[0][1])
    const target = g.employees[1].days[5].date
    const cell = screen.getAllByRole('button', { name: /Elçin Quliyev/ }).find((b) => b.getAttribute('aria-label')!.includes(Number(target.slice(8)).toString()))!
    fireEvent.click(cell)
    fireEvent.click(await screen.findByRole('menuitemradio', { name: /İstirahət/ }))

    await waitFor(() => expect(api.applyShiftPlan).toHaveBeenCalledWith({ employeeIds: ['e2'], steps: ['rest'], dates: [target], locationId: 'loc' }))
    expect(await screen.findByText(/Plan yadda saxlandı/)).toBeTruthy()

    fireEvent.click(screen.getByRole('button', { name: 'Geri al' }))
    await waitFor(() => expect(api.undoPlanChange).toHaveBeenCalledWith('c1', false))
    expect(await screen.findByText(/Geri alındı/)).toBeTruthy()
  })

  it('asks before changing a day already worked, and sends the confirmation only after yes', async () => {
    api.applyShiftPlan
      .mockResolvedValueOnce({ status: 409, data: { error: 'PastDatesNeedConfirm', pastDays: 1 } })
      .mockResolvedValueOnce({ ...SAVED, data: { ...SAVED.data, rebuiltDays: 1 } })
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
  it('shows what «2 days, 2 nights, 2 off» will change before writing it', async () => {
    await openWeek()
    fireEvent.click(screen.getByRole('checkbox', { name: 'Hamısını seç' }))
    fireEvent.click(screen.getByRole('button', { name: 'Seçilənlərə plan yaz' }))

    const drawer = await screen.findByRole('dialog', { name: 'Yeni plan' })
    fireEvent.click(within(drawer).getByRole('radio', { name: /Səhər/ }))
    fireEvent.click(within(drawer).getByRole('button', { name: 'Növbəli' }))
    fireEvent.change(within(drawer).getByLabelText('Qayda'), { target: { value: '2-2-2' } })
    fireEvent.change(within(drawer).getByLabelText('İkinci növbə'), { target: { value: NIGHT } })
    await act(async () => { fireEvent.click(within(drawer).getByRole('button', { name: 'Davam et' })) })

    // Nothing written yet: only asked.
    expect(api.applyShiftPlan).toHaveBeenCalledTimes(1)
    expect(api.applyShiftPlan.mock.calls[0][0]).toMatchObject({ preview: true, steps: [DAY, DAY, NIGHT, NIGHT, 'rest', 'rest'] })
    const check = await screen.findByRole('dialog', { name: 'Planı yoxlayın' })
    expect(within(check).getByText('Mövcud planı əvəz edəcək')).toBeTruthy()
    expect(within(check).getByText('4 gün', { selector: '.sp-movecount' })).toBeTruthy()
    expect(within(check).getByText(/^Kim:/).textContent).toMatch(/Aysel Məmmədova/)       // the people by name, not «2 işçi»
    expect(within(check).queryByText('Onsuz da belədir')).toBeNull()                       // a count of zero is not shown
    expect(within(check).getAllByText('öz növbəsi', { selector: '.sp-labelnote' })).toHaveLength(1) // the own shift is named

    await act(async () => { fireEvent.click(within(check).getByRole('button', { name: 'Təsdiqlə və yadda saxla' })) })
    await waitFor(() => expect(api.applyShiftPlan).toHaveBeenLastCalledWith(expect.objectContaining({
      employeeIds: expect.arrayContaining(['e1', 'e2']),
      steps: [DAY, DAY, NIGHT, NIGHT, 'rest', 'rest'],
    })))
    expect(api.applyShiftPlan.mock.calls[1][0].preview).toBeUndefined()
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Planı yoxlayın' })).toBeNull())
  })

  it('writes a shift with no end date as a standing shift, past any 92-day limit', async () => {
    await openWeek()
    fireEvent.click(screen.getByRole('button', { name: 'Yeni plan' }))
    const drawer = await screen.findByRole('dialog', { name: 'Yeni plan' })
    fireEvent.click(within(drawer).getByRole('checkbox', { name: /Elçin Quliyev/ }))
    fireEvent.click(within(drawer).getByRole('radio', { name: /Gecə/ }))
    fireEvent.click(within(drawer).getByRole('checkbox', { name: /Bitmə tarixi yoxdur/ }))
    await act(async () => { fireEvent.click(within(drawer).getByRole('button', { name: 'Davam et' })) })

    expect(api.applyShiftPlan.mock.calls[0][0]).toMatchObject({ employeeIds: ['e2'], steps: [NIGHT], standing: true, preview: true })
    expect(api.applyShiftPlan.mock.calls[0][0].to).toBeUndefined()
  })
})

describe('the day', () => {
  it('sets one person’s shift straight from the list', async () => {
    await openWeek()
    fireEvent.click(screen.getByRole('button', { name: 'Gün' }))
    const select = await screen.findByRole('combobox', { name: 'Elçin Quliyev: növbə' })
    fireEvent.change(select, { target: { value: NIGHT } })

    await waitFor(() => expect(api.applyShiftPlan).toHaveBeenCalledWith(expect.objectContaining({ employeeIds: ['e2'], steps: [NIGHT] })))
    expect(api.applyShiftPlan.mock.calls[0][0].dates).toHaveLength(1)
    expect(api.applyShiftPlan.mock.calls[0][0].preview).toBeUndefined()
  })

  it('checks a change for several people before writing it', async () => {
    await openWeek()
    fireEvent.click(screen.getByRole('button', { name: 'Gün' }))
    fireEvent.click(await screen.findByRole('checkbox', { name: 'Hamısını seç' }))
    fireEvent.change(screen.getByRole('combobox', { name: 'Növbə seçin' }), { target: { value: NIGHT } })
    fireEvent.click(screen.getByRole('button', { name: 'Tətbiq et' }))

    const dialog = await screen.findByRole('alertdialog', { name: /2 işçi üçün dəyişiklik/ })
    expect(api.applyShiftPlan.mock.calls[0][0]).toMatchObject({ preview: true })
    await act(async () => { fireEvent.click(within(dialog).getByRole('button', { name: 'Təsdiqlə və yadda saxla' })) })
    expect(api.applyShiftPlan.mock.calls[1][0].preview).toBeUndefined()
  })
})

describe('the history', () => {
  it('says who changed which day from what to what, and can undo it', async () => {
    await openWeek()
    fireEvent.click(screen.getByRole('button', { name: 'Tarixçə' }))
    const panel = await screen.findByRole('dialog', { name: 'Dəyişikliklərin tarixçəsi' })
    expect(await within(panel).findByText('Ruslan Hüseynov')).toBeTruthy()
    fireEvent.click(within(panel).getByRole('button', { name: /Günlərə bax/ }))
    // The hours are kept on one line (their own span), so the cell is matched as a whole.
    expect(within(panel).getByRole('cell', { name: 'Səhər 06:00–15:00' })).toBeTruthy()
    expect(within(panel).getByRole('cell', { name: 'Gecə 20:00–06:00' })).toBeTruthy()

    fireEvent.click(within(panel).getByRole('button', { name: 'Geri al' }))
    await waitFor(() => expect(api.undoPlanChange).toHaveBeenCalledWith('c9', false))
  })
})
