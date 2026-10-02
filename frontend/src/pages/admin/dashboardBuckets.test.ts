import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'
import { countToday } from './todayCounts'

/**
 * The panel and the board must bucket the day the same way — checked at the one place it can drift.
 *
 * It drifted once already, and silently. `DashboardPage` carried its own copy of the rule twice: a
 * counting loop that put every «Sahədə» row in «Tamamlayıb» whatever the visit was doing, and a
 * `rowInBucket` that matched those rows to no bucket at all. On 02.10.2026, with 54 people still
 * standing on sites, the panel said 491 at work and 55 finished while the board said 545 and 1 — the
 * same 546 people, split differently — and pressing the «Tamamlayıb 55» pill opened a list of one.
 *
 * Nothing on screen says which number is wrong, and nobody re-reads a tile they have seen every
 * morning. So the rule now lives only in todayCounts.ts and this guards the two ways back:
 * re-introducing a copy, and adding a bucket the panel has no pill for.
 *
 * A source test is unusual. A number that quietly disagrees with the screen next to it is worse.
 */
const SOURCE = readFileSync(fileURLToPath(new URL('./DashboardPage.tsx', import.meta.url)), 'utf8')

/**
 * The three statuses only the bucketing rule cares about.
 *
 * `Incomplete`, `Absent` and the leave statuses appear elsewhere in this screen for honest reasons —
 * the map plots who is physically standing at a site, the branch ranking decides who was expected.
 * `OnTime`, `Late` and `Field` have no other use, so one of them reappearing means somebody has
 * started writing the bucketing out again.
 */
const BUCKETING_ONLY = ['OnTime', 'Late', 'Field']

describe('DashboardPage bucketing', () => {
  it.each(BUCKETING_ONLY)('does not decide %s for itself — todayCounts.ts owns that', (status) => {
    expect(
      SOURCE.includes(`'${status}'`),
      `DashboardPage.tsx refers to the '${status}' status. Bucketing belongs to bucketOf() in `
      + 'todayCounts.ts; call it instead of restating the rule here.',
    ).toBe(false)
  })

  it('asks todayCounts for both the counts and the pill membership', () => {
    expect(SOURCE).toMatch(/import\s*\{[^}]*\bbucketOf\b[^}]*\}\s*from\s*'\.\/todayCounts'/)
    expect(SOURCE).toMatch(/import\s*\{[^}]*\bcountToday\b[^}]*\}\s*from\s*'\.\/todayCounts'/)
    expect(SOURCE).toContain('countToday(rows)')
  })

  it('gives every bucket a pill, so a new one cannot land with nowhere to show', () => {
    const literal = SOURCE.match(/PILL_BUCKET:[^=]*=\s*\{([\s\S]*?)\}/)
    expect(literal, 'PILL_BUCKET must stay a plain object literal this test can read').not.toBeNull()

    const mapped = new Set([...literal![1].matchAll(/:\s*'([A-Za-z]+)'/g)].map((m) => m[1]))
    // Every key countToday can return a number for is a bucket the day can land in.
    const all = Object.keys(countToday([]))

    expect([...all].sort()).toEqual([...mapped].sort())
  })
})
