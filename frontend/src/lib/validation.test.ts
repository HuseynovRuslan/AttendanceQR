import { describe, expect, it } from 'vitest'
import { isValidEmail, personNameError } from './validation'

describe('isValidEmail', () => {
  it('"@" və ondan sonra nöqtə olanı qəbul edir', () => {
    expect(isValidEmail('ad@sirket.az')).toBe(true)
    expect(isValidEmail('  ad@sirket.az  ')).toBe(true)
  })

  it('yarımçıq e-poçtu qəbul etmir', () => {
    expect(isValidEmail('ad')).toBe(false)
    expect(isValidEmail('ad@sirket')).toBe(false)
    expect(isValidEmail('@sirket.az')).toBe(false)
    expect(isValidEmail('ad@.az')).toBe(false)
    expect(isValidEmail('ad@sirket.')).toBe(false)
  })
})

describe('personNameError', () => {
  it('Azərbaycan hərflərini, defisi və apostrofu qəbul edir', () => {
    expect(personNameError('Ləman', 'Ad')).toBeNull()
    expect(personNameError('Şükür', 'Ad')).toBeNull()
    expect(personNameError('Məmməd-Əli', 'Ad')).toBeNull()
    expect(personNameError("Ma'sumə", 'Ad')).toBeNull()
  })

  it('qısa adı və hərf olmayan simvolu qəbul etmir', () => {
    expect(personNameError(' Ə ', 'Ad')).toBe('Ad ən azı 2 hərf olmalıdır')
    expect(personNameError('Ali2', 'Ad')).toBe('Ad yalnız hərflərdən ibarət olmalıdır')
    expect(personNameError('-Ali', 'Soyad')).toBe('Soyad yalnız hərflərdən ibarət olmalıdır')
  })
})
