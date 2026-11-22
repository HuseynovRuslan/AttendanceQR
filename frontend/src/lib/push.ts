// Web Push subscription for the employee app. Powers the "you forgot to check out" reminder — the
// only nudge that reaches someone after they've left work.
//
// Platform reality worth knowing: on iOS this works ONLY on iOS 16.4+ AND only when the PWA has been
// added to the home screen (in Safari's normal browser tab there is no PushManager at all). Android
// and desktop Chrome work either way. `pushSupported()` reflects exactly that.
import { apiRequest, getImpersonation, getToken } from '../api/client'
import { Capacitor } from '@capacitor/core'
import { PushNotifications } from '@capacitor/push-notifications'
import { isOperatorHost } from './host'
import { decodeJwt } from './jwt'

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

// ── Whose subscription this is ───────────────────────────────────────────────
// A browser (or the app) holds ONE push subscription for this site, whoever happens to be signed in, and
// the server files an endpoint under whichever account posted it last (PushController re-points it). So
// «this browser is subscribed» is not «this account is»: on a crew phone, worker A turned notifications
// on, worker B signed in on the same phone and was shown them as already on — B never registered, and A
// went on receiving the pushes meant for B's phone while B got none, «çıxışı unutdunuz» included.
//
// So the device remembers which account it last registered the subscription for, and when somebody else
// is signed in it re-posts the subscription it already has, as them. No new permission is asked for:
// the browser's permission and subscription are the device's and stay as they are; only the server's
// note of whose they are changes. Done on login, on a profile switch (a full reload) and on app start —
// see AuthProvider — and whenever a screen asks isSubscribed().
const OWNER_KEY = 'attendanceqr.pushOwner'

function readOwner(): string | null {
  try {
    return localStorage.getItem(OWNER_KEY)
  } catch {
    return null
  }
}

function writeOwner(employeeId: string | null): void {
  try {
    if (employeeId) localStorage.setItem(OWNER_KEY, employeeId)
    else localStorage.removeItem(OWNER_KEY)
  } catch {
    /* private mode — it is simply re-posted next time, which is harmless */
  }
}

/**
 * The account the subscription should be filed under right now — or null when it is not ours to move.
 * Not during a support or view session: the operator's own browser would be handed to the admin whose
 * seat they borrow, and that admin's notifications would arrive on the operator's machine. Not on the
 * operator console either (a cross-tenant token), and not on a temporary PIN, which the server refuses
 * everything but «set your PIN» until it is changed — the fresh token that follows is claimed instead.
 */
function claimant(): string | null {
  try {
    const token = getToken()
    if (!token || getImpersonation() || isOperatorHost()) return null
    const claims = decodeJwt(token)
    if (!claims?.sub || claims.mcp === '1') return null
    return claims.sub
  } catch {
    return null // storage refused outright: nothing we can attribute, so nothing to move
  }
}

let claiming: { account: string; done: Promise<boolean> } | null = null

/** File this device's existing subscription under `account`. True when the server now has it there. */
function claimFor(account: string): Promise<boolean> {
  if (readOwner() === account) return Promise.resolve(true)
  if (claiming?.account === account) return claiming.done
  const done = (isNative() ? claimNative(account) : claimWeb(account)).catch(() => false)
  const entry = { account, done }
  claiming = entry
  void done.then(() => {
    if (claiming === entry) claiming = null
  })
  return done
}

async function claimWeb(account: string): Promise<boolean> {
  if (!pushSupported() || Notification.permission !== 'granted') return false
  const reg = await activeRegistration()
  if (!reg) return false
  const sub = await reg.pushManager.getSubscription()
  if (!sub) return false
  const json = sub.toJSON() as { endpoint?: string; keys?: { p256dh?: string; auth?: string } }
  if (!json.endpoint || !json.keys?.p256dh || !json.keys?.auth) return false
  // The request is signed with whatever token is active when it goes out. If somebody else signed in
  // while the worker was being found, this one is theirs to make, not ours.
  if (claimant() !== account) return false
  const res = await apiRequest('/api/push/subscribe', {
    method: 'POST',
    body: { endpoint: json.endpoint, p256dh: json.keys.p256dh, auth: json.keys.auth },
    timeoutMs: 8000,
  })
  if (res.status !== 200) return false
  writeOwner(account)
  return true
}

async function claimNative(account: string): Promise<boolean> {
  const perm = await PushNotifications.checkPermissions()
  if (perm.receive !== 'granted') return false
  let token: string | null = null
  try {
    token = localStorage.getItem(FCM_TOKEN_KEY)
  } catch {
    /* private mode */
  }
  if (!token) {
    // No token yet: ask FCM for it. The 'registration' listener posts it for whoever is signed in then.
    await PushNotifications.register()
    return false
  }
  if (claimant() !== account) return false
  const res = await apiRequest('/api/push/register-native', { method: 'POST', body: { token }, timeoutMs: 8000 })
  if (res.status !== 200) return false
  writeOwner(account)
  return true
}

