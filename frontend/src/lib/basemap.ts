/**
 * The map background every admin map draws on.
 *
 * CARTO used to serve these tiles to anyone; in August 2026 they began requiring a key and started
 * answering unauthenticated requests with a tile that has "API KEY REQUIRED" printed across it. The
 * request still returns 200, so nothing errored and no log line appeared — the maps simply went
 * wrong in front of the customer.
 *
 * With a key, CARTO. Without one, OpenStreetMap. The fallback is the point: a key that lapses, is
 * revoked, or is missing from a local build must degrade to a working map rather than a watermark,
 * and it means a developer with no key still sees a real map.
 *
 * The key is baked into the bundle by Vite, so it is public. That is normal for map keys and is why
 * CARTO asks for a domain — but ours is NOT domain-locked (verified: it serves tiles with no
 * referer), so if the 5M/month allowance is ever exhausted, someone lifting it is the first thing to
 * check.
 */

const KEY = import.meta.env.VITE_CARTO_KEY as string | undefined

export type BasemapVariant = 'light' | 'dark'

export interface Basemap {
  url: string
  /** Required by both providers' terms — the free tier is granted in exchange for it. */
  attribution: string
  subdomains: string
  maxZoom: number
  /**
   * True when a dark map was asked for but the tiles are light. OpenStreetMap publishes no dark
   * style, and a white map on the dark HQ console is worse than a filtered one — the caller inverts
   * the tile layer in CSS. False whenever the real dark tiles are being served.
   */
  needsDarkFilter: boolean
}

const OSM_ATTRIBUTION =
  '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> töhfəçiləri'

const CARTO_ATTRIBUTION =
  `${OSM_ATTRIBUTION}, &copy; <a href="https://carto.com/attributions">CARTO</a>`

export function basemap(variant: BasemapVariant = 'light'): Basemap {
  if (KEY) {
    // {r} is Leaflet's retina suffix ("@2x" on a high-DPI screen). Verified to work with the key.
    const style = variant === 'dark' ? 'dark_all' : 'light_all'
    return {
      url: `https://{s}.basemaps.cartocdn.com/${style}/{z}/{x}/{y}{r}.png?key=${KEY}`,
      attribution: CARTO_ATTRIBUTION,
      subdomains: 'abcd',
      maxZoom: 20,
      needsDarkFilter: false,
    }
  }

  return {
    url: 'https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png',
    attribution: OSM_ATTRIBUTION,
    subdomains: 'abc',
    // OSM's own tiles stop at 19; asking for 20 gets blank squares at the deepest zoom.
    maxZoom: 19,
    needsDarkFilter: variant === 'dark',
  }
}

// The filter itself lives in hq.css, next to the only map that uses it — one definition, not two
// that can drift.
