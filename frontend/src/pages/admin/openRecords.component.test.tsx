// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { OpenRecordsPage } from './OpenRecordsPage'

/**
 * «Çıxışı unudulan günlər» and «Elastik qrafik»: a flexible session has no end of day, so the screen must not
 * suggest one. The box starts empty, «Bağla» waits for a real time, and the batch (which closes at the shift's end)
 * leaves the row out. A fixed-hours day is offered 18:00 exactly as before.
 */

const net = vi.hoisted(() => ({ rows: [] as unknown[] }))

vi.mock('../../auth/AuthContext', () => ({
  useAuth: () => ({
    token: 't', isAuthenticated: true, role: 'Admin', employeeId: 'me', email: 'sinaq@test.local',
    mustChangePin: false, saveToken: () => {}, logout: () => {}, profiles: [], switchProfile: () => {},
  }),
}))
vi.mock('../../api/client', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../api/client')>()),
  apiRequest: vi.fn(async (path: string) =>
    path === '/api/admin/attendance/open' ? { status: 200, data: net.rows } : { status: 404, data: null }),
}))

afterEach(() => cleanup())

const row = (over: Record<string, unknown>) => ({
  recordId: 'r', employeeId: 'e', employeeName: 'İşçi', locationName: 'Ofis',
  attendanceDate: '2026-11-14', checkInAtUtc: '2026-11-14T06:00:00Z', closable: true, ...over,
})

async function renderWith(rows: unknown[]) {
  net.rows = rows
  render(<MemoryRouter><OpenRecordsPage /></MemoryRouter>)
  await screen.findByText('Elastik İşçi')
}

const rowOf = (name: string) => screen.getByText(name).closest('tr')!

describe('«Çıxışı unudulan günlər» — elastik qrafik', () => {
  const rows = [
    row({ recordId: 'flex', employeeId: 'e1', employeeName: 'Elastik İşçi', flexible: true }),
    row({ recordId: 'fixed', employeeId: 'e2', employeeName: 'Sabit İşçi' }),
  ]

  it('offers no exit time for a flexible session, and «Bağla» waits for one', async () => {
    await renderWith(rows)
    const flex = rowOf('Elastik İşçi')
    const time = within(flex).getByLabelText('Elastik İşçi — çıxış vaxtı') as HTMLInputElement
    expect(time.value).toBe('')
    expect(within(flex).getByText('Sərbəst iş qrafiki — real çıxış saatını yazın')).toBeTruthy()
    expect((within(flex).getByRole('button', { name: /Bağla/ }) as HTMLButtonElement).disabled).toBe(true)

    fireEvent.change(time, { target: { value: '2026-11-14T15:30' } })
    expect((within(flex).getByRole('button', { name: /Bağla/ }) as HTMLButtonElement).disabled).toBe(false)
  })

  it('still offers 18:00 on a fixed-hours day', async () => {
    await renderWith(rows)
    const fixed = rowOf('Sabit İşçi')
    expect((within(fixed).getByLabelText('Sabit İşçi — çıxış vaxtı') as HTMLInputElement).value).toBe('2026-11-14T18:00')
    expect((within(fixed).getByRole('button', { name: /Bağla/ }) as HTMLButtonElement).disabled).toBe(false)
  })

  it('leaves the flexible session out of the batch, and says why', async () => {
    await renderWith(rows)
    expect((within(rowOf('Elastik İşçi')).getByLabelText('Elastik İşçi — seç') as HTMLInputElement).disabled).toBe(true)
    expect((within(rowOf('Sabit İşçi')).getByLabelText('Sabit İşçi — seç') as HTMLInputElement).disabled).toBe(false)
    expect(screen.getByRole('button', { name: 'Hamısını seç (1)' })).toBeTruthy()
    expect(screen.getByText(/1 sərbəst qrafikli sessiya toplu bağlanmır/)).toBeTruthy()
  })
})
