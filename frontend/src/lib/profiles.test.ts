import { describe, expect, it } from 'vitest'
import { MAX_PROFILES, upsert, withoutId, type SavedProfile } from './profiles'

const p = (id: string, name = id, token = `t-${id}`, addedAtMs = 1): SavedProfile => ({
  employeeId: id,
  name,
  token,
  addedAtMs,
})

describe('upsert', () => {
  it('appends a new profile', () => {
    expect(upsert([p('a')], p('b')).map((x) => x.employeeId)).toEqual(['a', 'b'])
  })

  it('replaces the token of somebody already saved rather than listing them twice', () => {
    // The reason this matters: a worker whose PIN was reset is re-added with the same phone number,
    // and two rows with one name — one of them dead — is discovered at the poster.
    const next = upsert([p('a', 'Rəşad', 'old'), p('b')], p('a', 'Rəşad', 'new'))
    expect(next).toHaveLength(2)
    expect(next.find((x) => x.employeeId === 'a')?.token).toBe('new')
  })

  it('keeps a refreshed profile in its original position', () => {
    // A switcher whose rows reorder as people scan is one where the holder taps the wrong name.
    const next = upsert([p('a'), p('b'), p('c')], p('b', 'b', 'fresh'))
    expect(next.map((x) => x.employeeId)).toEqual(['a', 'b', 'c'])
  })

  it('preserves the original addedAtMs when refreshing', () => {
    const next = upsert([p('a', 'a', 'old', 100)], p('a', 'a', 'new', 999))
    expect(next[0].addedAtMs).toBe(100)
  })

  it('drops the oldest entry once the cap is reached', () => {
    const many = Array.from({ length: MAX_PROFILES }, (_, i) => p(`e${i}`))
    const next = upsert(many, p('new'))
    expect(next).toHaveLength(MAX_PROFILES)
    expect(next.some((x) => x.employeeId === 'e0')).toBe(false)
    expect(next[next.length - 1].employeeId).toBe('new')
  })

  it('does not evict anyone when refreshing a full list', () => {
    const many = Array.from({ length: MAX_PROFILES }, (_, i) => p(`e${i}`))
    const next = upsert(many, p('e0', 'e0', 'fresh'))
    expect(next).toHaveLength(MAX_PROFILES)
    expect(next[0].token).toBe('fresh')
  })
})

describe('withoutId', () => {
  it('removes only the named profile', () => {
    expect(withoutId([p('a'), p('b')], 'a').map((x) => x.employeeId)).toEqual(['b'])
  })

  it('is a no-op for an id that is not there', () => {
    expect(withoutId([p('a')], 'zzz')).toHaveLength(1)
  })
})
