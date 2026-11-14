import { describe, expect, it } from 'vitest'
import { formatNumber, formatPercent } from './numberFormat'
import { formatMoney } from './money'

// The whole reason this file exists: Chrome has no Azerbaijani number data, so toLocaleString('az-AZ')
// printed «1,000.00» on every phone while Node (where tests run) printed «1.000,00». These tests pin the
// hand-placed separators, so neither runtime's locale data can change what a reader sees.
const S = ' ' // the thousands separator is a no-break space

describe('formatNumber', () => {
  it('groups thousands with a no-break space and uses a decimal comma', () => {
    expect(formatNumber(1000, 2)).toBe(`1${S}000,00`)
    expect(formatNumber(1250.5, 2)).toBe(`1${S}250,50`)
    expect(formatNumber(1234567.891, 2)).toBe(`1${S}234${S}567,89`)
    expect(formatNumber(999, 2)).toBe('999,00')
    expect(formatNumber(0, 2)).toBe('0,00')
  })

  it('drops trailing zeros only when asked', () => {
    expect(formatNumber(160, 1, { trim: true })).toBe('160')
    expect(formatNumber(91.3, 1, { trim: true })).toBe('91,3')
    expect(formatNumber(91.3, 1)).toBe('91,3')
    expect(formatNumber(160, 1)).toBe('160,0')
  })

  it('keeps the sign, but never prints «-0»', () => {
    expect(formatNumber(-1500, 2)).toBe(`-1${S}500,00`)
    expect(formatNumber(-0.001, 2)).toBe('0,00')
  })

  it('shows a dash for a value that is not a number', () => {
    expect(formatNumber(Number.NaN, 2)).toBe('—')
    expect(formatNumber(Number.POSITIVE_INFINITY)).toBe('—')
  })
})

describe('formatPercent', () => {
  it('writes one decimal at most, with a comma', () => {
    expect(formatPercent(92.2)).toBe('92,2%')
    expect(formatPercent(93.33333)).toBe('93,3%')
    expect(formatPercent(100)).toBe('100%')
    expect(formatPercent(99.96)).toBe('100%')
    expect(formatPercent(0)).toBe('0%')
  })

  it('shows a dash, not 0%, when there is no value', () => {
    expect(formatPercent(null)).toBe('—')
    expect(formatPercent(undefined)).toBe('—')
  })
})

describe('formatMoney', () => {
  it('is always two decimals, grouped, with ₼ that cannot wrap away from the amount', () => {
    expect(formatMoney(1000)).toBe(`1${S}000,00${S}₼`)
    expect(formatMoney(300.5)).toBe(`300,50${S}₼`)
    expect(formatMoney(0)).toBe(`0,00${S}₼`)
  })
})
