// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { DeviceChangesPage } from './DeviceChangesPage'
import { revokeSharedDevice } from '../../api/admin'

/**
 * The device screens no longer receive device fingerprints — the value the scan and the forgot-PIN reset
 * trust as "this person's phone". They get a short display id instead, and «Telefon itib — hamısını ayır»
 * names the handset by one of its bindings. These pin that the screen still works on what it now gets.
 */

vi.mock('../../auth/AccessContext', () => ({ useHolds: () => true, useScopedRole: () => 'Admin' }))
vi.mock('../../api/admin', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../api/admin')>()),
  getPendingDeviceChanges: vi.fn(async () => ({
    status: 200,
    data: [{
      requestId: 'r1', employeeId: 'e1', employeeName: 'Aygün Məmmədova',
      currentDeviceId: '3fa2c1d0', newDeviceId: '9b7e4a12', requestedAtUtc: '2026-11-20T06:00:00Z',
      recentNewDevices: 1, autoBindLimitReached: false,
    }],
  })),
  getSharedDevices: vi.fn(async () => ({
    status: 200,
    data: [{
      deviceId: 'c0ffee12', label: 'Nokia G21', accountCount: 2, lastSeenAtUtc: null,
      employees: [
        { bindingId: 'b-1', employeeId: 'e2', fullName: 'Rəşad', position: null, canShareDevice: true, boundAtUtc: '2026-11-01T06:00:00Z' },
        { bindingId: 'b-2', employeeId: 'e3', fullName: 'Elvin', position: null, canShareDevice: true, boundAtUtc: '2026-11-01T06:00:00Z' },
      ],
    }],
  })),
  getDeviceBindings: vi.fn(async () => ({ status: 200, data: [] })),
  revokeSharedDevice: vi.fn(async () => ({ status: 200, data: { revoked: 2 } })),
}))

afterEach(cleanup)

async function renderPage() {
  await act(async () => {
    render(
      <MemoryRouter>
        <DeviceChangesPage />
      </MemoryRouter>,
    )
  })
}

describe('device screens without fingerprints', () => {
  it('shows the old and new phone of a request by display id', async () => {
    await renderPage()
    expect(await screen.findByText('3fa2c1d0')).toBeTruthy()
    expect(screen.getByText('9b7e4a12')).toBeTruthy()
  })

  it('names a lost shared phone by one of its bindings when revoking it', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true)
    await renderPage()
    await act(async () => {
      fireEvent.click(await screen.findByRole('button', { name: 'Telefon itib — hamısını ayır' }))
    })
    expect(vi.mocked(revokeSharedDevice)).toHaveBeenCalledWith('b-1')
  })
})
