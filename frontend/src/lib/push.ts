// Web Push subscription for the employee app. Powers the "you forgot to check out" reminder — the
// only nudge that reaches someone after they've left work.
//
// Platform reality worth knowing: on iOS this works ONLY on iOS 16.4+ AND only when the PWA has been
// added to the home screen (in Safari's normal browser tab there is no PushManager at all). Android
// and desktop Chrome work either way. `pushSupported()` reflects exactly that.
import { apiRequest } from '../api/client'

export interface PushKeyInfo {
  enabled: boolean
  publicKey: string
}

/** Does this browser expose the APIs at all? False in an iOS Safari tab, true in the installed PWA. */
export function pushSupported(): boolean {
  return (
    typeof window !== 'undefined' &&
    'serviceWorker' in navigator &&
    'PushManager' in window &&
    'Notification' in window
  )
}

/** Current permission, or 'unsupported' when the browser has no push at all. */
export function pushPermission(): NotificationPermission | 'unsupported' {
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
export async function enablePush(): Promise<'ok' | 'unsupported' | 'denied' | 'disabled' | 'failed'> {
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
    return res.status === 200 ? 'ok' : 'failed'
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

/** Sends a test notification to this employee's own devices. Returns how many were reached — 0 means
 *  the subscription didn't survive (re-enable), so the UI can say something useful. */
export async function sendTestPush(): Promise<number | null> {
  const res = await apiRequest<{ reached: number }>('/api/push/test', { method: 'POST' })
  return res.status === 200 && res.data ? res.data.reached : null
}

/** Unsubscribes this browser and tells the server to forget it. */
export async function disablePush(): Promise<boolean> {
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
