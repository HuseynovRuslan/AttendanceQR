// Web Push subscription for the employee app. Powers the "you forgot to check out" reminder — the
// only nudge that reaches someone after they've left work.
//
// Platform reality worth knowing: on iOS this works ONLY on iOS 16.4+ AND only when the PWA has been
// added to the home screen (in Safari's normal browser tab there is no PushManager at all). Android
// and desktop Chrome work either way. `pushSupported()` reflects exactly that.
import { apiRequest, getToken } from '../api/client'
import { Capacitor } from '@capacitor/core'
import { PushNotifications } from '@capacitor/push-notifications'
import { decodeJwt } from './jwt'
import { receiveForegroundPush } from './foregroundPush'

export interface PushKeyInfo {
  enabled: boolean
  publicKey: string
}

// ── Native app (Capacitor) push ──────────────────────────────────────────────
// The native Android/iOS app runs the site in a WebView, which has NO Web Push (no PushManager). So on
// native we use FCM via @capacitor/push-notifications instead: request the OS permission, register, and
// hand the resulting device token to the server (/api/push/register-native). Everything below only runs
// inside the app; in a browser `isNative()` is false and the ordinary Web Push path takes over.
const isNative = (): boolean => Capacitor.isNativePlatform()
const FCM_TOKEN_KEY = 'attendanceqr.fcmToken'
// checkPermissions is async but pushPermission() is a sync getter the UI reads during render, so we
// cache the last-known native permission and refresh it on every init/enable.
let nativePerm: NotificationPermission = 'default'
let nativeInited = false

/** Wire the native push listeners ONCE: forward the token to the server, and open a notification's
 *  deep-link when tapped. Also refreshes the token when permission is already granted (rotation). */
export function initNativePush(): void {
  if (!isNative() || nativeInited) return
  nativeInited = true
  void PushNotifications.addListener('registration', (t) => {
    void apiRequest('/api/push/register-native', { method: 'POST', body: { token: t.value } })
    try {
      localStorage.setItem(FCM_TOKEN_KEY, t.value)
    } catch {
      /* private mode */
    }
  })
  void PushNotifications.addListener('registrationError', () => {})
  void PushNotifications.addListener('pushNotificationActionPerformed', (a) => {
    const url = (a.notification.data as { url?: string } | undefined)?.url
    if (url) window.location.href = url
  })
  // A push that arrives while the app is open is handed to the app, not drawn by Android — the page shows it.
  void PushNotifications.addListener('pushNotificationReceived', (n) => {
    const url = (n.data as { url?: string } | undefined)?.url
    receiveForegroundPush({ title: n.title ?? '', body: n.body ?? '', url: typeof url === 'string' ? url : null })
  })
  void PushNotifications.checkPermissions().then((p) => {
    nativePerm = p.receive === 'granted' ? 'granted' : p.receive === 'denied' ? 'denied' : 'default'
    if (p.receive === 'granted') void PushNotifications.register()
  })
}

async function enablePushNative(): Promise<'ok' | 'denied' | 'failed'> {
  try {
    initNativePush()
    let perm = await PushNotifications.checkPermissions()
    if (perm.receive === 'prompt' || perm.receive === 'prompt-with-rationale')
      perm = await PushNotifications.requestPermissions()
    nativePerm = perm.receive === 'granted' ? 'granted' : perm.receive === 'denied' ? 'denied' : 'default'
    if (perm.receive !== 'granted') return 'denied'
    await PushNotifications.register() // the 'registration' listener posts the token to the server
    return 'ok'
  } catch {
    return 'failed'
  }
}

/** Does this browser expose the APIs at all? False in an iOS Safari tab, true in the installed PWA.
 *  Always true in the native app, where we use FCM instead of the Web Push APIs. */
export function pushSupported(): boolean {
  if (isNative()) return true
  return (
    typeof window !== 'undefined' &&
    'serviceWorker' in navigator &&
    'PushManager' in window &&
    'Notification' in window
  )
}

/** Current permission, or 'unsupported' when the browser has no push at all. */
export function pushPermission(): NotificationPermission | 'unsupported' {
  if (isNative()) return nativePerm
  return pushSupported() ? Notification.permission : 'unsupported'
}

