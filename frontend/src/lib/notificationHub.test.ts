import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

/**
 * The live channel's own lifecycle, against a fake SignalR: it does not stay open for nobody, and a connection that the
 * server closed (the end of a ticket's lifetime) comes back by itself — with a refetch, since lines may have been
 * written while it was gone.
 */

const fake = vi.hoisted(() => ({
  conns: [] as Array<{
    started: boolean
    stopped: boolean
    onclose?: () => void
    resolveStart?: () => void
  }>,
  holdStart: false,
}))

vi.mock('../api/client', () => ({ API_BASE_URL: 'http://api.test' }))
vi.mock('../api/notificationCenter', () => ({
  getStreamTicket: async () => ({ status: 200, data: { ticket: 't', expiresInSeconds: 120 } }),
}))
vi.mock('@microsoft/signalr', () => {
  class HubConnectionBuilder {
    withUrl() { return this }
    withAutomaticReconnect() { return this }
    configureLogging() { return this }
    build() {
      const c: (typeof fake.conns)[number] = { started: false, stopped: false }
      fake.conns.push(c)
      return {
        on: () => {},
        onreconnecting: () => {},
        onreconnected: () => {},
        onclose: (fn: () => void) => { c.onclose = fn },
        start: () => {
          c.started = true
          if (!fake.holdStart) return Promise.resolve()
          return new Promise<void>((resolve) => { c.resolveStart = resolve })
        },
        stop: () => { c.stopped = true; return Promise.resolve() },
      }
    }
  }
  return { HubConnectionBuilder, LogLevel: { Warning: 3 } }
})

async function load() {
  vi.resetModules()
  return import('./notificationHub')
}

const flush = () => new Promise((r) => setTimeout(r, 0))

beforeEach(() => {
  fake.conns = []
  fake.holdStart = false
})
afterEach(() => vi.useRealTimers())

describe('live channel lifecycle', () => {
  it('does not keep a connection open when the last listener left while it was connecting', async () => {
    const hub = await load()
    fake.holdStart = true
    const off = hub.subscribeLive({})
    await flush()
    await flush()
    off()
    fake.conns[0].resolveStart?.()
    await flush()
    await flush()
    expect(fake.conns[0].stopped).toBe(true)
    expect(hub.liveStatus()).toBe('offline')
  })

  it('comes back soon after the server closed it, and tells listeners to refetch', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] })
    const hub = await load()
    const onReconnected = vi.fn()
    hub.subscribeLive({ onReconnected })
    await vi.waitFor(() => expect(hub.liveStatus()).toBe('live'))
    expect(onReconnected).not.toHaveBeenCalled()

    fake.conns[0].onclose?.()
    expect(hub.liveStatus()).toBe('offline')
    await vi.advanceTimersByTimeAsync(3_000)
    await vi.waitFor(() => expect(hub.liveStatus()).toBe('live'))
    expect(fake.conns).toHaveLength(2)
    expect(onReconnected).toHaveBeenCalledTimes(1)
  })
})
