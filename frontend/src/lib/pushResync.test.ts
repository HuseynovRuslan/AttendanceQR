// @vitest-environment jsdom
// A device's push subscription follows the session: once per token identity (sub:tv) the app re-registers the
// subscription the browser already holds, in the background, without ever asking for permission or blocking.
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const net = vi.hoisted(() => ({
  calls: [] as { path: string; body: unknown; token?: string; timeoutMs?: number }[],
  status: 200,
  hang: false,
}))
const native = vi.hoisted(() => ({ on: false, perm: 'granted', registered: 0 }))

vi.mock('../api/client', () => ({
  getToken: () => localStorage.getItem('attendanceqr.jwt'),
  apiRequest: vi.fn(async (path: string, o: { body?: unknown; token?: string; timeoutMs?: number } = {}) => {
    net.calls.push({ path, body: o.body, token: o.token, timeoutMs: o.timeoutMs })
    if (net.hang) return new Promise(() => {})
    return { status: net.status, data: null }
  }),
}))
vi.mock('@capacitor/core', () => ({ Capacitor: { isNativePlatform: () => native.on } }))
vi.mock('@capacitor/push-notifications', () => ({
  PushNotifications: {
    addListener: vi.fn(async () => ({ remove: () => {} })),
    checkPermissions: vi.fn(async () => ({ receive: native.perm })),
    requestPermissions: vi.fn(async () => ({ receive: native.perm })),
    register: vi.fn(async () => {
      native.registered++
    }),
  },
}))

import { resyncPushForToken } from './push'

