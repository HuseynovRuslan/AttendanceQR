import { describe, expect, it } from 'vitest'
import { isShortMapLink, parseCoords } from './geoLink'

/**
 * Every one of these is a link somebody can actually produce from a phone, which is the point: the
 * admin should not have to know which kind of Google link they copied.
 */
describe('parseCoords', () => {
  it('reads a shared pin (?q=)', () => {
    expect(parseCoords('https://www.google.com/maps?q=40.396690368652344,49.86564254760742&z=17&hl=en'))
      .toEqual({ lat: 40.396690368652344, lng: 49.86564254760742 })
  })

  it('reads the URL bar of an open map (/@)', () => {
    expect(parseCoords('https://www.google.com/maps/@40.4093,49.8671,17z')).toEqual({ lat: 40.4093, lng: 49.8671 })
  })

  it('reads an older share link (?ll=)', () => {
    expect(parseCoords('https://maps.google.com/?ll=40.41,49.85&z=15')).toEqual({ lat: 40.41, lng: 49.85 })
  })

  it('reads a route destination', () => {
    expect(parseCoords('https://www.google.com/maps?daddr=40.42,49.83')).toEqual({ lat: 40.42, lng: 49.83 })
  })

  it('prefers the pin over the viewport on a place page', () => {
    // The two are different points: @ is wherever the map was looking, !3d!4d is the place itself.
    const url = 'https://www.google.com/maps/place/Baku/@40.3,49.7,14z/data=!3m1!4b1!4m5!3m4!1s0x0:0x0!8m2!3d40.396690!4d49.865642'
    expect(parseCoords(url)).toEqual({ lat: 40.39669, lng: 49.865642 })
  })

  it('reads a pair copied straight off the map', () => {
    expect(parseCoords('40.396690, 49.865642')).toEqual({ lat: 40.39669, lng: 49.865642 })
  })

  it('handles a negative pair', () => {
    expect(parseCoords('-33.8688, 151.2093')).toEqual({ lat: -33.8688, lng: 151.2093 })
  })

  it('refuses coordinates outside the world', () => {
    expect(parseCoords('?q=200,400')).toBeNull()
  })

  it('refuses 0,0 — that is a failed parse, not a place', () => {
    expect(parseCoords('https://www.google.com/maps?q=0,0')).toBeNull()
  })

  it('refuses a link with no coordinates in it', () => {
    expect(parseCoords('https://www.google.com/maps/place/Baku')).toBeNull()
  })

  it('does not mistake numbers in a sentence for a location', () => {
    expect(parseCoords('filial 12, mərtəbə 3')).toBeNull()
  })

  it('ignores empty input', () => {
    expect(parseCoords('')).toBeNull()
    expect(parseCoords('   ')).toBeNull()
  })
})

describe('isShortMapLink', () => {
  it('recognises the short links that carry no coordinates', () => {
    expect(isShortMapLink('https://maps.app.goo.gl/abc123')).toBe(true)
    expect(isShortMapLink('https://goo.gl/maps/xyz')).toBe(true)
  })

  it('does not flag a full link', () => {
    expect(isShortMapLink('https://www.google.com/maps?q=40.4,49.8')).toBe(false)
  })
})
