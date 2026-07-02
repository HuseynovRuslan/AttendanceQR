import { useEffect, useRef } from 'react'

/**
 * Calls `callback` immediately, then every `intervalMs`. The latest callback is always used
 * (no stale closures), and the interval is cleared on unmount.
 */
export function usePolling(callback: () => void | Promise<void>, intervalMs: number) {
  const saved = useRef(callback)
  useEffect(() => {
    saved.current = callback
  })

  useEffect(() => {
    let active = true
    const tick = () => {
      if (active) void saved.current()
    }
    tick()
    const id = setInterval(tick, intervalMs)
    return () => {
      active = false
      clearInterval(id)
    }
  }, [intervalMs])
}
