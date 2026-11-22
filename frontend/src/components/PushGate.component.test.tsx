// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { PUSH_GATE_CHECK_MS, PushGate } from './PushGate'

/**
 * The notification ask that stands between an employee and the scanner for their first few days.
 * CLAUDE.md: a check-in is never blocked by anything optional — not by notifications. So whatever the
 * push machinery does (hang, fail, throw), there is always a way on to the scan.
 */

const push = vi.hoisted(() => ({
  supported: true,
  permission: 'default' as NotificationPermission,
  isSubscribed: (() => Promise.resolve(false)) as () => Promise<boolean>,
  serverEnabled: (() => Promise.resolve(true)) as () => Promise<boolean>,
  enable: (() => Promise.resolve('ok')) as () => Promise<string>,
  shown: 0,
}))

vi.mock('../lib/push', () => ({
  pushSupported: () => push.supported,
  pushPermission: () => push.permission,
  isSubscribed: () => push.isSubscribed(),
  pushServerEnabled: () => push.serverEnabled(),
  enablePush: () => push.enable(),
  markPushGateShown: () => {
    push.shown++
  },
}))
vi.mock('../lib/device', () => ({ isStandalone: () => true }))

const never = <T,>() => new Promise<T>(() => {})

beforeEach(() => {
  vi.useFakeTimers()
  push.supported = true
  push.permission = 'default'
  push.isSubscribed = () => Promise.resolve(false)
  push.serverEnabled = () => Promise.resolve(true)
  push.enable = () => Promise.resolve('ok')
  push.shown = 0
})
afterEach(() => {
  cleanup()
  vi.useRealTimers()
})

/** Let the gate's own awaits run (it is all promises that are already settled, or never will be). */
const flush = () => act(async () => {
  await Promise.resolve()
  await Promise.resolve()
  await Promise.resolve()
})

async function renderGate(onDone = vi.fn()) {
  const view = render(<PushGate onDone={onDone} />)
  await flush()
  return { onDone, ...view }
}

describe('while it finds out whether to ask', () => {
  it('steps aside after the time-box when the server never answers', async () => {
    push.serverEnabled = () => never<boolean>()
    const { onDone } = await renderGate()
    expect(onDone).not.toHaveBeenCalled()

    await act(async () => {
      await vi.advanceTimersByTimeAsync(PUSH_GATE_CHECK_MS)
    })

    expect(onDone).toHaveBeenCalledTimes(1)
    // It was never shown, so it does not use up today's one ask.
    expect(push.shown).toBe(0)
  })

  it('steps aside when the subscription check itself hangs', async () => {
    push.isSubscribed = () => never<boolean>()
    const { onDone } = await renderGate()
    await act(async () => {
      await vi.advanceTimersByTimeAsync(PUSH_GATE_CHECK_MS)
    })
    expect(onDone).toHaveBeenCalledTimes(1)
  })

  it('keeps one time-box however often the scan page re-renders', async () => {
    // ScanPage passes a new arrow every render. Restarting the check on each one meant a page that kept
    // re-rendering kept the gate blank for ever.
    push.serverEnabled = () => never<boolean>()
    const first = vi.fn()
    const later = vi.fn()
    const { rerender } = await renderGate(first)
    for (let i = 0; i < 4; i++) {
      await act(async () => {
        await vi.advanceTimersByTimeAsync(PUSH_GATE_CHECK_MS / 5)
      })
      rerender(<PushGate onDone={later} />)
    }
    await act(async () => {
      await vi.advanceTimersByTimeAsync(PUSH_GATE_CHECK_MS / 5)
    })
    expect(first).not.toHaveBeenCalled()
    expect(later).toHaveBeenCalledTimes(1)
  })

  it('a late answer after the time-box does not put the ask back up', async () => {
    let answer: (v: boolean) => void = () => {}
    push.serverEnabled = () => new Promise<boolean>((r) => { answer = r })
    const { onDone } = await renderGate()
    await act(async () => {
      await vi.advanceTimersByTimeAsync(PUSH_GATE_CHECK_MS)
    })
    answer(true)
    await flush()
    expect(onDone).toHaveBeenCalledTimes(1)
    expect(screen.queryByText('Aktiv et və davam et')).toBeNull()
    expect(push.shown).toBe(0)
  })

  it('an employee already subscribed goes straight through', async () => {
    push.isSubscribed = () => Promise.resolve(true)
    const { onDone } = await renderGate()
    expect(onDone).toHaveBeenCalledTimes(1)
  })
})

describe('the ask', () => {
  it('offers «Sonra», which goes to the scan without turning anything on', async () => {
    const enable = vi.fn(() => Promise.resolve('ok'))
    push.enable = enable
    const { onDone } = await renderGate()
    expect(screen.getByText('Aktiv et və davam et')).toBeTruthy()
    expect(push.shown).toBe(1)

    fireEvent.click(screen.getByRole('button', { name: 'Sonra' }))

    expect(onDone).toHaveBeenCalledTimes(1)
    expect(enable).not.toHaveBeenCalled()
  })

  it('after a failure still has a way on, not just «Yenidən cəhd edin»', async () => {
    push.enable = () => Promise.resolve('failed')
    const { onDone } = await renderGate()
    await act(async () => {
      fireEvent.click(screen.getByText('Aktiv et və davam et'))
    })
    await flush()
    expect(screen.getByText('Alınmadı. Yenidən cəhd edin.')).toBeTruthy()
    expect(onDone).not.toHaveBeenCalled()

    fireEvent.click(screen.getByRole('button', { name: 'Sonra' }))
    expect(onDone).toHaveBeenCalledTimes(1)
  })

  it('«Sonra» works while an attempt is still hanging, and the attempt cannot call them back', async () => {
    let settle: (v: string) => void = () => {}
    push.enable = () => new Promise<string>((r) => { settle = r })
    const { onDone } = await renderGate()
    await act(async () => {
      fireEvent.click(screen.getByText('Aktiv et və davam et'))
    })
    expect(screen.getByText('Aktivləşdirilir…')).toBeTruthy()
    const later = screen.getByRole('button', { name: 'Sonra' }) as HTMLButtonElement
    expect(later.disabled).toBe(false)

    fireEvent.click(later)
    expect(onDone).toHaveBeenCalledTimes(1)

    settle('ok')
    await flush()
    expect(onDone).toHaveBeenCalledTimes(1)
  })

  it('a rejected attempt is a failure, not a button stuck on «Aktivləşdirilir…»', async () => {
    push.enable = () => Promise.reject(new TypeError('Failed to fetch'))
    await renderGate()
    await act(async () => {
      fireEvent.click(screen.getByText('Aktiv et və davam et'))
    })
    await flush()
    expect(screen.getByText('Alınmadı. Yenidən cəhd edin.')).toBeTruthy()
    expect(screen.getByText('Aktiv et və davam et')).toBeTruthy()
  })

  it('still turns them on and continues when it works', async () => {
    const { onDone } = await renderGate()
    await act(async () => {
      fireEvent.click(screen.getByText('Aktiv et və davam et'))
    })
    await flush()
    expect(onDone).toHaveBeenCalledTimes(1)
  })
})
