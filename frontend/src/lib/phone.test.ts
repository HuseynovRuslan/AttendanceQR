import { describe, expect, it } from 'vitest'
import { cleanPhone, formatPhone, isValidPhone, onlyDigits, phoneError } from './phone'

/**
 * Telefon sahəsinin qaydaları. Burada səhv olsa, işçi nömrəsini yaza bilməz və işə gəldiyini qeyd edə bilməz,
 * ona görə yapışdırma, ölkə kodu və artıq rəqəm halları ayrıca yoxlanılır.
 */
describe('onlyDigits', () => {
  it('rəqəm olmayan hər şeyi silir', () => {
    expect(onlyDigits('+994 (50) 123-45-67a')).toBe('994501234567')
  })
})

describe('cleanPhone — local', () => {
  it('+994 ilə yapışdırılan nömrəni 0 ilə başlayan formaya çevirir', () => {
    expect(cleanPhone('+994 50 123 45 67', 'local')).toBe('0501234567')
  })

  it('994 ilə başlayanı da çevirir', () => {
    expect(cleanPhone('994501234567', 'local')).toBe('0501234567')
  })

  it('"+994 050..." yazılışında ikinci 0 qoymur', () => {
    expect(cleanPhone('+994 050 123 45 67', 'local')).toBe('0501234567')
  })

  it('11-ci rəqəmi kəsir', () => {
    expect(cleanPhone('05012345678', 'local')).toBe('0501234567')
  })

  it('hərfi buraxmır', () => {
    expect(cleanPhone('050a', 'local')).toBe('050')
  })

  it('0994 (Bakcell 099 + 4) nömrəsinə toxunmur', () => {
    expect(cleanPhone('0994123456', 'local')).toBe('0994123456')
  })
})

describe('cleanPhone — international', () => {
  it('+994 ilə yapışdırılanı 9 rəqəmə endirir', () => {
    expect(cleanPhone('+994 50 123 45 67', 'international')).toBe('501234567')
  })

  it('yerli 0-ı silir', () => {
    expect(cleanPhone('050 123 45 67', 'international')).toBe('501234567')
  })

  it('99 4XX XX XX nömrəsini yazmaq mümkündür (994-ü vaxtından əvvəl silmir)', () => {
    expect(cleanPhone('994', 'international')).toBe('994')
    expect(cleanPhone('994123456', 'international')).toBe('994123456')
  })

  it('10-cu rəqəmi kəsir', () => {
    expect(cleanPhone('5012345678', 'international')).toBe('501234567')
  })
})

describe('formatPhone', () => {
  it('local nömrəni 3-3-2-2 qruplarla göstərir', () => {
    expect(formatPhone('0501234567', 'local')).toBe('050 123 45 67')
  })

  it('international nömrəni 2-3-2-2 qruplarla göstərir', () => {
    expect(formatPhone('501234567', 'international')).toBe('50 123 45 67')
  })

  it('yarımçıq nömrənin sonuna boşluq qoymur', () => {
    expect(formatPhone('050', 'local')).toBe('050')
    expect(formatPhone('0501', 'local')).toBe('050 1')
  })
})

describe('isValidPhone və phoneError', () => {
  it('local: 10 rəqəm və 0 ilə başlamalıdır', () => {
    expect(isValidPhone('0501234567', 'local')).toBe(true)
    expect(isValidPhone('050123456', 'local')).toBe(false)
    expect(phoneError('050123', 'local')).toBe('Nömrə 10 rəqəm olmalıdır')
    expect(phoneError('5012345678', 'local')).toBe('Nömrə 0 ilə başlamalıdır')
  })

  it('international: 9 rəqəm olmalıdır', () => {
    expect(isValidPhone('501234567', 'international')).toBe(true)
    expect(phoneError('5012', 'international')).toBe('Nömrə 9 rəqəm olmalıdır')
    expect(phoneError('501234567', 'international')).toBeNull()
  })
})
