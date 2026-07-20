/* QRLog service worker — app-shell offline support.
 *
 * The prerequisite for offline check-in: without this the PWA cannot even open with no connection
 * (the browser shows "you're offline"). Strategy chosen to avoid the stale-bundle trap that kept a
 * service worker out until now:
 *
 *   - Navigation (HTML): NETWORK-FIRST — online always serves the freshest index.html (so a deploy is
 *     picked up immediately); only with no connection does it fall back to the cached shell.
 *   - Hashed build assets + static files: cache-first — their names change every build, so a cached
 *     copy is never stale.
 *   - /version.json and everything cross-origin (the API on api.qrlog.az, photos on R2): never touched.
 *     They go straight to the network, so the update check (useAppUpdate) and every auth/tenant-scoped
 *     request always see the truth and nothing sensitive is ever cached.
 *
 * Bump CACHE to force-invalidate on a breaking service-worker change.
 */
const CACHE = 'qrlog-shell-v1'
const SHELL = '/index.html'

self.addEventListener('install', (event) => {
  // Take over as soon as installed rather than waiting for every tab to close.
  self.skipWaiting()
  event.waitUntil(caches.open(CACHE).then((c) => c.add(SHELL)).catch(() => {}))
})

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches
      .keys()
      .then((keys) => Promise.all(keys.filter((k) => k !== CACHE).map((k) => caches.delete(k))))
      .then(() => self.clients.claim()),
  )
})

self.addEventListener('fetch', (event) => {
  const req = event.request
  if (req.method !== 'GET') return

  const url = new URL(req.url)

  // Only the app shell (same origin). The API is another subdomain and R2 another host — leave those
  // entirely to the network so nothing auth/tenant-scoped is ever cached.
  if (url.origin !== self.location.origin) return

  // The staleness check must always read the real server value — never intercept it.
  if (url.pathname === '/version.json') return

  // App navigation: network-first, cached shell as the offline fallback.
  if (req.mode === 'navigate') {
    event.respondWith(
      fetch(req)
        .then((res) => {
          const copy = res.clone()
          caches.open(CACHE).then((c) => c.put(SHELL, copy)).catch(() => {})
          return res
        })
        .catch(() => caches.match(SHELL).then((r) => r || Response.error())),
    )
    return
  }

  // Hashed build assets + static files: cache-first (safe — filenames change per build).
  if (
    url.pathname.startsWith('/assets/') ||
    /\.(?:js|css|woff2?|ttf|png|jpe?g|svg|ico|webmanifest)$/.test(url.pathname)
  ) {
    event.respondWith(
      caches.match(req).then(
        (cached) =>
          cached ||
          fetch(req).then((res) => {
            if (res.ok) {
              const copy = res.clone()
              caches.open(CACHE).then((c) => c.put(req, copy)).catch(() => {})
            }
            return res
          }),
      ),
    )
    return
  }

  // Anything else: default network handling (no respondWith).
})
