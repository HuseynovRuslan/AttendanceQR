// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { SetPinPage } from './SetPinPage'
import { setMyReferencePhoto } from '../api/attendance'

/**
 * First sign-in on a temporary PIN: set your own PIN, then (only for an account with no reference photo)
 * take the reference selfie. The server now sets a reference only where there is none and answers 409
 * ReferencePhotoAlreadySet otherwise — and that answer must never keep anybody on this screen. Whatever the
 * save says, the employee ends up home.
 */

vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({ saveToken: vi.fn(), mustChangePin: true, role: 'Employee' }),
}))
vi.mock('../api/auth', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../api/auth')>()),
  setInitialPin: vi.fn(async () => ({ status: 200, data: { token: 't', needsReferencePhoto: true } })),
}))
vi.mock('../api/attendance', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../api/attendance')>()),
  setMyReferencePhoto: vi.fn(),
}))

beforeEach(() => {
  // A camera that works, in a DOM that has none.
  Object.defineProperty(navigator, 'mediaDevices', {
    configurable: true,
    value: { getUserMedia: vi.fn(async () => ({ getTracks: () => [] })) },
  })
  vi.spyOn(HTMLMediaElement.prototype, 'play').mockImplementation(() => Promise.resolve())
  vi.spyOn(HTMLVideoElement.prototype, 'videoWidth', 'get').mockReturnValue(640)
  vi.spyOn(HTMLVideoElement.prototype, 'videoHeight', 'get').mockReturnValue(480)
  vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(
    { drawImage: vi.fn() } as unknown as CanvasRenderingContext2D,
  )
  vi.spyOn(HTMLCanvasElement.prototype, 'toDataURL').mockReturnValue('data:image/jpeg;base64,AAAA')
})

afterEach(() => {
  cleanup()
  vi.restoreAllMocks()
})

async function signInAndShoot() {
  const { container } = render(
    <MemoryRouter initialEntries={['/set-pin']}>
      <Routes>
        <Route path="/set-pin" element={<SetPinPage />} />
        <Route path="/home" element={<div>EV</div>} />
      </Routes>
    </MemoryRouter>,
  )
  const [pin, again] = Array.from(container.querySelectorAll('input[type="password"]'))
  fireEvent.change(pin, { target: { value: '8471' } })
  fireEvent.change(again, { target: { value: '8471' } })
  await act(async () => {
    fireEvent.click(screen.getByRole('button', { name: 'Davam et' }))
  })
  await act(async () => {
    fireEvent.click(await screen.findByRole('button', { name: /Şəkil çək/ }))
  })
  await act(async () => {
    fireEvent.click(screen.getByRole('button', { name: 'Təsdiqlə' }))
  })
}

describe('first sign-in: the reference selfie', () => {
  it('goes home when the server says a reference is already set', async () => {
    vi.mocked(setMyReferencePhoto).mockResolvedValue({ status: 409, data: { error: 'ReferencePhotoAlreadySet' } })

    await signInAndShoot()

    expect(await screen.findByText('EV')).toBeTruthy()
    expect(vi.mocked(setMyReferencePhoto)).toHaveBeenCalledWith('data:image/jpeg;base64,AAAA')
  })

  it('goes home when the save itself fails', async () => {
    vi.mocked(setMyReferencePhoto).mockRejectedValue(new Error('offline'))

    await signInAndShoot()

    expect(await screen.findByText('EV')).toBeTruthy()
  })

  it('goes home when the reference is saved', async () => {
    vi.mocked(setMyReferencePhoto).mockResolvedValue({ status: 200, data: { ok: true } })

    await signInAndShoot()

    expect(await screen.findByText('EV')).toBeTruthy()
  })
})
