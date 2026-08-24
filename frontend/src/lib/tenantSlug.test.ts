import { describe, expect, it } from 'vitest'
import { slugify, withSuffix } from './tenantSlug'

/**
 * These rules are invisible in the product, which is the whole reason to pin them here.
 *
 * The operator never sees the label and has no field to correct it in, so anything this function gets
 * wrong surfaces as "Yaradılmadı" on a form with nothing wrong on it — in front of a paying customer.
 * The server takes 2–20 characters starting with a letter or digit; every case below is one of the
 * ways a real company name walks off that edge.
 */
describe('slugify', () => {
  it('folds Azerbaijani letters rather than dropping them', () => {
    // "Şirkət" losing its ş and ə would leave "irkt" — a label that means nothing to anyone reading
    // an audit row later.
    expect(slugify('Yeni Şirkət MMC')).toBe('yeni-sirket-mmc')
    expect(slugify('Gülağa Çörəkçi')).toBe('gulaga-corekci')
    expect(slugify('İpək Yolu')).toBe('ipek-yolu')
  })

  it('turns punctuation and runs of spaces into single dashes', () => {
    expect(slugify('  Green   Garden  ')).toBe('green-garden')
    expect(slugify('A&B — "Xidmət", MMC')).toBe('a-b-xidmet-mmc')
  })

  it('never starts or ends with a dash', () => {
    expect(slugify('«CleanFix»')).toBe('cleanfix')
    expect(slugify('...EastCaf...')).toBe('eastcaf')
  })

  it('stops at twenty characters without leaving a trailing dash', () => {
    // The cut can land exactly on a separator; "beynelxalq-" is not a label anyone would type.
    // A hard cut at twenty would give "beynelxalq-abadliq-v"; the last whole word wins instead.
    const long = slugify('Beynəlxalq Abadlıq və Xidmət Şirkəti')
    expect(long.length).toBeLessThanOrEqual(20)
    expect(long.startsWith('-')).toBe(false)
    expect(long.endsWith('-')).toBe(false)
    expect(long).toBe('beynelxalq-abadliq')

    // One word longer than the limit has no boundary to step back to, so the hard cut stands.
    expect(slugify('Azerbaycanbeynelxalqsirket')).toBe('azerbaycanbeynelxalq')
  })

  it('gives back nothing when the name cannot make a label', () => {
    // Empty is the honest answer — the caller substitutes. Returning a one-character or empty label
    // would be rejected by the server on a form the operator cannot fix.
    expect(slugify('')).toBe('')
    expect(slugify('   ')).toBe('')
    expect(slugify('—')).toBe('')
    expect(slugify('A')).toBe('')
    expect(slugify('日本')).toBe('')
  })

  it('leaves a name that is already a label alone', () => {
    expect(slugify('greengarden')).toBe('greengarden')
    expect(slugify('bax')).toBe('bax')
  })
})

describe('withSuffix', () => {
  it('numbers the second company of the same name', () => {
    expect(withSuffix('green-garden', 2)).toBe('green-garden-2')
  })

  it('keeps the counter inside the twenty-character limit', () => {
    // The counter is what resolves the clash, so it is the part that must survive the trim — cutting
    // it off would produce the same taken label again and loop.
    const s = withSuffix('beynelxalq-abadliq', 10)
    expect(s.length).toBeLessThanOrEqual(20)
    expect(s.endsWith('-10')).toBe(true)
  })

  it('does not leave a double dash where it trims', () => {
    expect(withSuffix('abcdefghij-klmnopqr', 3)).toBe('abcdefghij-klmnopq-3')
    expect(withSuffix('abcdefghijklmnopq-', 4)).toBe('abcdefghijklmnopq-4')
  })

  it('produces something the server accepts, for every counter it can reach', () => {
    // Twenty attempts is what the form makes before giving up; every one of them has to be valid.
    const format = /^[a-z0-9][a-z0-9-]{1,19}$/
    for (let n = 2; n <= 20; n++) {
      expect(withSuffix('beynelxalq-abadliq', n)).toMatch(format)
      expect(withSuffix('ab', n)).toMatch(format)
    }
  })
})
