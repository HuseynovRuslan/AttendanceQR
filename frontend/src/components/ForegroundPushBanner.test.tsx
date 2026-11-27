// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { BANNER_MS, ForegroundPushBanner } from './ForegroundPushBanner'
import { noteLiveItems, receiveForegroundPush, resetForegroundPush } from '../lib/foregroundPush'

/**
 * The banner is what Android would have drawn had the app been closed: the push's title and text, a tap that opens
 * what it points to, gone by itself — and nothing at all for a push the live pop-up already showed.
 */

function mount() {
  return render(
    <MemoryRouter initialEntries={['/home']}>
      <ForegroundPushBanner />
      <Routes>
        <Route path="/home" element={<p>home screen</p>} />
        <Route path="/admin/employees/:id" element={<p>employee card</p>} />
      </Routes>
    </MemoryRouter>,
  )
}

afterEach(() => {
  cleanup()
  resetForegroundPush()
  vi.useRealTimers()
})

describe('ForegroundPushBanner', () => {
  it('shows a push that arrives while the app is open, with its text as the drawer would', () => {
    mount()
    expect(screen.queryByTestId('foreground-push')).toBeNull()
    act(() => receiveForegroundPush({ title: 'QRLog', body: 'Sınaq bildirişi', url: '/home' }))
    expect(screen.getByTestId('foreground-push').textContent).toContain('Sınaq bildirişi')
    expect(screen.getByRole('status').textContent).toContain('QRLog')
  })

  it('opens what the push points to on a tap, and closes', () => {
    mount()
    act(() =>
      receiveForegroundPush({ title: 'İşçi çıxış etdi', body: 'X işdən çıxış etdi.\nSaat parkı · 14:55', url: '/admin/employees/p1' }),
    )
    fireEvent.click(screen.getByText('İşçi çıxış etdi'))
    expect(screen.getByText('employee card')).toBeTruthy()
    expect(screen.queryByTestId('foreground-push')).toBeNull()
  })

  it('goes by itself after a few seconds, or at once with «Bağla»; a newer push replaces the older', () => {
    vi.useFakeTimers()
    mount()
    act(() => receiveForegroundPush({ title: 'Birinci', body: 'a', url: null }))
    act(() => receiveForegroundPush({ title: 'İkinci', body: 'b', url: null }))
    expect(screen.queryByText('Birinci')).toBeNull()
    expect(screen.getByText('İkinci')).toBeTruthy()
    act(() => vi.advanceTimersByTime(BANNER_MS + 10))
    expect(screen.queryByTestId('foreground-push')).toBeNull()

    act(() => receiveForegroundPush({ title: 'Üçüncü', body: 'c', url: null }))
    fireEvent.click(screen.getByRole('button', { name: 'Bağla' }))
    expect(screen.queryByTestId('foreground-push')).toBeNull()
  })

  it('shows nothing for an arrival the live pop-up has just shown', () => {
    mount()
    act(() => noteLiveItems([{ url: '/admin/employees/p1' }]))
    act(() => receiveForegroundPush({ title: 'İşçi giriş etdi', body: 'X işə giriş etdi.', url: '/admin/employees/p1' }))
    expect(screen.queryByTestId('foreground-push')).toBeNull()
  })
})
