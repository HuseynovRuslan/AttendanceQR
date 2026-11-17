import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { getPosition, POOR_ACCURACY_METERS, REFINE_MS, type GeoResult } from './geo'

/**
 * What the phone hands back, and when. Real phones answer the watch asynchronously and often more
 * than once — a cell-tower estimate first, satellites later — which is the whole story here.
 */
type Emit = { at: number; accuracy: number } | { at: number; deny: true }

function fakePhone(script: Emit[], network?: { at: number; accuracy: number }) {
  const clearWatch = vi.fn()
  const watchPosition = vi.fn((ok: PositionCallback, fail?: PositionErrorCallback | null) => {
    for (const e of script) {
      setTimeout(() => {
        if ('deny' in e) fail?.({ code: 1, message: 'denied' } as GeolocationPositionError)
        else ok({ coords: { latitude: 40.34, longitude: 49.84, accuracy: e.accuracy }, timestamp: e.at } as GeolocationPosition)
      }, e.at)
    }
    return 7
  })
  // The coarse network attempt that runs only when the watch produced nothing at all.
  const getCurrentPosition = vi.fn((ok: PositionCallback, fail?: PositionErrorCallback | null) => {
    setTimeout(() => {
      if (network) ok({ coords: { latitude: 40.34, longitude: 49.84, accuracy: network.accuracy }, timestamp: 0 } as GeolocationPosition)
      else fail?.({ code: 3, message: 'timeout' } as GeolocationPositionError)
    }, network?.at ?? 100)
  })
  vi.stubGlobal('navigator', { geolocation: { watchPosition, clearWatch, getCurrentPosition } })
  return { clearWatch, getCurrentPosition }
}

/** Starts a lookup and exposes its result as soon as it settles, so a test can ask "not yet?". */
function start() {
  const box: { result?: GeoResult } = {}
  void getPosition().then((r) => { box.result = r })
  return box
}

const accuracyOf = (r?: GeoResult) => (r && r.ok ? r.coords.accuracy : undefined)

beforeEach(() => { vi.useFakeTimers() })
afterEach(() => {
  vi.useRealTimers()
  vi.unstubAllGlobals()
})

describe('getPosition', () => {
  it('uses a good fix the moment it arrives', async () => {
    const phone = fakePhone([{ at: 400, accuracy: 12 }])
    const box = start()

    await vi.advanceTimersByTimeAsync(500)

    expect(accuracyOf(box.result)).toBe(12)
    expect(phone.clearWatch).toHaveBeenCalledWith(7)
  })

  it('does not settle for a cell-tower estimate while the satellites are still coming', async () => {
    // Baxşəliyev's morning, minus the ninety minutes: ±2000 m at once, a real fix a few seconds later.
    fakePhone([{ at: 300, accuracy: 2000 }, { at: 6_000, accuracy: 8 }])
    const box = start()

    await vi.advanceTimersByTimeAsync(1_000)
    expect(box.result).toBeUndefined()

    await vi.advanceTimersByTimeAsync(5_100)
    expect(accuracyOf(box.result)).toBe(8)
  })

  it('still uses the coarse fix when nothing better comes — never worse than before', async () => {
    fakePhone([{ at: 300, accuracy: 2000 }])
    const box = start()

    await vi.advanceTimersByTimeAsync(300 + REFINE_MS - 100)
    expect(box.result).toBeUndefined()

    await vi.advanceTimersByTimeAsync(200)
    expect(accuracyOf(box.result)).toBe(2000)
  })

  it('keeps the best of several coarse fixes, not the latest', async () => {
    fakePhone([{ at: 300, accuracy: 2000 }, { at: 4_000, accuracy: 400 }, { at: 8_000, accuracy: 700 }])
    const box = start()

    await vi.advanceTimersByTimeAsync(300 + REFINE_MS + 100)

    expect(accuracyOf(box.result)).toBe(400)
  })

  it('stops waiting as soon as a refinement is good enough to judge a 150 m fence', async () => {
    fakePhone([{ at: 300, accuracy: 500 }, { at: 2_000, accuracy: POOR_ACCURACY_METERS }])
    const box = start()

    await vi.advanceTimersByTimeAsync(2_100)

    expect(accuracyOf(box.result)).toBe(POOR_ACCURACY_METERS)
  })

  it('never lets the refinement outlast the overall budget', async () => {
    // A coarse fix first seen at 40 s is answered at 45 s, not held until 55.
    fakePhone([{ at: 40_000, accuracy: 900 }])
    const box = start()

    await vi.advanceTimersByTimeAsync(45_100)

    expect(accuracyOf(box.result)).toBe(900)
  })

  it('ends at once on a refusal', async () => {
    fakePhone([{ at: 200, deny: true }])
    const box = start()

    await vi.advanceTimersByTimeAsync(300)

    expect(box.result).toEqual({ ok: false, kind: 'denied' })
  })

  it('leaves the cold start as it was: nothing from the sky → one network attempt', async () => {
    const phone = fakePhone([], { at: 1_000, accuracy: 800 })
    const box = start()

    await vi.advanceTimersByTimeAsync(45_000 + 1_100)

    expect(phone.getCurrentPosition).toHaveBeenCalledTimes(1)
    expect(accuracyOf(box.result)).toBe(800)
  })

  it('does not make the network attempt when the watch did produce a fix', async () => {
    const phone = fakePhone([{ at: 300, accuracy: 2000 }])
    const box = start()

    await vi.advanceTimersByTimeAsync(300 + REFINE_MS + 100)

    expect(accuracyOf(box.result)).toBe(2000)
    expect(phone.getCurrentPosition).not.toHaveBeenCalled()
  })
})
