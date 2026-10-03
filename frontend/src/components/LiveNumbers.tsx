import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import { COMPANY_TZ } from '../lib/format'

/**
 * Numbers that move without moving the page around them.
 *
 * The dashboard counted its headline numbers up with React state: every animation frame set five
 * values, and every set re-rendered the whole panel — the map, the activity list, the branch tables —
 * some forty-five times in the 750 ms it took the numbers to arrive, on every opening and on every
 * 30-second poll that changed one. A clock ticking in the same component re-rendered all of it once a
 * second besides. On 03.10.2026, on a 12-core machine, opening the panel produced frames of up to
 * 100 ms and four long tasks; on the office PCs it is used from, that is the stutter people describe.
 *
 * Here only the moving part moves: the count-up writes its number straight into its own element each
 * frame, and the clock re-renders a timestamp. Nothing above either of them renders again.
 */

const reduceMotion = () =>
  typeof window !== 'undefined' && window.matchMedia?.('(prefers-reduced-motion: reduce)').matches === true

/** easeOutCubic: quick to start, settling gently on the value. */
export const easeOutCubic = (p: number) => 1 - Math.pow(1 - p, 3)

/** `value`, eased in from whatever is on screen — from 0 the first time — or set at once when the
 *  reader has asked for reduced motion. */
export function CountUp({ value, duration = 750, suffix = '' }: { value: number; duration?: number; suffix?: string }) {
  const ref = useRef<HTMLSpanElement>(null)
  const shown = useRef(0)

  // Before the first paint, so the element is never seen empty.
  useLayoutEffect(() => {
    if (ref.current && ref.current.textContent === '') ref.current.textContent = `${Math.round(shown.current)}${suffix}`
  }, [suffix])

  useEffect(() => {
    const el = ref.current
    if (!el) return
    const write = (v: number) => {
      shown.current = v
      el.textContent = `${Math.round(v)}${suffix}`
    }
    if (reduceMotion() || duration <= 0) {
      write(value)
      return
    }
    const from = shown.current
    const start = performance.now()
    let raf = 0
    const tick = (now: number) => {
      const p = Math.min(1, (now - start) / duration)
      write(from + (value - from) * easeOutCubic(p))
      if (p < 1) raf = requestAnimationFrame(tick)
    }
    raf = requestAnimationFrame(tick)
    return () => cancelAnimationFrame(raf)
  }, [value, duration, suffix])

  // Childless on purpose: React must never own this text, or a re-render of the parent would write its
  // own idea of the number over the one the animation is drawing.
  return <span ref={ref} />
}

/** The ticking clock — its own component, so the second hand re-renders a timestamp, not a page. */
export function LiveClock({ className }: { className?: string }) {
  const [now, setNow] = useState(() => new Date())
  useEffect(() => {
    const t = setInterval(() => setNow(new Date()), 1000)
    return () => clearInterval(t)
  }, [])
  return (
    <span className={className}>
      {now.toLocaleTimeString('az-AZ', { hour: '2-digit', minute: '2-digit', second: '2-digit', timeZone: COMPANY_TZ })}
    </span>
  )
}
