import { describe, expect, it } from 'vitest'
import { looksApproximate, POOR_ACCURACY_METERS } from './geo'

/**
 * Əhməyeva Tünzalə, Dədə Qorqud Parkı, 25.09: eight refusals, every one «±2000 m». That is not a weak
 * signal — it is Android's approximate-location permission, which answers 2000 m from anywhere. The
 * app used to tell her to go outside and wait.
 */
describe('telling a blurred fix from a weak one', () => {
  it('recognises Android approximate location (±2000 m)', () => {
    expect(looksApproximate(2000)).toBe(true)
  })

  it('leaves an ordinary weak fix alone — that one IS helped by open sky', () => {
    expect(looksApproximate(120)).toBe(false)
    expect(looksApproximate(900)).toBe(false)
    expect(looksApproximate(POOR_ACCURACY_METERS + 1)).toBe(false)
  })

  it('says nothing when there is no reading', () => {
    expect(looksApproximate(null)).toBe(false)
    expect(looksApproximate(undefined)).toBe(false)
  })
})
