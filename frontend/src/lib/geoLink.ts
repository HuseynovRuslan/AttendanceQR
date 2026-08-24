/**
 * Reading coordinates out of whatever the admin pasted.
 *
 * Typing latitude and longitude by hand is how a branch ends up a kilometre from where people work,
 * and nobody notices until a scan is refused for being outside the fence. Everyone already has the
 * place open in a map app, so the honest input is the thing they can copy: the link, or the pair of
 * numbers the map shows them.
 *
 * Google alone writes the pair four different ways depending on how you got there — a search, a
 * dropped pin, a place page, a shared link — so this reads all of them, plus a bare "lat, lng".
 */
export type Coords = { lat: number; lng: number }

/** Short links (maps.app.goo.gl, goo.gl/maps) carry no coordinates at all — they redirect to one. */
export function isShortMapLink(text: string): boolean {
  return /(?:maps\.app\.goo\.gl|goo\.gl\/maps)/i.test(text)
}

function valid(lat: number, lng: number): Coords | null {
  if (!Number.isFinite(lat) || !Number.isFinite(lng)) return null
  if (lat < -90 || lat > 90 || lng < -180 || lng > 180) return null
  // 0,0 is in the Atlantic and is what a broken parse produces, so it is refused rather than accepted
  // as a location somebody meant to enter.
  if (lat === 0 && lng === 0) return null
  return { lat, lng }
}

const PATTERNS: RegExp[] = [
  // ?q=40.39,49.86 — a search or a shared pin
  /[?&]q=(-?\d+(?:\.\d+)?),\s*(-?\d+(?:\.\d+)?)/i,
  // ?ll=40.39,49.86 — older share links
  /[?&]ll=(-?\d+(?:\.\d+)?),\s*(-?\d+(?:\.\d+)?)/i,
  // ?daddr=… / ?saddr=… — a route
  /[?&](?:daddr|saddr)=(-?\d+(?:\.\d+)?),\s*(-?\d+(?:\.\d+)?)/i,
  // /@40.39,49.86,17z — the map's own viewport, in the URL bar
  /@(-?\d+(?:\.\d+)?),\s*(-?\d+(?:\.\d+)?)/,
  // !3d40.39!4d49.86 — a place page: the PIN, which is what is wanted when the two disagree
  /!3d(-?\d+(?:\.\d+)?)!4d(-?\d+(?:\.\d+)?)/,
  // /search/40.39,+49.86 or /dir/40.39,49.86
  /\/(?:search|dir)\/(-?\d+(?:\.\d+)?),\+?\s*(-?\d+(?:\.\d+)?)/i,
]

/**
 * Coordinates from a pasted link or a pasted pair, or null when there is nothing to read.
 *
 * A place page carries the map's viewport in `@` AND the pin in `!3d!4d`, and they are not the same
 * point — the pin is the place, the viewport is wherever the map happened to be looking — so the pin
 * is preferred when both are present.
 */
export function parseCoords(text: string): Coords | null {
  const input = (text ?? '').trim()
  if (!input) return null

  // The pin first, whatever else the URL contains.
  const pin = input.match(/!3d(-?\d+(?:\.\d+)?)!4d(-?\d+(?:\.\d+)?)/)
  if (pin) {
    const coords = valid(Number(pin[1]), Number(pin[2]))
    if (coords) return coords
  }

  for (const pattern of PATTERNS) {
    const m = input.match(pattern)
    if (!m) continue
    const coords = valid(Number(m[1]), Number(m[2]))
    if (coords) return coords
  }

  // A bare pair, which is what the map shows when you long-press: "40.39669, 49.86564". Anchored, so a
  // stray pair of numbers inside a longer sentence is not mistaken for a location.
  const bare = input.match(/^(-?\d{1,3}(?:\.\d+)?)\s*,\s*(-?\d{1,3}(?:\.\d+)?)$/)
  if (bare) return valid(Number(bare[1]), Number(bare[2]))

  return null
}

/** Trimmed to the precision a geofence can actually use — 6 decimals is ~11 cm. */
export function formatCoord(value: number): string {
  return String(Number(value.toFixed(6)))
}
