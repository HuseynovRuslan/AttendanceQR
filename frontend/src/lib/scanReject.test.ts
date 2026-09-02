import { describe, expect, it } from 'vitest'
import { isPermanentDeviceReject } from './scanReject'

describe('isPermanentDeviceReject', () => {
  it('a shared-phone refusal is permanent — the real 02.09 loop', () => {
    // 75 retries at Qafur Məmmədov Parkı came from treating this as a keep-and-retry account state.
    expect(isPermanentDeviceReject(403, 'SharedDeviceNotAllowed')).toBe(true)
  })

  it('every device-binding refusal is permanent for this phone', () => {
    expect(isPermanentDeviceReject(403, 'DeviceMismatch')).toBe(true)
    expect(isPermanentDeviceReject(403, 'NoDeviceBound')).toBe(true)
    expect(isPermanentDeviceReject(403, 'DeviceAccountLimit')).toBe(true)
  })

  it('a PIN-state 403 is NOT dropped — it clears the moment they set a PIN, so the scan must survive', () => {
    // The whole reason the drainer keeps 403s. A device code is the exception, not the rule.
    expect(isPermanentDeviceReject(403, 'MustSetPin')).toBe(false)
    expect(isPermanentDeviceReject(403, undefined)).toBe(false)
  })

  it('only a 403 counts — a 401 is an expired token, handled by the account path', () => {
    expect(isPermanentDeviceReject(401, 'SharedDeviceNotAllowed')).toBe(false)
  })

  it('a 4xx that is not 403 is not this function\'s business — the definitive-4xx path already drops it', () => {
    expect(isPermanentDeviceReject(400, 'OutsideRadius')).toBe(false)
    expect(isPermanentDeviceReject(409, 'AlreadyCompleted')).toBe(false)
  })
})
