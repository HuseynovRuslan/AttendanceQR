// @vitest-environment jsdom
// In the Android app a push that arrives while the app is open comes to the page, not to the drawer — and the page
// passes it on to the banner. In a browser nothing of this is wired (the service worker draws every push).
import { beforeEach, describe, expect, it, vi } from 'vitest'

const native = vi.hoisted(() => ({ on: true, handlers: new Map<string, (arg: unknown) => void>() }))

vi.mock('../api/client', () => ({
  getToken: () => null,
  apiRequest: vi.fn(async () => ({ status: 200, data: null })),
}))
vi.mock('@capacitor/core', () => ({ Capacitor: { isNativePlatform: () => native.on } }))
vi.mock('@capacitor/push-notifications', () => ({
  PushNotifications: {
    addListener: vi.fn(async (name: string, fn: (arg: unknown) => void) => {
      native.handlers.set(name, fn)
      return { remove: () => {} }
    }),
    checkPermissions: vi.fn(async () => ({ receive: 'granted' })),
    requestPermissions: vi.fn(async () => ({ receive: 'granted' })),
    register: vi.fn(async () => {}),
  },
}))

beforeEach(() => {
  native.handlers.clear()
  vi.resetModules()
})

describe('native push while the app is open', () => {
  it('hands the push to the banner with its title, text and link', async () => {
    native.on = true
    const { initNativePush } = await import('./push')
    const { onForegroundPush } = await import('./foregroundPush')
    const got: unknown[] = []
    onForegroundPush((p) => got.push(p))

    initNativePush()
    const received = native.handlers.get('pushNotificationReceived')
    expect(received).toBeTypeOf('function')
    received!({ id: '1', title: 'QRLog', body: 'Sınaq bildirişi', data: { url: '/home' } })
    received!({ id: '2', title: 'İş vaxtınız başlayır', body: '10 dəqiqəyə növbəniz başlayır.', data: {} })

    expect(got).toEqual([
      { title: 'QRLog', body: 'Sınaq bildirişi', url: '/home' },
      { title: 'İş vaxtınız başlayır', body: '10 dəqiqəyə növbəniz başlayır.', url: null },
    ])
  })

  it('wires nothing in a browser', async () => {
    native.on = false
    const { initNativePush } = await import('./push')
    initNativePush()
    expect(native.handlers.size).toBe(0)
  })
})
