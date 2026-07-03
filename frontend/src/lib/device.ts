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

/**
 * Best-effort human-readable device name from the User-Agent, captured once at activation and
 * shown in the admin employee list (e.g. "Samsung Galaxy", "iPhone"). Purely cosmetic — never
 * used for any security decision; getDeviceFingerprint() is what scan actually matches against.
 */
export function getFriendlyDeviceName(): string {
  const ua = navigator.userAgent
  if (/iPhone/.test(ua)) return 'iPhone'
  if (/iPad/.test(ua)) return 'iPad'
  if (/Samsung/.test(ua)) return 'Samsung Galaxy'
  if (/Xiaomi|MIUI/.test(ua)) return 'Xiaomi'
  if (/Huawei/.test(ua)) return 'Huawei'
  if (/Android/.test(ua)) return 'Android cihaz'
  if (/Windows/.test(ua)) return 'Windows PC'
  if (/Macintosh/.test(ua)) return 'Mac'
  return 'Naməlum cihaz'
}
