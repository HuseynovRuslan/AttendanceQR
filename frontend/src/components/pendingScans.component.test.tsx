// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { PendingScansBanner } from './PendingScansBanner'

/**
 * The home screen's word on scans this phone is still holding. Balaxanı, 27.10.2026: with no signal the
 * screen looked the same before and after a scan, so people scanned again, and managers reset PINs and
 * removed phones that had nothing wrong with them. These pin what the employee is told, and that
 * «çatdı» is only ever said for a scan the server took.
 */

const state = vi.hoisted(() => ({
  queue: [] as { clientScanId: string; clientTimestampUtc: string }[],
  archive: [] as { clientScanId: string; outcome: string }[],
  // What the next sync does to the queue.
  onSync: null as null | (() => void),
  syncs: 0,
}))

vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({ employeeId: 'me' }),
}))
vi.mock('../lib/offlineQueue', () => ({
  QUEUE_CHANGED: 'qrlog:queue-changed',
  scansFor: vi.fn(async () => state.queue.map((q) => ({ ...q }))),
  archivedScans: vi.fn(async () => state.archive.map((a) => ({ ...a }))),
}))
vi.mock('../lib/offlineSync', () => ({
  syncOfflineScans: vi.fn(async () => {
    state.syncs++
    state.onSync?.()
    window.dispatchEvent(new Event('qrlog:queue-changed'))
  }),
}))

beforeEach(() => {
  state.queue = []
  state.archive = []
  state.onSync = null
  state.syncs = 0
})
afterEach(cleanup)

describe('PendingScansBanner', () => {
  it('says nothing when the phone holds nothing', async () => {
    const { container } = render(<PendingScansBanner />)
    await act(async () => {})
    expect(container.innerHTML).toBe('')
  })

  it('tells the employee the scan is on the phone, when it was taken, and not to scan again', async () => {
    state.queue = [{ clientScanId: 'a', clientTimestampUtc: '2026-10-27T03:51:00Z' }]
    render(<PendingScansBanner />)

    expect(await screen.findByText(/Skanınız telefonda saxlanılıb/)).toBeTruthy()
    expect(screen.getByText(/07:51/)).toBeTruthy()
    expect(screen.getByText(/Yenidən skan etməyin/)).toBeTruthy()
    expect(screen.getByRole('button', { name: 'İndi göndər' })).toBeTruthy()
  })

  it('«İndi göndər» runs the shared sync and, once the server took it, says it arrived', async () => {
    state.queue = [{ clientScanId: 'a', clientTimestampUtc: '2026-10-27T03:51:00Z' }]
    state.onSync = () => {
      state.queue = []
      state.archive = [{ clientScanId: 'a', outcome: 'sent' }]
    }
    render(<PendingScansBanner />)
    fireEvent.click(await screen.findByRole('button', { name: 'İndi göndər' }))

    await waitFor(() => expect(screen.getByText('✓ Skanınız serverə çatdı')).toBeTruthy())
    expect(state.syncs).toBe(1)
  })

  it('never says «çatdı» for a scan the server refused — that one is the reject banner’s to explain', async () => {
    state.queue = [{ clientScanId: 'a', clientTimestampUtc: '2026-10-27T03:51:00Z' }]
    state.onSync = () => {
      state.queue = []
      state.archive = [{ clientScanId: 'a', outcome: 'rejected' }]
    }
    const { container } = render(<PendingScansBanner />)
    fireEvent.click(await screen.findByRole('button', { name: 'İndi göndər' }))

    await waitFor(() => expect(container.innerHTML).toBe(''))
    expect(screen.queryByText(/serverə çatdı/)).toBeNull()
  })

  it('when it still cannot send, says why and keeps the scan', async () => {
    state.queue = [{ clientScanId: 'a', clientTimestampUtc: '2026-10-27T03:51:00Z' }]
    render(<PendingScansBanner />)
    fireEvent.click(await screen.findByRole('button', { name: 'İndi göndər' }))

    expect(await screen.findByText(/Hələ göndərilmədi|İnternet yoxdur/)).toBeTruthy()
    expect(screen.getByText(/Skanınız telefonda saxlanılıb/)).toBeTruthy()
  })
})
