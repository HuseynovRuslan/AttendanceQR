import { describe, expect, it } from 'vitest'
import { deviceRefusalCopy } from './deviceRefusal'

describe('deviceRefusalCopy', () => {
  it('tells someone whose allowance ran out that the phone is forgetting the app, and how to get back in', () => {
    const card = deviceRefusalCopy('DeviceMismatch', 'DeviceBindLimit')

    expect(card.title).toBe('Telefonunuz tətbiqi yadda saxlamır')
    expect(card.showDeviceChangeLink).toBe(true)
    // The way back (the request) and the lasting fix (one way of opening the app) are both on the card.
    expect(card.note).toContain('Bu mənim yeni telefonumdur')
    expect(card.note).toContain('ana ekrandakı ikondan')
  })

  it('says a removed phone was removed, and still offers the request', () => {
    const card = deviceRefusalCopy('DeviceMismatch', 'DeviceRevoked')

    expect(card.title).toBe('Bu telefon hesabınızdan çıxarılıb')
    expect(card.showDeviceChangeLink).toBe(true)
  })

  it('lets the cause decide even when the account has no phone left at all', () => {
    // Eviction can leave an account with no active phone; the reason it was refused is still the cause.
    expect(deviceRefusalCopy('NoDeviceBound', 'DeviceBindLimit').title).toBe('Telefonunuz tətbiqi yadda saxlamır')
  })

  it('keeps the old cards word for word when the server sends no cause', () => {
    // Strict mode, and every server from before the cause existed. Nothing new to say, so nothing changes.
    expect(deviceRefusalCopy('DeviceMismatch', undefined)).toEqual({
      title: 'Bu cihaz hesabınıza bağlı deyil',
      note: 'Yenidən skan etmək kömək etməyəcək.',
      showDeviceChangeLink: true,
    })
    expect(deviceRefusalCopy('NoDeviceBound', null)).toEqual({
      title: 'Cihaz hesabınıza bağlı deyil',
      detail: 'Admin ilə əlaqə saxlayın.',
      showDeviceChangeLink: false,
    })
  })

  it('falls back to the card for the code when the cause is one this copy has never heard of', () => {
    // A newer server may name a cause an older bundle does not know; that bundle must still say something true.
    expect(deviceRefusalCopy('DeviceMismatch', 'SomethingNewer').title).toBe('Bu cihaz hesabınıza bağlı deyil')
  })
})
