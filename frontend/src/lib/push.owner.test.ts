import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

/**
 * One push subscription per browser, whoever is signed in — and the server files an endpoint under
 * whoever posted it last. On a crew phone worker B was shown notifications as «on» because worker A had
 * turned them on; B never registered, A kept getting the pushes meant for this phone, and B got none.
 * The device now remembers whose the subscription is and re-posts it for whoever is signed in.
 */

const cap = vi.hoisted(() => ({
  native: false,
  perm: 'granted' as string,
  listeners: {} as Record<string, (arg: unknown) => void>,
  registered: 0,
}))

vi.mock('@capacitor/core', () => ({ Capacitor: { isNativePlatform: () => cap.native } }))
vi.mock('@capacitor/push-notifications', () => ({
  PushNotifications: {
    addListener: async (name: string, fn: (arg: unknown) => void) => {
      cap.listeners[name] = fn
      return { remove: async () => {} }
    },
    checkPermissions: async () => ({ receive: cap.perm }),
    requestPermissions: async () => ({ receive: cap.perm }),
    register: async () => {
      cap.registered++
    },
  },
}))

function memoryStorage(): Storage {
  const m = new Map<string, string>()
  return {
    get length() { return m.size },
    clear: () => m.clear(),
    getItem: (k) => (m.has(k) ? m.get(k)! : null),
    key: (i) => [...m.keys()][i] ?? null,
    removeItem: (k) => { m.delete(k) },
    setItem: (k, v) => { m.set(k, String(v)) },
  }
}

const jwt = (claims: object) => `h.${btoa(JSON.stringify(claims))}.s`
const WORKER_A = jwt({ sub: 'A', role: 'Employee' })
const WORKER_B = jwt({ sub: 'B', role: 'Employee' })
const OWNER = 'attendanceqr.pushOwner'

const SUB = { endpoint: 'https://push.example/ep-1', keys: { p256dh: 'p', auth: 'a' } }

type Call = { path: string; method: string; auth: string | undefined; body: unknown }
let calls: Call[] = []
let answer: (path: string) => { status: number; body: unknown } | 'throw'
let subscription: { toJSON: () => unknown; endpoint: string; unsubscribe: () => Promise<boolean> } | null
let subscribeCalls = 0
let permissionPrompts = 0

function browser(permission: NotificationPermission = 'granted') {
  const pushManager = {
    getSubscription: async () => subscription,
    subscribe: async () => {
      subscribeCalls++
      return subscription
    },
  }
  const reg = { active: {}, pushManager }
  vi.stubGlobal('navigator', {
    serviceWorker: { getRegistration: async () => reg, ready: new Promise(() => {}) },
  })
  const Notification = {
    permission,
    requestPermission: async () => {
      permissionPrompts++
      return permission
    },
  }
  vi.stubGlobal('Notification', Notification)
  vi.stubGlobal('window', { PushManager: function PushManager() {}, Notification })
}

beforeEach(() => {
  vi.resetModules()
  vi.stubGlobal('localStorage', memoryStorage())
  cap.native = false
  cap.perm = 'granted'
  cap.listeners = {}
  cap.registered = 0
  calls = []
  subscribeCalls = 0
  permissionPrompts = 0
  subscription = { toJSON: () => SUB, endpoint: SUB.endpoint, unsubscribe: async () => true }
  answer = () => ({ status: 200, body: { ok: true } })
  vi.stubGlobal('fetch', vi.fn(async (url: string, init: RequestInit) => {
    const path = new URL(url).pathname
    const headers = init.headers as Record<string, string>
    calls.push({ path, method: init.method ?? 'GET', auth: headers.Authorization, body: init.body ? JSON.parse(init.body as string) : undefined })
    const a = answer(path)
    if (a === 'throw') throw new TypeError('Failed to fetch')
    return { status: a.status, text: async () => JSON.stringify(a.body) }
  }))
  browser()
})
afterEach(() => vi.unstubAllGlobals())

