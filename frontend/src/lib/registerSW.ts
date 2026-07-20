// Registers the app-shell service worker (see public/sw.js). Production only — in dev the service
// worker would cache the Vite dev bundle and fight HMR. It makes the PWA open with no connection, the
// prerequisite for offline check-in. Network-first HTML keeps it from ever serving a stale bundle
// online, so the existing /version.json update check (useAppUpdate) still drives updates as before.
export function registerServiceWorker(): void {
  if (!import.meta.env.PROD) return
  if (typeof navigator === 'undefined' || !('serviceWorker' in navigator)) return

  window.addEventListener('load', () => {
    // updateViaCache: 'none' → the browser revalidates sw.js against the network on every check, so a
    // new worker is picked up even if the server sent a long cache header for static files.
    navigator.serviceWorker.register('/sw.js', { updateViaCache: 'none' }).catch(() => {
      // Best-effort: a browser that refuses the worker just loses offline support, nothing else breaks.
    })
  })
}
