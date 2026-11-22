// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { PinChangeSheet } from './PinChangeSheet'
import { changePassword } from '../api/auth'

/**
 * «PIN-i dəyiş» asks for the current PIN, and the server now limits how often it may be guessed (8 tries,
 * then a cool-off that grows). The screen has to say "wait N minutes" in that case — not "PIN dəyişdirilmədi",
 * which reads as a fault and sends the employee to try again, straight into the lock.
 */

vi.mock('../auth/AuthContext', () => ({ useAuth: () => ({ saveToken: vi.fn() }) }))
vi.mock('../api/auth', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../api/auth')>()),
  changePassword: vi.fn(),
}))

afterEach(cleanup)

async function submit(current: string, next: string) {
  const { container } = render(
    <MemoryRouter>
      <PinChangeSheet onClose={() => {}} />
    </MemoryRouter>,
  )
  const [cur, nw, again] = Array.from(container.querySelectorAll('input[type="password"]'))
  fireEvent.change(cur, { target: { value: current } })
  fireEvent.change(nw, { target: { value: next } })
  fireEvent.change(again, { target: { value: next } })
  await act(async () => {
    fireEvent.click(screen.getByRole('button', { name: 'PIN-i dəyiş' }))
  })
}

describe('PIN change sheet: the current-PIN lockout', () => {
  it('tells the employee how long to wait when the server locks the change', async () => {
    vi.mocked(changePassword).mockResolvedValue({ status: 429, data: { error: 'TooManyAttempts', minutes: 15 } })

    await submit('0000', '8471')

    expect(await screen.findByText('Çox sayda yanlış cəhd — 15 dəqiqə sonra yenidən cəhd edin')).toBeTruthy()
  })

  it('still says the current PIN is wrong before the lock', async () => {
    vi.mocked(changePassword).mockResolvedValue({ status: 401, data: { error: 'InvalidCurrentPassword' } })

    await submit('0000', '8471')

    expect(await screen.findByText('Cari PIN yanlışdır')).toBeTruthy()
  })
})
