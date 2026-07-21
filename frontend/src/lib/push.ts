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

/** True when this browser already has a live subscription registered. */
export async function isSubscribed(): Promise<boolean> {
  if (!pushSupported()) return false
  try {
    const reg = await navigator.serviceWorker.ready
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
    const reg = await navigator.serviceWorker.ready
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

/** Unsubscribes this browser and tells the server to forget it. */
export async function disablePush(): Promise<boolean> {
  if (!pushSupported()) return false
  try {
    const reg = await navigator.serviceWorker.ready
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
