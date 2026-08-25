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

/** Great-circle distance between two lat/lng points in metres (haversine). Mirrors the server's
 *  GeoCalculator so the scan page can pre-check the geofence before the QR is even scanned. */
export function distanceMeters(lat1: number, lng1: number, lat2: number, lng2: number): number {
  const R = 6_371_000
  const toRad = (d: number) => (d * Math.PI) / 180
  const dLat = toRad(lat2 - lat1)
  const dLng = toRad(lng2 - lng1)
  const a =
    Math.sin(dLat / 2) ** 2 +
    Math.cos(toRad(lat1)) * Math.cos(toRad(lat2)) * Math.sin(dLng / 2) ** 2
  return 2 * R * Math.asin(Math.min(1, Math.sqrt(a)))
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
 * Ask for a position, patiently.
 *
 * The first version gave the satellites 12 seconds and then fell back to a coarse network fix for 8
 * more. Both numbers were wrong for the people who actually fail this, and Dədə Qorqud Parkı proved
 * it: one worker there logged five "GPS siqnal tapmadı" in a row, having granted the permission two
 * refusals earlier — so his phone had no cached fix at all and was doing a genuine cold start, under
 * trees, on a phone with no data. A cold start takes thirty to sixty seconds. Twelve is not a timeout,
 * it is a coin toss.
 *
 * Worse, the fallback that existed for exactly this case could not run for exactly these people: a
 * coarse position is a NETWORK position, computed from nearby Wi-Fi and cell IDs by a service on the
 * internet. A phone with no data plan cannot obtain one. The safety net was missing under the only
 * people who fall.
 *
 * So it watches instead of asking once, and takes the first fix that arrives — accuracy is only ever a
 * warning here, never a block, so an early coarse fix is worth more than a perfect one that comes
 * after the person has given up. The coarse attempt still runs at the end for anyone who does have
 * data. `onCountdown` lets the screen show the seconds rather than looking frozen: a wait nobody can
 * see is a wait people abandon.
 */
const COLD_FIX_BUDGET_MS = 45_000

export async function getPosition(onCountdown?: (secondsLeft: number) => void): Promise<GeoResult> {
  if (!navigator.geolocation) return { ok: false, kind: 'unsupported' }

  const watched = await new Promise<GeolocationPosition | GeolocationPositionError | null>((resolve) => {
    let settled = false
    const finish = (v: GeolocationPosition | GeolocationPositionError | null) => {
      if (settled) return
      settled = true
      navigator.geolocation.clearWatch(id)
      clearInterval(tick)
      clearTimeout(deadline)
      resolve(v)
    }

    const id = navigator.geolocation.watchPosition(
      (pos) => finish(pos),
      // A hard refusal is final and there is nothing to wait for; anything else may still resolve
      // itself when a satellite comes into view, so the watch stays open until the budget runs out.
      (err) => { if (err.code === 1) finish(err) },
      { enableHighAccuracy: true, timeout: COLD_FIX_BUDGET_MS, maximumAge: 30_000 },
    )

    const startedAt = Date.now()
    const tick = setInterval(() => {
      const left = Math.ceil((COLD_FIX_BUDGET_MS - (Date.now() - startedAt)) / 1000)
      onCountdown?.(Math.max(0, left))
    }, 1000)
    onCountdown?.(COLD_FIX_BUDGET_MS / 1000)

    const deadline = setTimeout(() => finish(null), COLD_FIX_BUDGET_MS)
  })

  if (watched && 'coords' in watched) return { ok: true, coords: watched.coords }
  if (watched && 'code' in watched && watched.code === 1) return { ok: false, kind: 'denied' }

  // Nothing from the sky. A network position needs the internet, so this is the branch that helps
  // somebody with data and cannot help somebody without it.
  try {
    const pos = await once({ enableHighAccuracy: false, timeout: 8_000, maximumAge: 120_000 })
    return { ok: true, coords: pos.coords }
  } catch (err) {
    const code = (err as GeolocationPositionError)?.code
    if (code === 1) return { ok: false, kind: 'denied' }
    // iOS reports a hard "denied" as POSITION_UNAVAILABLE often enough that the error code alone
    // isn't trustworthy — if the browser will state the permission outright, believe it instead.
    if ((await permissionState()) === 'denied') return { ok: false, kind: 'denied' }
    return { ok: false, kind: code === 3 ? 'timeout' : 'unavailable' }
  }
}
