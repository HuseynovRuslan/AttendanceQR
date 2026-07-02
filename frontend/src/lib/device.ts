const DEVICE_KEY = 'attendanceqr.device'

/**
 * The stable per-device fingerprint the backend binds a device to. A random UUID persisted in
 * localStorage is enough — no fingerprinting library needed. Created once, reused thereafter.
 * The same value is sent at activation (binds the device) and at every scan (must match).
 */
export function getDeviceFingerprint(): string {
  let id = localStorage.getItem(DEVICE_KEY)
  if (!id) {
    id = crypto.randomUUID()
    localStorage.setItem(DEVICE_KEY, id)
  }
  return id
}