function jwt(claims: Record<string, string>): string {
  const b64 = (o: object) => btoa(JSON.stringify(o)).replace(/=+$/, '').replace(/\+/g, '-').replace(/\//g, '_')
  return `${b64({ alg: 'HS256' })}.${b64(claims)}.sig`
}

let subscription: object | null
function installBrowserPush(permission: NotificationPermission) {
  subscription = {
    toJSON: () => ({ endpoint: 'https://push.example/abc', keys: { p256dh: 'P256', auth: 'AUTH' } }),
  }
  const registration = { active: {}, pushManager: { getSubscription: async () => subscription } }
  Object.defineProperty(navigator, 'serviceWorker', {
    configurable: true,
    value: { getRegistration: async () => registration, ready: Promise.resolve(registration) },
  })
  ;(window as unknown as { PushManager: unknown }).PushManager = function PushManager() {}
  ;(window as unknown as { Notification: unknown }).Notification = { permission }
  ;(globalThis as unknown as { Notification: unknown }).Notification = { permission }
}

const subscribes = () => net.calls.filter((c) => c.path === '/api/push/subscribe')

beforeEach(() => {
  localStorage.clear()
  net.calls = []
  net.status = 200
  net.hang = false
  native.on = false
  native.perm = 'granted'
  native.registered = 0
  installBrowserPush('granted')
})
afterEach(() => vi.restoreAllMocks())

describe('resyncPushForToken', () => {
  it('re-registers the existing subscription once per sub:tv, as a refresh, with that very token', async () => {
    const t = jwt({ sub: 'emp-1', tv: '3' })
    expect(await resyncPushForToken(t)).toBe('synced')
    expect(subscribes()).toHaveLength(1)
    expect(subscribes()[0].body).toEqual({ endpoint: 'https://push.example/abc', p256dh: 'P256', auth: 'AUTH', refresh: true })
    expect(subscribes()[0].token).toBe(t)
    expect(subscribes()[0].timeoutMs).toBeGreaterThan(0)

    // The same token again — an ordinary reload — costs nothing.
    expect(await resyncPushForToken(t)).toBe('skipped')
    expect(subscribes()).toHaveLength(1)

    // The PIN was reset and the person signed in again: a new TokenVersion, so the binding is renewed.
    expect(await resyncPushForToken(jwt({ sub: 'emp-1', tv: '4' }))).toBe('synced')
    // Another account on the same (crew) phone gets its own attempt; the server decides it is not theirs.
    expect(await resyncPushForToken(jwt({ sub: 'emp-2', tv: '0' }))).toBe('synced')
    expect(subscribes()).toHaveLength(3)
  })

  it('never asks for permission and never creates a subscription nobody switched on', async () => {
    installBrowserPush('default')
    expect(await resyncPushForToken(jwt({ sub: 'emp-1', tv: '1' }))).toBe('skipped')
    installBrowserPush('granted')
    subscription = null
    expect(await resyncPushForToken(jwt({ sub: 'emp-1', tv: '1' }))).toBe('skipped')
    expect(subscribes()).toHaveLength(0)
  })

  it('skips an operator session, a temporary-PIN session and a token without a version', async () => {
    expect(await resyncPushForToken(jwt({ sub: 'emp-1', tv: '1', imp: 'op-9' }))).toBe('skipped')
    expect(await resyncPushForToken(jwt({ sub: 'emp-1', tv: '1', mcp: '1' }))).toBe('skipped')
    expect(await resyncPushForToken(jwt({ sub: 'emp-1' }))).toBe('skipped')
    expect(await resyncPushForToken(null)).toBe('skipped')
    expect(await resyncPushForToken('not-a-jwt')).toBe('skipped')
    expect(subscribes()).toHaveLength(0)
  })

  it('tries again on the next load when the server did not take it', async () => {
    const t = jwt({ sub: 'emp-1', tv: '2' })
    net.status = 401
    expect(await resyncPushForToken(t)).toBe('failed')
    net.status = 200
    expect(await resyncPushForToken(t)).toBe('synced')
    expect(subscribes()).toHaveLength(2)
  })

  it('survives a localStorage that throws (private mode) and still syncs', async () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('denied')
    })
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('denied')
    })
    await expect(resyncPushForToken(jwt({ sub: 'emp-1', tv: '5' }))).resolves.toBe('synced')
  })

  it('never throws, whatever the platform does', async () => {
    Object.defineProperty(navigator, 'serviceWorker', {
      configurable: true,
      value: {
        getRegistration: async () => {
          throw new Error('SecurityError')
        },
        ready: new Promise(() => {}),
      },
    })
    await expect(resyncPushForToken(jwt({ sub: 'emp-1', tv: '6' }))).resolves.toBe('skipped')
  })

  it('does not hold up its caller: a sign-in that fires it and moves on is not waiting', async () => {
    net.hang = true
    let settled = false
    void resyncPushForToken(jwt({ sub: 'emp-1', tv: '7' })).then(() => (settled = true))
    await new Promise((r) => setTimeout(r, 20))
    // The request is out with a deadline, the caller already moved on, and nothing was thrown at it.
    expect(subscribes()).toHaveLength(1)
    expect(subscribes()[0].timeoutMs).toBe(10_000)
    expect(settled).toBe(false)
  })

  it('native app: registers again with the OS when permission is granted, once per token', async () => {
    native.on = true
    const t = jwt({ sub: 'emp-1', tv: '3' })
    expect(await resyncPushForToken(t)).toBe('synced')
    await new Promise((r) => setTimeout(r, 0)) // the listeners' own start-up registration (initNativePush)
    const afterFirst = native.registered
    expect(afterFirst).toBeGreaterThan(0)
    expect(await resyncPushForToken(t)).toBe('skipped')
    expect(native.registered).toBe(afterFirst)
    expect(await resyncPushForToken(jwt({ sub: 'emp-1', tv: '4' }))).toBe('synced')
    expect(native.registered).toBe(afterFirst + 1)
    native.perm = 'denied'
    expect(await resyncPushForToken(jwt({ sub: 'emp-1', tv: '5' }))).toBe('skipped')
    expect(native.registered).toBe(afterFirst + 1)
  })
})
