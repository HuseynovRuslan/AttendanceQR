import { describe, expect, it } from 'vitest'
import { parseStaticQrValidity, staticQrValidityQuery } from './staticQrValidity'

describe('static QR validity', () => {
  it('keeps an exact day duration inside the supported range', () => {
    expect(parseStaticQrValidity('days', '60')).toEqual({ validityDays: 60 })
    expect(parseStaticQrValidity('days', ' 3650 ')).toEqual({ validityDays: 3650 })
  })

  it('rejects empty, fractional and out-of-range durations', () => {
    expect(parseStaticQrValidity('days', '')).toBeNull()
    expect(parseStaticQrValidity('days', '1.5')).toBeNull()
    expect(parseStaticQrValidity('days', '0')).toBeNull()
    expect(parseStaticQrValidity('days', '3651')).toBeNull()
  })

  it('does not attach a duration to a permanent QR', () => {
    expect(parseStaticQrValidity('permanent', '')).toEqual({ permanent: true })
  })

  it('uses the API query contract for both choices', () => {
    expect(staticQrValidityQuery({ validityDays: 90 })).toBe('validityDays=90')
    expect(staticQrValidityQuery({ permanent: true })).toBe('permanent=true')
  })
})