/**
 * Make sure the server sends this device's notifications to whoever is signed in on it now. A no-op when
 * it already does, when there is no subscription, or when nobody (or an operator) is signed in. Never
 * prompts, never throws and never needs awaiting — a failure is retried on the next login, app start or
 * isSubscribed(), and nothing else waits on it.
 */
export function syncPushOwner(): Promise<void> {
  try {
    const account = claimant()
    if (!account || !pushSupported()) return Promise.resolve()
    return claimFor(account).then(
      () => undefined,
      () => undefined,
    )
  } catch {
    return Promise.resolve()
  }
}

/** Wire the native push listeners ONCE: forward the token to the server, and open a notification's
 *  deep-link when tapped. Also refreshes the token when permission is already granted (rotation). */
export function initNativePush(): void {
  if (!isNative() || nativeInited) return
  nativeInited = true
  void PushNotifications.addListener('registration', (t) => {
    let previous: string | null = null
    try {
      previous = localStorage.getItem(FCM_TOKEN_KEY)
      localStorage.setItem(FCM_TOKEN_KEY, t.value)
    } catch {
      /* private mode */
    }
    // The owner note describes the token the server was given. One FCM has rotated is filed under nobody
    // yet, so the note goes: otherwise the worker it named would sign in, find their own account there,
    // and post nothing — and the server would go on pushing to a token FCM has retired. The same token
    // handed back on an ordinary app start keeps the note.
    if (t.value !== previous) writeOwner(null)
    // Filed under whoever is signed in as it goes out, and remembered as theirs — see «Whose subscription
    // this is». Nobody signed in (the app opened on the login screen): nothing to file it under yet; the
    // token is kept, and syncPushOwner posts it once somebody signs in.
    const account = claimant()
    if (!account) return
    void apiRequest('/api/push/register-native', { method: 'POST', body: { token: t.value } })
      .then((r) => {
        if (r.status === 200 && claimant() === account) writeOwner(account)
      })
      .catch(() => {})
  })
  void PushNotifications.addListener('registrationError', () => {})
  void PushNotifications.addListener('pushNotificationActionPerformed', (a) => {
    const url = (a.notification.data as { url?: string } | undefined)?.url
    if (url) window.location.href = url
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

/** True when this device has a live subscription AND it is registered for the account signed in now.
 *  A subscription filed under somebody else (a crew phone) is re-posted for this account first — no
 *  prompt — and only a failed re-post answers false, so the screen offers to turn it on rather than
 *  showing it on. Never hangs: when the worker isn't up we answer "not subscribed", so the prompt is
 *  shown rather than silently swallowed. */
export async function isSubscribed(): Promise<boolean> {
  if (isNative()) {
    try {
      const granted = (await PushNotifications.checkPermissions()).receive === 'granted'
      nativePerm = granted ? 'granted' : nativePerm
      if (!granted) return false
    } catch {
      return false
    }
    const account = claimant()
    return account ? claimFor(account) : true
  }
  const reg = await activeRegistration()
  if (!reg) return false
  try {
    if ((await reg.pushManager.getSubscription()) === null) return false
  } catch {
    return false
  }
  // Signed in as nobody we track (an operator, a support session): the browser's answer is all there is.
  const account = claimant()
  return account ? claimFor(account) : true
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

    const account = claimant()
    const res = await apiRequest('/api/push/subscribe', {
      method: 'POST',
      body: { endpoint: json.endpoint, p256dh: json.keys.p256dh, auth: json.keys.auth },
    })
    if (res.status !== 200) return 'failed'
    if (account) writeOwner(account)
    return 'ok'
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

/** Sends a test notification to this employee's own devices. Returns how many were reached — 0 means
 *  the subscription didn't survive (re-enable), so the UI can say something useful. */
export async function sendTestPush(): Promise<number | null> {
  const res = await apiRequest<{ reached: number }>('/api/push/test', { method: 'POST' })
  return res.status === 200 && res.data ? res.data.reached : null
}

/** Unsubscribes this browser and tells the server to forget it. */
export async function disablePush(): Promise<boolean> {
  if (isNative()) {
    // Can't revoke the OS permission from here; forgetting the token server-side stops the sends.
    try {
      const token = localStorage.getItem(FCM_TOKEN_KEY)
      if (token) await apiRequest('/api/push/unregister-native', { method: 'POST', body: { token } })
      localStorage.removeItem(FCM_TOKEN_KEY)
      writeOwner(null)
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
    writeOwner(null)
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
