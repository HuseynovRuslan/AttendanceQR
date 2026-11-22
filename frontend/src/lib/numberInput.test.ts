import { describe, expect, it } from 'vitest'
import { parseCount, ungroup } from './numberInput'

describe('ungroup', () => {
  it('drops thousands spaces of any kind', () => {
    expect(ungroup('1 200')).toBe('1200')
    expect(ungroup('12 500,50')).toBe('12500,50')
    expect(ungroup('1 250')).toBe('1250') // the no-break space numberFormat prints
    expect(ungroup('1 250')).toBe('1250')
    expect(ungroup('  42 ')).toBe('42')
  })

  it('refuses a space where a thousands separator cannot be', () => {
    expect(ungroup('850 5')).toBeNull()
    expect(ungroup('1 20')).toBeNull()
    expect(ungroup('1 2000')).toBeNull()
  })

  it('leaves text with no space for the caller to judge', () => {
    expect(ungroup('abc')).toBe('abc')
    expect(ungroup('')).toBe('')
  })
})

describe('parseCount', () => {
  it('reads a whole count, spaces and all', () => {
    expect(parseCount('12')).toBe(12)
    expect(parseCount('0')).toBe(0)
    expect(parseCount('1 200')).toBe(1200)
  })

  it('refuses what is not a whole count — never a silent 0 or a rounded guess', () => {
    // A type="number" field read every one of these as "" or NaN, and the branch form turned that into
    // 0 — which clears the headcount.
    expect(parseCount('')).toBeNull()
    expect(parseCount('12,5')).toBeNull()
    expect(parseCount('12.5')).toBeNull()
    expect(parseCount('-3')).toBeNull()
    expect(parseCount('on iki')).toBeNull()
    expect(parseCount('99999999999999999999')).toBeNull()
  })
})
