import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'
import { easeOutCubic } from '../../components/LiveNumbers'

/**
 * The panel must not re-render itself to make a number move.
 *
 * It did: five count-ups held their frames as state in DashboardPage, so every animation frame
 * re-rendered the whole panel — map, activity list, branch tables — about forty-five times per
 * opening and per changed poll, and a clock in the same component re-rendered it every second. On
 * 03.10.2026 opening it produced 100 ms frames on a fast machine; on an office PC that is a stutter.
 *
 * The moving parts now live in components/LiveNumbers, which update only themselves. These are the
 * two ways back, and nothing on screen would say that either had happened.
 */
const SOURCE = readFileSync(fileURLToPath(new URL('./DashboardPage.tsx', import.meta.url)), 'utf8')

describe('DashboardPage animation', () => {
  it('keeps no per-frame count-up state of its own', () => {
    expect(SOURCE, 'count numbers up with <CountUp>, which writes its own text and re-renders nothing')
      .not.toMatch(/useCountUp/)
    expect(SOURCE).toMatch(/import\s*\{[^}]*\bCountUp\b[^}]*\}\s*from\s*'\.\.\/\.\.\/components\/LiveNumbers'/)
  })

  it('keeps no per-second clock state of its own', () => {
    expect(SOURCE, 'tick the clock in <LiveClock>, so a second re-renders a timestamp and not the panel')
      .not.toMatch(/setInterval\(/)
    expect(SOURCE).toMatch(/<LiveClock\b/)
  })
})

describe('easeOutCubic', () => {
  it('runs from 0 to 1 and is most of the way there by halfway', () => {
    expect(easeOutCubic(0)).toBe(0)
    expect(easeOutCubic(1)).toBe(1)
    expect(easeOutCubic(0.5)).toBeGreaterThan(0.85)
  })
})
