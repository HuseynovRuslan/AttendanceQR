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


/**
 * Words people type instead of a place.
 *
 * Production holds «Obyektdeyem», «Obyektdəyəm», «Obyekt deyem» and «Obyektdeyem» — four spellings
 * of "I am at the site", which answers the question "where" with the fact that they are somewhere.
 * 17 of 118 visits carry one of these or nothing at all.
 *
 * Matching strips everything but letters, so spacing and punctuation cannot dodge it, and the list
 * is of WHOLE labels rather than substrings: «Obyekt 4» is a real answer and must survive.
 */
const JUNK_LABELS = new Set([
  'obyekt', 'obyektde', 'obyektdə', 'obyektdeyem', 'obyektdəyəm', 'obyekdeyem', 'obyekdəyəm',
  'burdayam', 'buradayam', 'yerindeyem', 'yerindəyəm', 'yerdeyem', 'yerdəyəm',
  'isdeyem', 'işdəyəm', 'isdəyəm', 'catdim', 'çatdım', 'geldim', 'gəldim',
  'erazideyem', 'ərazidəyəm', 'sahedeyem', 'sahədəyəm',
])

export function isJunkTargetLabel(text: string | null | undefined): boolean {
  if (!text) return true
  const clean = text.toLocaleLowerCase('az').replace(/[^a-zəıöğşüçğ]/g, '')
  return clean.length === 0 || JUNK_LABELS.has(clean)
}

/**
 * A GPS point turned into an address a person would recognise.
 *
 * ⚠️ THIS SENDS THE EMPLOYEE'S EXACT POSITION TO A THIRD PARTY (openstreetmap.org). Everywhere else
 * in this product their coordinates go only to our own server; map TILES come from outside, but a
 * tile request does not carry where the person is standing and this does. It is therefore called in
 * exactly one place — when the worker says the branch list does not contain where they are — and
 * never for a visit at one of the company's own branches, where the branch name is the better answer
 * anyway.
 *
 * Nominatim is free and needs no key, and asks in return that it not be queried systematically. At
 * this volume (a handful of ad-hoc visits a day) that holds; it must never be moved onto the scan
 * path, which runs hundreds of times a morning.
 *
 * Fails to null, quickly. A worker standing in the rain must not wait on somebody else's server:
 * past the timeout they simply type it themselves, which is what they did before this existed.
 */
export async function reverseGeocode(lat: number, lng: number): Promise<string | null> {
  const abort = new AbortController()
  const timer = setTimeout(() => abort.abort(), 3500)
  try {
    const res = await fetch(
      `https://nominatim.openstreetmap.org/reverse?format=json&lat=${lat}&lon=${lng}&accept-language=az`,
      { signal: abort.signal, headers: { Accept: 'application/json' } },
    )
    if (!res.ok) return null
    const data = await res.json() as { address?: Record<string, string>; display_name?: string }
    const a = data.address
    if (!a) return data.display_name?.split(',').slice(0, 2).join(', ').trim() || null

    // A settlement name is what people here actually say — «Pirşağı», «Maştağa», «Bilgəh» — and a
    // street alone is ambiguous across a city with repeated street names.
    const settlement = a.village || a.town || a.suburb || a.hamlet
    const road = a.road || a.pedestrian
    const district = a.city_district || a.district || a.county

    if (settlement && road) return `${settlement}, ${road}`
    if (settlement) return /qəs|qes/i.test(settlement) ? settlement : `${settlement} qəs.`
    if (district && road) return `${district}, ${road}`
    return district || road || data.display_name?.split(',').slice(0, 2).join(', ').trim() || null
  } catch {
    return null
  } finally {
    clearTimeout(timer)
  }
}
