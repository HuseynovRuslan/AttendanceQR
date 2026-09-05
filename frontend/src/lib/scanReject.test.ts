import { describe, expect, it } from 'vitest'
import { isPermanentDeviceReject, isPermanentQueuedReject } from './scanReject'

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

describe('isPermanentQueuedReject', () => {
  it('an OutsideRadius 403 is permanent FOR A QUEUED SCAN — the real 132-retry loop', () => {
    // The position is frozen in the queued payload, so the retry is the request just refused. Read as
    // an account state it produced 132 refusals from ONE pair of coordinates at Bayıl yolu, and 277
    // log rows company-wide from 58 real taps.
    expect(isPermanentQueuedReject(403, 'OutsideRadius')).toBe(true)
  })

  it('still covers every device code — this replaces the device check, it does not narrow it', () => {
    expect(isPermanentQueuedReject(403, 'SharedDeviceNotAllowed')).toBe(true)
    expect(isPermanentQueuedReject(403, 'DeviceMismatch')).toBe(true)
    expect(isPermanentQueuedReject(403, 'NoDeviceBound')).toBe(true)
    expect(isPermanentQueuedReject(403, 'DeviceAccountLimit')).toBe(true)
  })

  it('a PIN-state 403 STILL survives — the guarantee this whole file exists to protect', () => {
    // A scan must never be thrown away for a condition that clears the moment somebody picks a PIN.
    expect(isPermanentQueuedReject(403, 'MustSetPin')).toBe(false)
    expect(isPermanentQueuedReject(403, undefined)).toBe(false)
    expect(isPermanentQueuedReject(401, 'OutsideRadius')).toBe(false)
  })

  it("a LIVE scan is not this rule's business — only the frozen payload of a queued one", () => {
    // The live path re-reads the GPS on every attempt, so walking closer really does change the
    // answer. This function is asked only by the drainer.
    expect(isPermanentQueuedReject(400, 'OutsideRadius')).toBe(false)
  })
})
