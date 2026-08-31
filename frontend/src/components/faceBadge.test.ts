import { describe, expect, it } from 'vitest'
import { faceBadge } from './FaceFlagBadge'

describe('faceBadge', () => {
  it('spells the verdict out in full off the board', () => {
    expect(faceBadge('Ok', 100)?.text).toBe('Uyğun 100%')
    expect(faceBadge('Mismatch', 41)?.text).toBe('Uyğunsuz 41%')
  })

  it('says only the score for a match on the board', () => {
    // The point of the compact form: a column of ticks reads as a column, and the eye finds the row
    // that is not one.
    expect(faceBadge('Ok', 100, true)?.text).toBe('100%')
    expect(faceBadge('Ok', 100, true)?.icon).toBe('✓')
  })

  it('never shortens a verdict somebody has to act on', () => {
    expect(faceBadge('Mismatch', 41, true)?.text).toBe('Uyğunsuz 41%')
    expect(faceBadge('NoFace', null, true)?.text).toBe('Üz yoxdur')
    expect(faceBadge('MultiFace', null, true)?.text).toBe('Çoxlu üz')
  })

  it('keeps the words in the tooltip when it drops them from the pill', () => {
    expect(faceBadge('Ok', 100, true)?.title).toBe('Uyğun — 100%')
  })

  it('has nothing to say about a row that was never checked', () => {
    expect(faceBadge(null)).toBeNull()
    expect(faceBadge('NotChecked')).toBeNull()
  })

  it('drops a score the backend did not compute, rather than printing null%', () => {
    expect(faceBadge('Ok', null)?.text).toBe('Uyğun')
    expect(faceBadge('NoReference', 90)?.text).toBe('Referans yox')
  })
})