function signedIn(token: string) {
  localStorage.setItem('attendanceqr.jwt', token)
}

const subscribePosts = () => calls.filter((c) => c.path === '/api/push/subscribe')

describe('a crew phone changing hands', () => {
  it('re-posts the subscription the browser already has for the worker now signed in — no prompt', async () => {
    const push = await import('./push')
    localStorage.setItem(OWNER, 'A')
    signedIn(WORKER_B)

    expect(await push.isSubscribed()).toBe(true)

    expect(subscribePosts()).toHaveLength(1)
    const post = subscribePosts()[0]
    expect(post.auth).toBe(`Bearer ${WORKER_B}`)
    expect(post.body).toEqual({ endpoint: SUB.endpoint, p256dh: 'p', auth: 'a' })
    expect(localStorage.getItem(OWNER)).toBe('B')
    expect(permissionPrompts).toBe(0)
    expect(subscribeCalls).toBe(0)
  })

  it('says nothing to the server when the subscription is already filed under this worker', async () => {
    const push = await import('./push')
    localStorage.setItem(OWNER, 'B')
    signedIn(WORKER_B)

    expect(await push.isSubscribed()).toBe(true)
    await push.syncPushOwner()

    expect(calls).toEqual([])
  })

  it('a failed re-post reads as «not on» — the screen offers it — and is tried again next time', async () => {
    const push = await import('./push')
    localStorage.setItem(OWNER, 'A')
    signedIn(WORKER_B)
    answer = () => ({ status: 500, body: null })

    expect(await push.isSubscribed()).toBe(false)
    expect(localStorage.getItem(OWNER)).toBe('A')

    answer = () => ({ status: 200, body: { ok: true } })
    expect(await push.isSubscribed()).toBe(true)
    expect(localStorage.getItem(OWNER)).toBe('B')
  })

  it('claims a device that predates the owner note, once', async () => {
    const push = await import('./push')
    signedIn(WORKER_A)

    await push.syncPushOwner()
    await push.syncPushOwner()
    await push.isSubscribed()

    expect(subscribePosts()).toHaveLength(1)
    expect(localStorage.getItem(OWNER)).toBe('A')
  })

  it('callers at the same moment share one re-post', async () => {
    const push = await import('./push')
    localStorage.setItem(OWNER, 'A')
    signedIn(WORKER_B)

    const [, on] = await Promise.all([push.syncPushOwner(), push.isSubscribed()])

    expect(on).toBe(true)
    expect(subscribePosts()).toHaveLength(1)
  })
})

describe('never in the way', () => {
  it('a dead network neither throws nor moves the note', async () => {
    const push = await import('./push')
    localStorage.setItem(OWNER, 'A')
    signedIn(WORKER_B)
    answer = () => 'throw'

    await expect(push.syncPushOwner()).resolves.toBeUndefined()
    expect(localStorage.getItem(OWNER)).toBe('A')
  })

  it('a browser with no subscription posts nothing', async () => {
    const push = await import('./push')
    subscription = null
    signedIn(WORKER_B)

    await push.syncPushOwner()
    expect(await push.isSubscribed()).toBe(false)
    expect(calls).toEqual([])
  })

  it('a browser whose permission is not granted posts nothing', async () => {
    browser('default')
    const push = await import('./push')
    signedIn(WORKER_B)

    await push.syncPushOwner()
    expect(calls).toEqual([])
  })

  it('nobody signed in: nothing to file it under', async () => {
    const push = await import('./push')
    await push.syncPushOwner()
    expect(calls).toEqual([])
  })

  it('a temporary PIN is left alone — the server refuses it everything else until the PIN is set', async () => {
    const push = await import('./push')
    signedIn(jwt({ sub: 'B', role: 'Employee', mcp: '1' }))
    await push.syncPushOwner()
    expect(calls).toEqual([])
  })

  it('never hands an operator’s browser to the admin whose seat a support session borrows', async () => {
    const push = await import('./push')
    localStorage.setItem(OWNER, 'operator')
    signedIn(jwt({ sub: 'tenant-admin', role: 'Admin' }))
    localStorage.setItem('attendanceqr.jwt.super', 'operator-token')
    localStorage.setItem('attendanceqr.impersonation', JSON.stringify({ tenantName: 'X', adminName: 'Y' }))

    await push.syncPushOwner()
    // The browser's own answer, as before — nothing re-pointed.
    expect(await push.isSubscribed()).toBe(true)
    expect(calls).toEqual([])
    expect(localStorage.getItem(OWNER)).toBe('operator')
  })
})

