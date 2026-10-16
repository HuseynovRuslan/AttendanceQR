// @vitest-environment jsdom
import { cleanup, render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { correctionError } from '../../components/CorrectionReason'
import { SelfCorrectionsPage } from './SelfCorrectionsPage'

/**
 * «Öz düzəlişləri» (the single-admin rule of 2026-10-16): every correction somebody made to their own day, in
 * the open — and, where the company asks for a second approval, the buttons only for whoever may answer it.
 */

const net = vi.hoisted(() => ({ role: 'Admin' as 'Admin' | 'Employee', list: null as unknown }))

vi.mock('../../auth/AuthContext', () => ({
  useAuth: () => ({
    token: 't', isAuthenticated: true, role: net.role, employeeId: 'me', email: 'sinaq@test.local',
    mustChangePin: false, saveToken: () => {}, logout: () => {}, profiles: [], switchProfile: () => {},
  }),
}))
vi.mock('../../api/client', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../api/client')>()),
  apiRequest: vi.fn(async (path: string) =>
    path.startsWith('/api/admin/attendance/self-corrections') ? { status: 200, data: net.list } : { status: 404, data: null }),
}))

afterEach(() => cleanup())

const item = (over: Record<string, unknown> = {}) => ({
  id: 'c1', employeeId: 'a1', employeeName: 'Tək Admin', date: '2026-09-14', kind: 'Update',
  checkInBeforeUtc: '2026-09-14T05:00:00Z', checkOutBeforeUtc: '2026-09-14T14:00:00Z',
  checkInAfterUtc: '2026-09-14T05:00:00Z', checkOutAfterUtc: '2026-09-14T15:30:00Z',
  reason: 'Gec çıxdım, telefon oturmuşdu', atUtc: '2026-09-15T06:00:00Z',
  awaitsApproval: false, decision: null, mayDecide: false, ...over,
})

function renderPage() {
  return render(<MemoryRouter><SelfCorrectionsPage /></MemoryRouter>)
}

describe('«Öz düzəlişləri»', () => {
  it('shows a correction with its reason and the times before and after', async () => {
    net.role = 'Admin'
    net.list = { needsApproval: false, approverAvailable: false, items: [item()] }
    renderPage()
    const row = (await screen.findByText('Gec çıxdım, telefon oturmuşdu')).closest('tr')!
    expect(within(row).getByText('Vaxt düzəlişi')).toBeTruthy()
    expect(within(row).getByText('Tətbiq olunub')).toBeTruthy()
    expect(within(row).getByText(/→/)).toBeTruthy()
    expect(within(row).queryByText('Təsdiqlə')).toBeNull()
  })

  it('offers the approval only to whoever may give it', async () => {
    net.role = 'Employee'
    net.list = { needsApproval: true, approverAvailable: false, items: [item({ awaitsApproval: true, mayDecide: true })] }
    renderPage()
    const row = (await screen.findByText('Təsdiq gözləyir')).closest('tr')!
    expect(within(row).getByText('Təsdiqlə')).toBeTruthy()
    expect(within(row).getByText('Rədd et')).toBeTruthy()
    // Not the company's setting to change for an approver who is not the administrator.
    expect(screen.queryByText('Öz düzəlişi ikinci təsdiq istəsin')).toBeNull()
  })

  it('will not switch the second approval on while nobody could give it', async () => {
    net.role = 'Admin'
    net.list = { needsApproval: false, approverAvailable: false, items: [] }
    renderPage()
    const box = (await screen.findByText('Öz düzəlişi ikinci təsdiq istəsin')).closest('label')!.querySelector('input')!
    expect(box.disabled).toBe(true)
    expect(screen.getByText(/Hələ təsdiq verə biləcək şəxs yoxdur/)).toBeTruthy()
  })
})

describe('a refused correction of one’s own day says who can make it', () => {
  it('names them', () => {
    expect(correctionError({ error: 'OwnAttendance', correctors: ['Aynur Həsənova'] }, 'x'))
      .toBe('Öz gününüzü düzəldə bilməzsiniz — bu günü Aynur Həsənova düzəldə bilər.')
  })
  it('falls back to the plain refusal and to the screen’s own words', () => {
    expect(correctionError({ error: 'OwnAttendance' }, 'x')).toContain('Öz davamiyyətinizə düzəliş edə bilməzsiniz')
    expect(correctionError({ error: 'Something' }, 'Alınmadı')).toBe('Alınmadı')
  })
})
