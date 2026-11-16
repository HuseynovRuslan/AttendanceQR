import { describe, expect, it } from 'vitest'
import { checkOutCardText } from './checkOutCard'

describe('the card after a check-out', () => {
  it('is unchanged for a fixed shift — «Sabaha qədər!»', () => {
    expect(checkOutCardText({ flexible: false, qrless: false, minutes: 485, at: '18:05' })).toEqual({
      title: 'Çıxış qeydə alındı',
      detail: '8 saat 5 dəqiqə işlədiniz',
      note: 'Sabaha qədər!',
    })
    expect(checkOutCardText({ flexible: false, qrless: true, at: '18:05' }).detail).toBe('Saat 18:05')
  })

  it('never sends a flexible worker home until tomorrow — the next session may start today', () => {
    const card = checkOutCardText({ flexible: true, qrless: false, minutes: 6, at: '21:53' })
    expect(card.title).toBe('Çıxışınız qeydə alındı')
    expect(card.detail).toBe('Bu sessiyada 0 saat 6 dəqiqə işlədiniz')
    expect(card.note).toBe('Yenidən işə başlasanız, giriş üçün yenidən skan edin.')
    expect(JSON.stringify(card)).not.toMatch(/Sabah/)
  })

  it('says «Giriş et» to a flexible worker who checks in without a poster', () => {
    expect(checkOutCardText({ flexible: true, qrless: true, at: '21:53' }).note)
      .toBe('Yenidən işə başlasanız, «Giriş et»ə toxunun.')
  })
})