describe('turning it on and off keeps the note honest', () => {
  it('enablePush files it under the worker who pressed the button', async () => {
    const push = await import('./push')
    localStorage.setItem(OWNER, 'A')
    signedIn(WORKER_B)
    answer = (path) => path === '/api/push/public-key'
      ? { status: 200, body: { enabled: true, publicKey: 'AAAA' } }
      : { status: 200, body: { ok: true } }

    expect(await push.enablePush()).toBe('ok')
    expect(localStorage.getItem(OWNER)).toBe('B')
  })

  it('disablePush forgets it, so the next worker is not told it is already theirs', async () => {
    const push = await import('./push')
    localStorage.setItem(OWNER, 'B')
    signedIn(WORKER_B)

    expect(await push.disablePush()).toBe(true)
    expect(localStorage.getItem(OWNER)).toBeNull()
  })
})

describe('the native app (FCM)', () => {
  beforeEach(() => {
    cap.native = true
  })

  it('re-registers the cached device token for the worker now signed in', async () => {
    const push = await import('./push')
    localStorage.setItem('attendanceqr.fcmToken', 'fcm-1')
    localStorage.setItem(OWNER, 'A')
    signedIn(WORKER_B)

    await push.syncPushOwner()

    const posts = calls.filter((c) => c.path === '/api/push/register-native')
    expect(posts).toHaveLength(1)
    expect(posts[0].auth).toBe(`Bearer ${WORKER_B}`)
    expect(posts[0].body).toEqual({ token: 'fcm-1' })
    expect(localStorage.getItem(OWNER)).toBe('B')
  })

  it('with no token cached yet asks FCM for one, which the registration listener then files', async () => {
    const push = await import('./push')
    push.initNativePush()
    await Promise.resolve()
    signedIn(WORKER_B)

    await push.syncPushOwner()
    expect(cap.registered).toBeGreaterThan(0)

    cap.listeners.registration({ value: 'fcm-2' })
    await vi.waitFor(() => expect(localStorage.getItem(OWNER)).toBe('B'))
    const posts = calls.filter((c) => c.path === '/api/push/register-native')
    expect(posts.map((p) => p.body)).toEqual([{ token: 'fcm-2' }])
  })

  it('a token that arrives while nobody is signed in is kept, not posted unauthenticated', async () => {
    // Posting it then was a 401 — which ends the session and bounces to the login screen.
    const push = await import('./push')
    push.initNativePush()
    await Promise.resolve()

    cap.listeners.registration({ value: 'fcm-3' })
    await Promise.resolve()

    expect(calls.filter((c) => c.path === '/api/push/register-native')).toEqual([])
    expect(localStorage.getItem('attendanceqr.fcmToken')).toBe('fcm-3')

    signedIn(WORKER_A)
    await push.syncPushOwner()
    expect(calls.filter((c) => c.path === '/api/push/register-native').map((c) => c.body)).toEqual([{ token: 'fcm-3' }])
    expect(localStorage.getItem(OWNER)).toBe('A')
  })

  it('isSubscribed answers for the worker signed in, not just for the OS permission', async () => {
    const push = await import('./push')
    localStorage.setItem('attendanceqr.fcmToken', 'fcm-1')
    localStorage.setItem(OWNER, 'A')
    signedIn(WORKER_B)
    answer = () => ({ status: 500, body: null })

    expect(await push.isSubscribed()).toBe(false)
  })
})
