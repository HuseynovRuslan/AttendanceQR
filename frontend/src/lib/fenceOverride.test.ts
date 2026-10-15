import { describe, expect, it } from 'vitest'
import { fenceSelectFrom, requireGeofenceFrom } from './fenceOverride'

describe('the per-person GPS wall means what the select says', () => {
  it('«Radiusdan kənarda girişi rədd et» requires the wall', () => {
    expect(requireGeofenceFrom('on')).toBe(true)
    expect(fenceSelectFrom(true)).toBe('on')
  })

  it('«Rədd etmə — yalnız ölç» takes it away', () => {
    expect(requireGeofenceFrom('off')).toBe(false)
    expect(fenceSelectFrom(false)).toBe('off')
  })

  it('«Filiala görə» follows the branch', () => {
    expect(requireGeofenceFrom('')).toBeNull()
    expect(fenceSelectFrom(null)).toBe('')
    expect(fenceSelectFrom(undefined)).toBe('')
  })

  it('a stored value survives the form unchanged', () => {
    for (const stored of [true, false, null]) expect(requireGeofenceFrom(fenceSelectFrom(stored))).toBe(stored)
  })
})
