import { describe, expect, it } from 'vitest'
import { matchesSearch, searchFold } from './searchFold'

// The same table as the server's AzerbaijaniSearchTests: the browser and the database must agree.
describe('Azerbaijani search', () => {
  it.each([
    ['İsmayılov Anar', 'İsmayılov'],
    ['İsmayılov Anar', 'ismayılov'],
    ['İsmayılov Anar', 'İSMAYILOV'],
    ['İsmayılov Anar', 'Ismayilov'],
    ['İsmayılov Anar', 'ismayilov'],
    ['İsmayılov Anar', 'ISMAYILOV'],
    ['Ilqar Qasımov', 'ılqar'],
    ['Ilqar Qasımov', 'İlqar'],
    ['Əliyev Şükür', 'ƏLİYEV'],
    ['Əliyev Şükür', 'ŞÜKÜR'],
    ['Çingiz Ağayev', 'çİngİz ağa'],
    ['Göyçə Ünal', 'GÖYÇƏ ÜNAL'],
    ['İsmayılov', 'i̇smayılov'],
  ])('«%s» is found by «%s»', (name, typed) => {
    expect(matchesSearch(typed, name)).toBe(true)
  })

  it.each([
    ['Əliyev', 'eliyev'],
    ['Şükür', 'sukur'],
    ['Çingiz', 'cingiz'],
  ])('«%s» is not «%s»: different letters stay different', (name, typed) => {
    expect(matchesSearch(typed, name)).toBe(false)
  })

  it('matches the server rule letter for letter', () => {
    expect(searchFold('İIıƏŞÇĞÖÜ̇')).toBe('iiiəşçğöü')
    expect(searchFold('A1B2C3D4')).toBe('a1b2c3d4')
    expect(searchFold(null)).toBe('')
  })

  it('an empty search matches everything, and any field may match', () => {
    expect(matchesSearch('  ', 'Kimsə')).toBe(true)
    expect(matchesSearch('park', 'Aynur', 'Bülbül parkı')).toBe(true)
    expect(matchesSearch('zzz', 'Aynur', null, undefined)).toBe(false)
  })
})
