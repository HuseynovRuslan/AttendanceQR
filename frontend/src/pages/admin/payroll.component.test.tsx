// @vitest-environment jsdom
import { cleanup, render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { PayrollPage } from './PayrollPage'

/**
 * «Maaş»: unpaid leave is counted in calendar days on every report, weekends included, but only its working
 * days are deducted. Where the two differ the cell says how many were deducted — «Ödənişsiz 14» beside a
 * deduction for ten days would otherwise read as a sum that does not add up.
 */

const net = vi.hoisted(() => ({ payroll: null as unknown }))

vi.mock('../../auth/AuthContext', () => ({
  useAuth: () => ({
    token: 't', isAuthenticated: true, role: 'Admin', employeeId: 'me', email: 'sinaq@test.local',
    mustChangePin: false, saveToken: () => {}, logout: () => {}, profiles: [], switchProfile: () => {},
  }),
}))
vi.mock('../../api/client', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../api/client')>()),
  apiRequest: vi.fn(async (path: string) =>
    path.startsWith('/api/reports/payroll')
      ? { status: 200, data: net.payroll }
      : path.startsWith('/api/reports/my-locations')
        ? { status: 200, data: [] }
        : { status: 404, data: null }),
}))

afterEach(() => cleanup())

const line = (over: Record<string, unknown> = {}) => ({
  employeeId: 'e1', employeeName: 'Ödənişsiz İşçi', locationName: 'Ofis', monthlySalary: 630,
  scheduledDays: 21, workDays: 11, absentDays: 0, leaveDays: 14, vacationDays: 0, sickDays: 0,
  unpaidDays: 14, restDays: 0, tripDays: 0, permissionDays: 0, overtimeHours: 0, earlyLeaveHours: 0,
  earlyArriveHours: 0, perDay: 30, deduction: 300, payable: 330, selfCorrectedDays: 0, deductedUnpaidDays: 10,
  ...over,
})

function renderPage(rows: unknown[]) {
  net.payroll = {
    from: '2026-11-01', to: '2026-11-30', scopeLabel: 'Hamısı', rows,
    totalMonthlySalary: 630, totalDeduction: 300, totalPayable: 330,
  }
  return render(<MemoryRouter><PayrollPage /></MemoryRouter>)
}

describe('«Maaş» — unpaid leave', () => {
  it('shows the working days deducted beside the calendar days when they differ', async () => {
    renderPage([line()])
    const row = (await screen.findByText('Ödənişsiz İşçi')).closest('tr')!
    const cell = row.querySelector('td[data-label="Ödənişsiz"]') as HTMLElement
    expect(cell.textContent).toContain('14')
    expect(within(cell).getByText(/10 iş günü/)).toBeTruthy()
  })

  it('says nothing more when every unpaid day was a working day', async () => {
    renderPage([line({ unpaidDays: 3, deductedUnpaidDays: 3, leaveDays: 3, deduction: 90, payable: 540 })])
    const row = (await screen.findByText('Ödənişsiz İşçi')).closest('tr')!
    const cell = row.querySelector('td[data-label="Ödənişsiz"]') as HTMLElement
    expect(cell.textContent).toBe('3')
  })

  it('explains the deduction as the working days of the unpaid leave', async () => {
    renderPage([line()])
    expect(await screen.findByText(/ödənişsiz məzuniyyətin iş\s+günləri/)).toBeTruthy()
  })
})
