import { useEffect, useState } from 'react'

// An installed iOS web app is suspended, not closed: tapping its icon resumes the page instead of
// reloading it, so a deploy never reaches the employee — and employees do not refresh apps. With no
// service worker to swap the bundle, the app has to notice for itself. /version.json changes on every
// build (scripts/version.mjs) and is served no-store; __BUILD_ID__ is the id of the bundle currently
// executing (vite.config.ts).

const POLL_MS = 5 * 60_000

async function fetchBuildId(): Promise<string | null> {
  try {
    const res = await fetch('/version.json', { cache: 'no-store' })
    if (!res.ok) return null
    const data = (await res.json()) as { buildId?: string }
    return data.buildId ?? null
  } catch {
    // Offline or mid-deploy — say nothing rather than nag.
    return null
  }
}

/** The build id published on the server, once it differs from the one running here. */
export function useAppUpdate(): string | null {
  const [newBuildId, setNewBuildId] = useState<string | null>(null)

  useEffect(() => {
    // 'dev' means the file was never generated (vite dev server) — nothing to compare against.
    if (!__BUILD_ID__ || __BUILD_ID__ === 'dev') return

    let cancelled = false

    async function check() {
      const published = await fetchBuildId()
      // Compared against the id COMPILED INTO this bundle, never against the first answer the server
      // gave: a stale bundle that adopted the server's id as its own would never see itself as old.
      if (!cancelled && published !== null && published !== __BUILD_ID__) setNewBuildId(published)
    }

    void check()

    // Resume is the moment that matters — it is the only time an employee's suspended PWA comes back
    // to life after a deploy. `pageshow` also covers a back/forward-cache restore, which fires no
    // visibilitychange at all.
    const onWake = () => {
      if (document.visibilityState === 'visible') void check()
    }
    document.addEventListener('visibilitychange', onWake)
    window.addEventListener('pageshow', onWake)
    window.addEventListener('focus', onWake)
    const timer = setInterval(onWake, POLL_MS)

    return () => {
      cancelled = true
      document.removeEventListener('visibilitychange', onWake)
      window.removeEventListener('pageshow', onWake)
      window.removeEventListener('focus', onWake)
      clearInterval(timer)
    }
  }, [])

  return newBuildId
}
