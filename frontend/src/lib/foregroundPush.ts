/**
 * A push that reaches the Android app while it is open on screen.
 *
 * FCM hands such a push to the app instead of drawing it, and the native shell does not ask the push plugin to draw it
 * either (that is a native setting — a new app release). Without this it is simply gone: «Sınaq bildirişi» says it was
 * sent and nothing appears, a shift reminder never shows to somebody who has the app open. So the page shows it itself,
 * as a banner (components/ForegroundPushBanner).
 *
 * One push is not shown twice: an attendance push about what the live channel has just put on screen as a pop-up (an
 * open admin panel) is dropped here. Somebody who switched the live pop-up off gets no live items, so their attendance
 * push is shown.
 */
export interface ForegroundPush {
  title: string
  body: string
  url: string | null
}

/** How long a live pop-up stands for the push about the same thing. The push follows the pop-up within seconds, or —
 *  when several arrive together — as the summary at most a minute later (the server's throttle). */
export const LIVE_COVERS_MS = 3 * 60_000

type Listener = (push: ForegroundPush) => void
const listeners = new Set<Listener>()
const liveSeenAt = new Map<string, number>()
let lastLiveAt = Number.NEGATIVE_INFINITY

/** The attendance feed — the same rule the server (FcmSender.AndroidTag) and the service worker (pushTag) use. */
export function isAttendanceUrl(url: string | null): boolean {
  return !!url && (url.startsWith('/admin/notifications') || url.startsWith('/admin/employees/'))
}

/** The live channel put these on screen now (lib/notificationHub). */
export function noteLiveItems(items: ReadonlyArray<{ url: string | null }>, now: number = Date.now()): void {
  if (items.length === 0) return
  lastLiveAt = now
  for (const i of items) if (i.url) liveSeenAt.set(i.url, now)
  for (const [url, at] of liveSeenAt) if (now - at >= LIVE_COVERS_MS) liveSeenAt.delete(url)
}

/** Has the live channel already shown what this push says? */
export function coveredByLive(push: ForegroundPush, now: number = Date.now()): boolean {
  if (!isAttendanceUrl(push.url)) return false
  // A summary («12 yeni bildiriş») is about what the pop-ups of the last minute already said.
  if (push.url!.startsWith('/admin/notifications')) return now - lastLiveAt < LIVE_COVERS_MS
  const at = liveSeenAt.get(push.url!)
  return at !== undefined && now - at < LIVE_COVERS_MS
}

/** A push arrived while the app is open (lib/push). Shown unless the live channel already showed it. */
export function receiveForegroundPush(push: ForegroundPush, now: number = Date.now()): void {
  if (!push.title && !push.body) return
  if (coveredByLive(push, now)) return
  for (const l of listeners) l(push)
}

export function onForegroundPush(listener: Listener): () => void {
  listeners.add(listener)
  return () => {
    listeners.delete(listener)
  }
}

/** Tests only: forget what the live channel showed. */
export function resetForegroundPush(): void {
  liveSeenAt.clear()
  lastLiveAt = Number.NEGATIVE_INFINITY
}