function urlBase64ToUint8Array(base64: string): Uint8Array {
  const padding = '='.repeat((4 - (base64.length % 4)) % 4)
  const normalized = (base64 + padding).replace(/-/g, '+').replace(/_/g, '/')
  const raw = atob(normalized)
  const out = new Uint8Array(raw.length)
  for (let i = 0; i < raw.length; i++) out[i] = raw.charCodeAt(i)
  return out
}

/**
 * The active registration, or null — bounded by a timeout.
 *
 * `navigator.serviceWorker.ready` NEVER settles while no worker is active (it doesn't reject, it just
 * hangs), which silently froze the enable-prompt: its `isSubscribed()` check never resolved, so the
 * prompt never appeared. Everything here must therefore be time-bounded.
 */
async function activeRegistration(timeoutMs = 3000): Promise<ServiceWorkerRegistration | null> {
  if (!pushSupported()) return null
  try {
    // getRegistration() settles immediately (undefined when there is none) — unlike `ready`.
    const existing = await navigator.serviceWorker.getRegistration()
    if (existing?.active) return existing
    const ready = navigator.serviceWorker.ready.then((r) => r).catch(() => null)
    const timeout = new Promise<null>((resolve) => setTimeout(() => resolve(null), timeoutMs))
    return await Promise.race([ready, timeout])
  } catch {
    return null
  }
}

/** True when this browser already has a live subscription registered. Never hangs: when the worker
 *  isn't up we answer "not subscribed", so the prompt is shown rather than silently swallowed. */
export async function isSubscribed(): Promise<boolean> {
  if (isNative()) {
    try {
      const granted = (await PushNotifications.checkPermissions()).receive === 'granted'
      nativePerm = granted ? 'granted' : nativePerm
      return granted
    } catch {
      return false
    }
  }
  const reg = await activeRegistration()
  if (!reg) return false
  try {
    return (await reg.pushManager.getSubscription()) !== null
  } catch {
    return false
  }
}

/**
 * Asks permission, subscribes, and registers the subscription with the server.
 * Returns why it failed so the UI can say something specific rather than "error".
 */
let serverPush: Promise<boolean> | null = null

/**
 * Does this server send web push at all? A copy without push keys (the staging server) answers
 * `enabled: false`. Then there is nothing to switch on: «Bildirişləri aktiv et» could only hide itself
 * after the tap and come back on the next reload, forever. Asked once per page load. Anything but a clear
 * «no» counts as yes — a network blip must never hide the prompt on a server that does send push.
 */
export function pushServerEnabled(): Promise<boolean> {
  if (isNative()) return Promise.resolve(true)
  serverPush ??= apiRequest<PushKeyInfo>('/api/push/public-key')
    .then((r) => !(r.status === 200 && r.data && r.data.enabled === false))
    .catch(() => true)
  return serverPush
}

export async function enablePush(): Promise<'ok' | 'unsupported' | 'denied' | 'disabled' | 'failed'> {
  if (isNative()) return enablePushNative()
  if (!pushSupported()) return 'unsupported'

  const key = await apiRequest<PushKeyInfo>('/api/push/public-key')
  if (key.status !== 200 || !key.data?.enabled || !key.data.publicKey) return 'disabled'

  let permission = Notification.permission
  if (permission === 'default') permission = await Notification.requestPermission()
  if (permission !== 'granted') return 'denied'

  try {
    // Give the worker a moment longer here: the employee just tapped, so a short wait is fine — but
    // still bounded, never the open-ended `ready`.
    const reg = await activeRegistration(8000)
    if (!reg) return 'failed'
    const sub =
      (await reg.pushManager.getSubscription()) ??
      (await reg.pushManager.subscribe({
        userVisibleOnly: true,
        applicationServerKey: urlBase64ToUint8Array(key.data.publicKey) as BufferSource,
      }))

    const json = sub.toJSON() as { endpoint?: string; keys?: { p256dh?: string; auth?: string } }
    if (!json.endpoint || !json.keys?.p256dh || !json.keys?.auth) return 'failed'

    const res = await apiRequest('/api/push/subscribe', {
      method: 'POST',
      body: { endpoint: json.endpoint, p256dh: json.keys.p256dh, auth: json.keys.auth },
    })
    if (res.status !== 200) return 'failed'
    // The server just bound this device to the current session — nothing for resyncPushForToken to redo.
    const claims = decodeJwt(getToken() ?? '')
    if (claims?.sub && claims.tv !== undefined) writePushSync(`${claims.sub}:${claims.tv}`)
    return 'ok'
  } catch {
    return 'failed'
  }
}

