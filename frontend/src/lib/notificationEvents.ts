/**
 * «Something changed in the notification center» — read marks, a read-all — so the bell's unread count follows at once
 * instead of at its next poll. A window event, not a store: the two parts are far apart in the tree and share nothing
 * else.
 */
const EVENT = 'qrlog:notifications-changed'

export function emitNotificationsChanged(): void {
  window.dispatchEvent(new Event(EVENT))
}

export function onNotificationsChanged(fn: () => void): () => void {
  window.addEventListener(EVENT, fn)
  return () => window.removeEventListener(EVENT, fn)
}
