// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { NotificationCenterPage } from './NotificationCenterPage'
import type { LiveListener } from '../../lib/notificationHub'

/**
 * «Bildirişlər»: newest first by company day, unread marked, read-all, a click that reads and opens the person, the
 * server's pages by cursor, new lines arriving live at the top, and a refetch after the live channel reconnects.
 */

const net = vi.hoisted(() => ({
  pages: {} as Record<string, { items: unknown[]; nextCursor: string | null }>,
  calls: [] as string[],
  listener: null as LiveListener | null,
}))

vi.mock('../../api/client', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../api/client')>()),
  apiRequest: vi.fn(async (path: string, options?: { method?: string }) => {
    net.calls.push(`${options?.method ?? 'GET'} ${path}`)
    if (path.startsWith('/api/notifications/preferences')) return { status: 200, data: { branches: [] } }
    if (path.startsWith('/api/notifications/read-all')) return { status: 200, data: { marked: 2 } }
    if (path.match(/^\/api\/notifications\/[^/]+\/read$/)) return { status: 200, data: {} }
    if (path.startsWith('/api/notifications')) {
      const cursor = new URLSearchParams(path.split('?')[1] ?? '').get('cursor') ?? ''
      return { status: 200, data: net.pages[cursor] ?? { items: [], nextCursor: null } }
    }
    return { status: 404, data: null }
  }),
}))
vi.mock('../../lib/notificationHub', () => ({
  subscribeLive: (l: LiveListener) => {
    net.listener = l
    return () => { net.listener = null }
  },
}))

const line = (id: string, over: Record<string, unknown> = {}) => ({
  id, typeKey: 'attendance.check_in', title: 'İşçi giriş etdi', body: `${id} işə giriş etdi.`, subjectEmployeeId: 'e1',
  subjectName: id, locationId: 'b1', branchName: 'Ramana', occurredAtUtc: '2026-11-24T05:02:00Z', wasOffline: false,
  minutes: null, url: '/admin/employees/e1', createdAtUtc: new Date().toISOString(), readAtUtc: null, ...over,
})

beforeEach(() => {
  net.pages = {}
  net.calls = []
  net.listener = null
})
afterEach(() => cleanup())

function page() {
  render(
    <MemoryRouter initialEntries={['/admin/notifications']}>
      <Routes>
        <Route path="/admin/notifications" element={<NotificationCenterPage />} />
        <Route path="/admin/employees/:id" element={<div>İşçi profili</div>} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('«Bildirişlər» center', () => {
  it('lists today under «Bu gün», tells read from unread, and reads all', async () => {
    net.pages[''] = {
      items: [line('Əliyev Elvin'), line('Quliyeva Aynur', { readAtUtc: new Date().toISOString(), typeKey: 'attendance.late', title: 'İşçi gecikdi' })],
      nextCursor: null,
    }
    page()
    const today = await screen.findByRole('region', { name: 'Bu gün' })
    const rows = within(today).getAllByRole('button')
    expect(rows[0].getAttribute('aria-label')).toContain('oxunmayıb')
    expect(rows[1].getAttribute('aria-label')).not.toContain('oxunmayıb')
    expect(rows[1].getAttribute('aria-label')).toContain('Gecikmə')

    fireEvent.click(screen.getByRole('button', { name: /Hamısını oxunmuş et/ }))
    await waitFor(() => expect(net.calls).toContain('POST /api/notifications/read-all'))
    await waitFor(() => expect(within(today).getAllByRole('button')[0].getAttribute('aria-label')).not.toContain('oxunmayıb'))
  })

  it('opens the person on a click and marks that line read', async () => {
    net.pages[''] = { items: [line('Əliyev Elvin', { id: 'n1' })], nextCursor: null }
    page()
    fireEvent.click(await screen.findByRole('button', { name: /Əliyev Elvin işə giriş etdi/ }))
    await screen.findByText('İşçi profili')
    expect(net.calls).toContain('POST /api/notifications/n1/read')
  })

  it('pages by the server cursor and never repeats a line', async () => {
    net.pages[''] = { items: [line('a'), line('b')], nextCursor: 'c1' }
    net.pages['c1'] = { items: [line('b'), line('c')], nextCursor: null }
    page()
    fireEvent.click(await screen.findByRole('button', { name: 'Daha çox göstər' }))
    await screen.findByRole('button', { name: /c işə giriş etdi/ })
    expect(screen.getAllByRole('button', { name: /işə giriş etdi/ })).toHaveLength(3)
    expect(screen.queryByRole('button', { name: 'Daha çox göstər' })).toBeNull()
  })

  it('puts a live line on top and fetches again after a reconnect', async () => {
    net.pages[''] = { items: [line('köhnə')], nextCursor: null }
    page()
    await screen.findByRole('button', { name: /köhnə işə giriş etdi/ })

    act(() => {
      net.listener?.onItems?.([{ id: 'live1', typeKey: 'attendance.check_out', title: 'İşçi çıxış etdi', body: 'Yeni İşçi işdən çıxış etdi.',
        branchName: 'Ramana', occurredAtUtc: new Date().toISOString(), wasOffline: false, minutes: null, url: '/admin/employees/e2' }])
    })
    const first = within(await screen.findByRole('region', { name: 'Bu gün' })).getAllByRole('button')[0]
    expect(first.getAttribute('aria-label')).toContain('Yeni İşçi işdən çıxış etdi')

    const before = net.calls.filter((c) => c.startsWith('GET /api/notifications?')).length
    act(() => net.listener?.onReconnected?.())
    await waitFor(() => expect(net.calls.filter((c) => c.startsWith('GET /api/notifications?')).length).toBe(before + 1))
  })

  it('says what to do when there is nothing yet', async () => {
    page()
    await screen.findByText('Hələ bildiriş yoxdur')
    const links = screen.getAllByRole('link', { name: 'Bildiriş ayarları' })
    expect(links.length).toBeGreaterThan(0)
    expect(links.every((a) => a.getAttribute('href') === '/menu/notifications')).toBe(true)
  })
})
