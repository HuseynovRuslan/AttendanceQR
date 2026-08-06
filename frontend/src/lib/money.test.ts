import { describe, expect, it } from 'vitest'
import { parseMoney, moneyInputFilter } from './money'

// parseMoney guards the two operator money inputs (plan price override + "Ödənildi" amount). The whole
// point is that a mistyped price fails LOUDLY (null → the caller shows an error) instead of silently
// becoming NaN→null (wipe the price) or 0 (paid for nothing) or 1.25 when 1250 was meant.
describe('parseMoney', () => {
  it('accepts plain and 1–2 decimal amounts', () => {
    expect(parseMoney('1250')).toBe(1250)
    expect(parseMoney('300.5')).toBe(300.5)
    expect(parseMoney('300.50')).toBe(300.5)
    expect(parseMoney('300,50')).toBe(300.5) // comma decimal
    expect(parseMoney('0')).toBe(0)          // a comped tenant
    expect(parseMoney('  42 ')).toBe(42)     // trimmed
  })

  it('rejects empty and whitespace (never silently 0)', () => {
    expect(parseMoney('')).toBeNull()
    expect(parseMoney('   ')).toBeNull()
  })

  it('rejects ambiguous thousands + malformed (never a silent mis-scale)', () => {
    expect(parseMoney('1.250')).toBeNull() // 3 decimals = thousands intent, ambiguous → reject
    expect(parseMoney('1.2.3')).toBeNull()
    expect(parseMoney('1.234,50')).toBeNull()
    expect(parseMoney('abc')).toBeNull()
    expect(parseMoney('-5')).toBeNull()
  })
})

describe('moneyInputFilter', () => {
  it('keeps digits and a single decimal point', () => {
    expect(moneyInputFilter('12a3')).toBe('123')
    expect(moneyInputFilter('1,5')).toBe('1.5')
    expect(moneyInputFilter('1.2.3')).toBe('1.23') // second dot dropped, no NaN can form
    expect(moneyInputFilter('300')).toBe('300')
  })
})
