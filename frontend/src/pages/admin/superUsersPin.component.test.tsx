// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { SuperUsers } from './TenantsPage'

/**
 * The operator console's «PIN sıfırla». The reset worked on the server, but the PIN never reached the screen: the list
 * refresh after the action cleared the one place it was held (2026-11-11 — the operator pressed it twice and got two
 * new PINs and no number). The PIN must be on the screen after the refresh, and gone on the next search.
 */

vi.mock('../operator/OperatorContext', () => ({ useCan: () => true }))
vi.mock('../../api/admin', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../api/admin')>()),
  searchSuperUsers: vi.fn(async () => ({
    status: 200,
    data: [{
      id: 'u1', tenantId: 't1', tenantSlug: 'cleanfix', tenantName: 'CleanFix', fullName: 'Sınaq Admin', phone: '501234567',
      email: null, role: 'Admin', isActive: true, mustChangePin: false, lastActiveAtUtc: null,
    }],
  })),
  resetSuperUserPin: vi.fn(async () => ({ status: 200, data: { id: 'u1', tempPin: '7316' } })),
}))

afterEach(cleanup)

describe('operator console: PIN reset', () => {
  it('shows the new PIN after the list refreshes, and forgets it on the next search', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true)
    render(<SuperUsers />)
    await act(async () => { fireEvent.change(screen.getByPlaceholderText(/Ad, telefon və ya email/), { target: { value: 'Sınaq' } }) })
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Axtar' })) })
    await act(async () => { fireEvent.click(await screen.findByRole('button', { name: 'PIN sıfırla' })) })

    expect(await screen.findByText('7316')).toBeTruthy()

    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Axtar' })) })
    expect(screen.queryByText('7316')).toBeNull()
  })
})