// --- the subscription follows the session ------------------------------------
// The server sends a push only to devices registered by the account's CURRENT session: a PIN reset or
// «hər yerdən çıxış» ends the session on every device, and its pushes with it (PushSessionBinding). A
// device whose person signs in again therefore has to say so — the browser still holds its subscription,
// it is the server's binding that ended. Done once per token identity (`sub:tv`), not on every load: the
// same token never needs it twice, and a new one (sign-in, PIN change, another account on a crew phone)
// always gets it.
const PUSH_SYNC_KEY = 'attendanceqr.pushSync'

function readPushSync(): string | null {
  try {
    return localStorage.getItem(PUSH_SYNC_KEY)
  } catch {
    return null
  }
}

function writePushSync(pair: string): void {
  try {
    localStorage.setItem(PUSH_SYNC_KEY, pair)
  } catch {
    /* private mode — the next load simply asks again */
  }
}

export type PushResync = 'synced' | 'skipped' | 'failed'

/**
 * Re-registers this device's existing push subscription under the session `token` belongs to.
 * Fire-and-forget: never throws, never asks for permission, never creates a subscription the person did
 * not switch on, and is bounded in time — nothing on the sign-in or scan path waits for it.
 * A refresh never takes over a subscription another account on this browser holds (`refresh: true`).
 */
export async function resyncPushForToken(token: string | null): Promise<PushResync> {
  try {
    if (!token) return 'skipped'
    const claims = decodeJwt(token)
    // An operator's session in someone else's account must never bind the operator's browser to it, and a
    // temporary-PIN session is refused this endpoint until the person picks their own (the token changes then).
    if (!claims?.sub || claims.tv === undefined || claims.imp || claims.mcp === '1') return 'skipped'
    const pair = `${claims.sub}:${claims.tv}`
    if (readPushSync() === pair) return 'skipped'

    if (isNative()) {
      initNativePush()
      const perm = await PushNotifications.checkPermissions()
      if (perm.receive !== 'granted') return 'skipped'
      // The 'registration' listener (initNativePush) posts the token under the current session.
      await PushNotifications.register()
      writePushSync(pair)
      return 'synced'
    }

    if (!pushSupported() || Notification.permission !== 'granted') return 'skipped'
    const reg = await activeRegistration()
    if (!reg) return 'skipped'
    const sub = await reg.pushManager.getSubscription()
    if (!sub) return 'skipped'
    const json = sub.toJSON() as { endpoint?: string; keys?: { p256dh?: string; auth?: string } }
    if (!json.endpoint || !json.keys?.p256dh || !json.keys?.auth) return 'skipped'

    // Sent with THIS token (not whatever is active by the time it lands), so the pair stored below is the
    // session the server actually bound; and a 401 here — a token already dead — never signs anyone out
    // from a background call (the next ordinary request does that).
    const res = await apiRequest('/api/push/subscribe', {
      method: 'POST',
      token,
      timeoutMs: 10_000,
      body: { endpoint: json.endpoint, p256dh: json.keys.p256dh, auth: json.keys.auth, refresh: true },
    })
    if (res.status !== 200) return 'failed'
    writePushSync(pair)
    return 'synced'
  } catch {
    return 'failed'
  }
}

// --- how insistent the mandatory gate is allowed to be -----------------------
// Asking on every scan turns into a wall: a morning queue slows down, and someone who mis-taps
// "block" gets a daily obstacle they cannot resolve from inside the app. Once a day, only before a
// check-IN, and only for the first few days — after that the soft in-card prompt carries on alone.
const GATE_DAY_KEY = 'attendanceqr.pushGateDay'
const GATE_FIRST_KEY = 'attendanceqr.pushGateFirst'
const GATE_MAX_DAYS = 3

