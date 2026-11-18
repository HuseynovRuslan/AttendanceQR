import { describe, expect, it } from 'vitest'
import { namesFreeMode, shiftOptionLabel, shiftParenLabel } from './shiftLabel'

const day = { name: 'Gündüz', shiftStart: '09:00', shiftEnd: '18:00' }
const night = { name: 'Gecə', shiftStart: '22:00', shiftEnd: '06:00', isOvernight: true }
const free = (name: string) => ({ name, isFlexible: true, shiftStart: '00:00', shiftEnd: '23:59' })

describe('shift labels — «Sərbəst iş qrafiki»', () => {
  it('prints hours for a fixed shift, and the moon only when asked', () => {
    expect(shiftOptionLabel(day)).toBe('Gündüz · 09:00–18:00')
    expect(shiftOptionLabel(night, { moon: true })).toBe('Gecə · 22:00–06:00 🌙')
    expect(shiftParenLabel(day)).toBe('Gündüz (09:00–18:00)')
  })

  it('never prints the 00:00–23:59 a free-hours shift is stored with', () => {
    expect(shiftOptionLabel(free('Barmenlər'))).toBe('Barmenlər · sərbəst qrafik')
    expect(shiftParenLabel(free('Barmenlər'))).toBe('Barmenlər (sərbəst qrafik)')
  })

  it('does not say the mode twice when the name already does', () => {
    expect(shiftOptionLabel(free('Sərbəst iş qrafiki'))).toBe('Sərbəst iş qrafiki')
    expect(shiftOptionLabel(free('SƏRBƏST'))).toBe('SƏRBƏST')
    expect(shiftOptionLabel(free('Elastik'))).toBe('Elastik') // a shift still named the old way: no «Elastik · Elastik»
    expect(namesFreeMode('Gündüz')).toBe(false)
  })
})
