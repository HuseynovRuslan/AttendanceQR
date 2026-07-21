/**
 * Chromium's install prompt, held for our own button.
 *
 * Chrome fires `beforeinstallprompt` when the PWA install criteria are met (manifest + a service
 * worker with a fetch handler + HTTPS). Calling preventDefault() suppresses Chrome's own mini-infobar
 * and hands us the event, which we can replay later from a real button — so the employee installs in
 * one tap instead of hunting through the ⋮ menu.
 *
 * The listener is registered at MODULE LOAD (imported from main.tsx before React renders): the event
 * often fires before any component mounts, and it is not replayed — a listener added inside a
 * component would simply miss it and the button would never appear.
 *
 * iOS has no equivalent: Apple exposes no API for "Add to Home Screen", so there the UI falls back to
 * instructions (see InstallHint).
 */

/** The subset of BeforeInstallPromptEvent we use — it isn't in the DOM lib typings. */
interface InstallPromptEvent extends Event {
  prompt: () => Promise<void>
  userChoice: Promise<{ outcome: 'accepted' | 'dismissed' }>
}

let deferred: InstallPromptEvent | null = null
const listeners = new Set<(available: boolean) => void>()

function emit() {
  for (const l of listeners) l(deferred !== null)
}

if (typeof window !== 'undefined') {
  window.addEventListener('beforeinstallprompt', (e) => {
    e.preventDefault()
    deferred = e as InstallPromptEvent
    emit()
  })
  // Installed (by our button or the browser's own UI) — the offer is done with.
  window.addEventListener('appinstalled', () => {
    deferred = null
    emit()
  })
}

/** True when a one-tap install is actually possible right now (Chromium only). */
export function canInstall(): boolean {
  return deferred !== null
}

/** Subscribe to availability changes. Returns an unsubscribe function. */
export function onInstallAvailability(fn: (available: boolean) => void): () => void {
  listeners.add(fn)
  return () => listeners.delete(fn)
}

/**
 * Shows the native install dialog. Returns whether the employee accepted.
 * The event is single-use: Chrome will fire a fresh one later if they declined.
 */
export async function promptInstall(): Promise<boolean> {
  if (!deferred) return false
  const e = deferred
  deferred = null
  emit()
  try {
    await e.prompt()
    const { outcome } = await e.userChoice
    return outcome === 'accepted'
  } catch {
    return false
  }
}