/** Whether the mandatory gate may appear right now (day- and age-limited). */
export function shouldShowPushGate(): boolean {
  try {
    const today = new Date().toISOString().slice(0, 10)
    if (localStorage.getItem(GATE_DAY_KEY) === today) return false
    const first = Number(localStorage.getItem(GATE_FIRST_KEY) ?? 0)
    if (first > 0 && Date.now() - first > GATE_MAX_DAYS * 86_400_000) return false
    return true
  } catch {
    return true
  }
}

/** Records that the gate was actually put in front of the employee today. */
export function markPushGateShown(): void {
  try {
    localStorage.setItem(GATE_DAY_KEY, new Date().toISOString().slice(0, 10))
    if (!localStorage.getItem(GATE_FIRST_KEY)) localStorage.setItem(GATE_FIRST_KEY, String(Date.now()))
  } catch {
    /* private mode — just show it again next time */
  }
}

/** One reminder the server sent to this employee (kept so the notifications tab has something to
 *  show — a push banner disappears the moment it's swiped away). */
export interface InboxItem {
  id: string
  /** 'PhotoWarning' is the one an admin sends by hand — see AdminAttendanceController. */
  type: 'CheckInSoon' | 'CheckOutSoon' | 'MissedCheckOut' | 'PhotoWarning' 
  title: string
  body: string
  createdAtUtc: string
}

/** GET /api/push/inbox — reminders sent to this employee, newest first. */
export function getInbox() {
  return apiRequest<InboxItem[]>('/api/push/inbox')
}

/** What a test push came to. `reached` > 0 = a push service took it. `transient` = it failed in a way worth
 *  trying again in a minute (timeout, 429, 5xx) — NOT a dead subscription, so the UI must not tell the person to
 *  switch notifications off and on. reached 0 without `transient` = nothing live to send to (re-enable). */
export interface TestPushResult {
  reached: number
  transient: boolean
}

/** Sends a test notification to this employee's own devices. null = the request itself failed. */
export async function sendTestPush(): Promise<TestPushResult | null> {
  const res = await apiRequest<{ reached: number; transient?: boolean }>('/api/push/test', { method: 'POST' })
  return res.status === 200 && res.data ? { reached: res.data.reached, transient: res.data.transient === true } : null
}

/** The line shown for a transient test-push failure — one wording everywhere it appears. */
export const TEST_PUSH_TRANSIENT = 'Müvəqqəti xəta — bir azdan yenidən yoxlayın'


/** Unsubscribes this browser and tells the server to forget it. */
export async function disablePush(): Promise<boolean> {
  if (isNative()) {
    // Can't revoke the OS permission from here; forgetting the token server-side stops the sends.
    try {
      const token = localStorage.getItem(FCM_TOKEN_KEY)
      if (token) await apiRequest('/api/push/unregister-native', { method: 'POST', body: { token } })
      localStorage.removeItem(FCM_TOKEN_KEY)
      nativePerm = 'default'
      return true
    } catch {
      return false
    }
  }
  if (!pushSupported()) return false
  try {
    const reg = await activeRegistration()
    if (!reg) return true
    const sub = await reg.pushManager.getSubscription()
    if (!sub) return true
    const endpoint = sub.endpoint
    await sub.unsubscribe()
    await apiRequest('/api/push/unsubscribe', { method: 'POST', body: { endpoint } })
    return true
  } catch {
    return false
  }
}


/** An admin warning this employee has not pressed «Anladım» on yet. */
export interface PendingWarning {
  id: string
  title: string
  body: string
  createdAtUtc: string
}

/** Read on every home load — the inbox is a tab people visit, this is a message that must find them. */
export function getPendingWarning() {
  return apiRequest<PendingWarning | null>('/api/push/pending-warning')
}

export function acknowledgeWarning(id: string) {
  return apiRequest<{ acknowledged: boolean }>(`/api/push/warning/${id}/ack`, { method: 'POST' })
}
