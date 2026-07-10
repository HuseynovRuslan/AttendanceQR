import { useEffect, useRef, useState } from 'react'

// An installed iOS web app is suspended, not closed: tapping its icon resumes the page instead of
// reloading it, so a deploy never reaches the employee — and employees do not refresh apps. With no
// service worker to swap the bundle, the app has to notice for itself. /version.json changes on every
// build (see scripts/version.mjs).

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

/** True once a newer build than the one currently running has been published. */
export function useAppUpdate(): boolean {
  const running = useRef<string | null>(null)
  const [updateReady, setUpdateReady] = useState(false)

  useEffect(() => {
    let cancelled = false

    async function check() {
      const id = await fetchBuildId()
      if (cancelled || id === null) return
      // First answer defines what "currently running" means; later answers are compared to it.
      if (running.current === null) running.current = id
      else if (id !== running.current) setUpdateReady(true)
    }

    void check()

    // The resume case is the important one: it is the only moment an employee's PWA comes back to
    // life after a deploy. Polling covers a phone left open on the counter all day.
    const onVisible = () => {
      if (document.visibilityState === 'visible') void check()
    }
    document.addEventListener('visibilitychange', onVisible)
    const timer = setInterval(onVisible, POLL_MS)

    return () => {
      cancelled = true
      document.removeEventListener('visibilitychange', onVisible)
      clearInterval(timer)
    }
  }, [])

  return updateReady
}
