// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { NotificationSettingsPage } from './NotificationSettingsPage'

/**
 * «Bildiriş ayarları»: every type starts off; a plain employee sees only this device; a manager picks from their own
 * branches (search, all, none, a count, grouped by structure); the save sends exactly what is on the screen and
 * says what went wrong in words.
 */

const net = vi.hoisted(() => ({
  prefs: null as unknown,
  prefsStatus: 200,
  putStatus: 200,
  putBody: null as unknown,
  putAnswer: null as unknown,
}))

vi.mock('../api/client', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../api/client')>()),
  apiRequest: vi.fn(async (path: string, options?: { method?: string; body?: unknown }) => {
    if (path.startsWith('/api/notifications/preferences') && (options?.method ?? 'GET') === 'GET')
      return { status: net.prefsStatus, data: net.prefsStatus === 200 ? net.prefs : null }
    if (path === '/api/notifications/preferences' && options?.method === 'PUT') {
      net.putBody = options.body
      return { status: net.putStatus, data: net.putAnswer }
    }
    return { status: 404, data: null }
  }),
}))
vi.mock('../lib/push', () => ({
  pushSupported: () => true,
  pushPermission: () => 'default',
  isSubscribed: async () => false,
  enablePush: async () => 'ok',
  disablePush: async () => true,
  sendTestPush: async () => 1,
}))
vi.mock('../lib/host', () => ({ isNativeApp: () => false }))

const TYPES = [
  { key: 'attendance.check_in', label: 'İşə giriş', description: 'İşçi giriş etdikdə bildiriş alın.', enabled: false },
  { key: 'attendance.check_out', label: 'İşdən çıxış', description: 'İşçi çıxış etdikdə bildiriş alın.', enabled: false },
  { key: 'attendance.late', label: 'Gecikmə', description: 'İşçi işə gecikdikdə bildiriş alın.', enabled: false },
]

const BRANCHES = [
  { id: 'b1', name: 'Ramana', structureId: 's1', structureName: 'Şimal' },
  { id: 'b2', name: 'Maştağa', structureId: 's1', structureName: 'Şimal' },
  { id: 'b3', name: 'Nəsimi', structureId: null, structureName: null },
]

function prefs(over: Record<string, unknown> = {}) {
  return {
    category: 'attendance', eligible: true, companyWide: false, types: TYPES, branchMode: 'all', locationIds: [],
    channels: { inApp: true, push: true, web: true }, branches: BRANCHES, updatedAtUtc: null, ...over,
  }
}

beforeEach(() => {
  net.prefs = prefs()
  net.prefsStatus = 200
  net.putStatus = 200
  net.putBody = null
  net.putAnswer = null
})
afterEach(() => cleanup())

const page = () => render(<MemoryRouter><NotificationSettingsPage /></MemoryRouter>)

describe('«Bildiriş ayarları»', () => {
  it('starts with every attendance type off', async () => {
    page()
    for (const t of TYPES) {
      const sw = await screen.findByRole('switch', { name: t.label })
      expect(sw.getAttribute('aria-checked')).toBe('false')
    }
  })

  it('shows a plain employee only this device, never the attendance settings', async () => {
    net.prefs = prefs({ eligible: false, branches: [] })
    page()
    await screen.findByText(/filiala rəhbərlik edənlər üçündür/)
    expect(screen.queryByRole('switch', { name: 'İşə giriş' })).toBeNull()
    expect(screen.getByText('Bu cihaz')).toBeTruthy()
  })

  it('saves exactly what is switched on, and says it was saved', async () => {
    page()
    fireEvent.click(await screen.findByRole('switch', { name: 'İşə giriş' }))
    fireEvent.click(screen.getByRole('switch', { name: 'Gecikmə' }))
    fireEvent.click(screen.getByRole('switch', { name: 'Mobil push bildirişləri' }))
    net.putAnswer = prefs({
      types: TYPES.map((t) => ({ ...t, enabled: t.key !== 'attendance.check_out' })),
      channels: { inApp: true, push: false, web: true },
    })

    fireEvent.click(screen.getByRole('button', { name: 'Yadda saxla' }))

    await screen.findByText('Yadda saxlanıldı')
    expect(net.putBody).toEqual({
      category: 'attendance',
      enabledTypes: ['attendance.check_in', 'attendance.late'],
      branchMode: 'all',
      locationIds: [],
      inApp: true,
      push: false,
      web: true,
    })
  })

  it('picks branches with search, all, none and a count, grouped by structure', async () => {
    page()
    fireEvent.click(await screen.findByRole('switch', { name: 'İşə giriş' }))
    fireEvent.click(screen.getByRole('radio', { name: /Yalnız seçilmiş filiallar/ }))

    expect(screen.getByRole('group', { name: 'Şimal' })).toBeTruthy()
    expect(screen.getByRole('group', { name: 'Struktursuz filiallar' })).toBeTruthy()

    fireEvent.click(screen.getByRole('button', { name: 'Hamısını seç' }))
    expect(screen.getByText(/seçilib/).textContent).toContain('3 / 3')
    fireEvent.click(screen.getByRole('button', { name: 'Təmizlə' }))
    expect(screen.getByText(/seçilib/).textContent).toContain('0 / 3')

    fireEvent.change(screen.getByRole('searchbox', { name: 'Filial axtar' }), { target: { value: 'maş' } })
    const found = screen.getAllByRole('checkbox')
    expect(found.map((c) => c.textContent)).toEqual(['Maştağa'])
    fireEvent.click(found[0])
    expect(found[0].getAttribute('aria-checked')).toBe('true')

    fireEvent.click(screen.getByRole('button', { name: 'Yadda saxla' }))
    await waitFor(() => expect(net.putBody).toMatchObject({ branchMode: 'selected', locationIds: ['b2'] }))
  })

  it('says in words what the server refused', async () => {
    net.putStatus = 400
    net.putAnswer = { error: 'BranchOutOfScope' }
    page()
    fireEvent.click(await screen.findByRole('switch', { name: 'İşə giriş' }))
    fireEvent.click(screen.getByRole('button', { name: 'Yadda saxla' }))

    const alert = await screen.findByRole('alert')
    expect(alert.textContent).toContain('artıq sizin səlahiyyətinizdə deyil')
  })

  it('offers a retry when the settings did not load', async () => {
    net.prefsStatus = 500
    page()
    const alert = await screen.findByRole('alert')
    expect(within(alert).getByText('Ayarlar yüklənmədi')).toBeTruthy()

    net.prefsStatus = 200
    fireEvent.click(within(alert).getByRole('button', { name: 'Yenidən yüklə' }))
    await screen.findByRole('switch', { name: 'İşə giriş' })
  })
})
