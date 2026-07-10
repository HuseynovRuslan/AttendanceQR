// Getting a position out of a phone browser fails in several distinct ways, and the employee can
// only fix it if we say which one. Everything here exists to turn one useless "GPS icazəsi lazımdır"
// into a specific, actionable answer.

/** Why the browser refused a position. Maps 1:1 onto the reason codes /scan-failure accepts. */
export type GeoFailKind = 'denied' | 'unavailable' | 'timeout' | 'unsupported'

export type GeoResult =
  | { ok: true; coords: GeolocationCoordinates }
  | { ok: false; kind: GeoFailKind }

/** Beyond this the fix says little against a ~150 m geofence. We warn and log it; we never block. */
export const POOR_ACCURACY_METERS = 100

export const FAILURE_REASON: Record<GeoFailKind, string> = {
  denied: 'GpsPermissionDenied',
  unavailable: 'GpsUnavailable',
  timeout: 'GpsTimeout',
  unsupported: 'GpsUnsupported',
}

export type Platform = 'ios' | 'android' | 'other'

export function platform(): Platform {
  const ua = navigator.userAgent
  // iPadOS 13+ claims to be a Mac; the touch-point count is what gives it away.
  if (/iPhone|iPad|iPod/.test(ua) || (/Macintosh/.test(ua) && navigator.maxTouchPoints > 1)) return 'ios'
  if (/Android/.test(ua)) return 'android'
  return 'other'
}

/**
 * The site-level permission, when the browser will tell us. Chrome answers reliably. Safari does not
 * implement the 'geolocation' name at all, so iOS always lands on 'unknown' — which is exactly why
 * the help screen walks iPhone users through every layer instead of guessing one.
 */
export async function permissionState(): Promise<'granted' | 'denied' | 'prompt' | 'unknown'> {
  try {
    const status = await navigator.permissions?.query({ name: 'geolocation' as PermissionName })
    return status?.state ?? 'unknown'
  } catch {
    return 'unknown'
  }
}

function once(options: PositionOptions): Promise<GeolocationPosition> {
  return new Promise((resolve, reject) => navigator.geolocation.getCurrentPosition(resolve, reject, options))
}

/**
 * Ask for a position, forgivingly. A plain getCurrentPosition({enableHighAccuracy:true}) gets two
 * things wrong on a real phone: it demands a fresh satellite fix even when a perfectly good
 * 20-second-old one is sitting there, and it reports a GPS timeout as outright failure even though a
 * coarse Wi-Fi/cell position was available for the asking. So: accept a recent fix, then fall back
 * to network positioning before giving up.
 */
export async function getPosition(): Promise<GeoResult> {
  if (!navigator.geolocation) return { ok: false, kind: 'unsupported' }

  try {
    const pos = await once({ enableHighAccuracy: true, timeout: 12_000, maximumAge: 30_000 })
    return { ok: true, coords: pos.coords }
  } catch (err) {
    if ((err as GeolocationPositionError)?.code === 1) return { ok: false, kind: 'denied' }

    try {
      const pos = await once({ enableHighAccuracy: false, timeout: 8_000, maximumAge: 60_000 })
      return { ok: true, coords: pos.coords }
    } catch (err2) {
      const code = (err2 as GeolocationPositionError)?.code
      if (code === 1) return { ok: false, kind: 'denied' }
      // iOS reports a hard "denied" as POSITION_UNAVAILABLE often enough that the error code alone
      // isn't trustworthy — if the browser will state the permission outright, believe it instead.
      if ((await permissionState()) === 'denied') return { ok: false, kind: 'denied' }
      return { ok: false, kind: code === 3 ? 'timeout' : 'unavailable' }
    }
  }
}
