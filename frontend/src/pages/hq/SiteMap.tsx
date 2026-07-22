import { Fragment, useEffect, useMemo, useState } from 'react'
import { Circle, CircleMarker, MapContainer, TileLayer, Tooltip, useMap } from 'react-leaflet'
import type { Map as LeafletMap } from 'leaflet'
import type { GroupSite } from '../../api/hq'

/** Fits the view to every site once the list arrives, so the board never opens on the wrong city or
 *  zoomed into a car park. Re-fits only when the set of sites changes, not on every refresh — the
 *  map jumping every twenty seconds while someone is looking at it would be worse than useless. */
function FitToSites({ sites, onFit }: { sites: GroupSite[]; onFit: (fit: () => void) => void }) {
  const map = useMap()
  const key = sites.map((s) => s.id).join(',')

  useEffect(() => {
    if (sites.length === 0) return
    const fit = () => {
      if (sites.length === 1) map.setView([sites[0].lat, sites[0].lng], 13)
      else map.fitBounds(sites.map((s) => [s.lat, s.lng] as [number, number]), { padding: [42, 42] })
    }
    fit()
    // Hand the same fit back up so the reset button returns to exactly the opening view.
    onFit(fit)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key])

  return null
}

/**
 * The group's sites on a live map, sized by how many people are working at each right now.
 *
 * This is the one element that a table of the same numbers cannot replace: a director recognises
 * their own city and their own sites instantly, and "our people are at these places right now" lands
 * in a way that "12 ərazi" never does.
 *
 * Dark tiles, because the board is dark and standard OpenStreetMap tiles would burn a white hole
 * through the middle of it.
 */
export function SiteMap({ sites, accentOf }: { sites: GroupSite[]; accentOf: (i: number) => string }) {
  const [map, setMap] = useState<LeafletMap | null>(null)
  const [fitFn, setFitFn] = useState<{ run: () => void } | null>(null)
  // The wheel is claimed only after a deliberate click on the map. Enabling it on hover would mean
  // scrolling past the board zooms the map instead of moving the page — the kind of thing that
  // happens exactly once, in front of the person you are demonstrating to.
  const [wheelArmed, setWheelArmed] = useState(false)

  const centre = useMemo<[number, number]>(() => {
    if (sites.length === 0) return [40.4093, 49.8671] // Baku, until the first site loads
    const lat = sites.reduce((s, x) => s + x.lat, 0) / sites.length
    const lng = sites.reduce((s, x) => s + x.lng, 0) / sites.length
    return [lat, lng]
  }, [sites])

  // Marker size carries the headcount. Square-root rather than linear: a site with forty people
  // would otherwise draw a blob that swallows its neighbours.
  const busiest = Math.max(1, ...sites.map((s) => s.onDuty))
  const radiusOf = (onDuty: number) => 7 + Math.sqrt(onDuty / busiest) * 15

  function armWheel() {
    if (!map || wheelArmed) return
    map.scrollWheelZoom.enable()
    setWheelArmed(true)
  }

  return (
    <div className="hq-map" onClick={armWheel}>
      <MapContainer
        ref={setMap}
        center={centre}
        zoom={11}
        scrollWheelZoom={false}
        zoomControl={false}
        attributionControl={false}
        style={{ height: '100%', width: '100%', background: '#0B1020' }}
      >
        <TileLayer url="https://{s}.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}{r}.png" />
        <FitToSites sites={sites} onFit={(run) => setFitFn({ run })} />
        {sites.map((s) => {
          const colour = accentOf(s.companyIndex < 0 ? 0 : s.companyIndex)
          const live = s.onDuty > 0
          return (
            // Fragment, not a wrapper element: react-leaflet renders children into the map
            // container, and a stray div there sits on top of the map and swallows clicks.
            <Fragment key={s.id}>
              {/* The geofence, to scale. Six dots on a city map look like nothing; six areas look
                  like coverage — and this is the only place the GPS rule is shown rather than said. */}
              <Circle
                center={[s.lat, s.lng]}
                radius={s.radiusMeters}
                pathOptions={{
                  color: colour,
                  fillColor: colour,
                  fillOpacity: live ? 0.1 : 0.04,
                  opacity: live ? 0.4 : 0.18,
                  weight: 1,
                  dashArray: '4 5',
                }}
              />
              <CircleMarker
                center={[s.lat, s.lng]}
                radius={radiusOf(s.onDuty)}
                pathOptions={{
                  color: colour,
                  fillColor: colour,
                  // A site with nobody on it stays visible but recedes — the eye should go to where
                  // work is actually happening.
                  fillOpacity: live ? 0.45 : 0.12,
                  opacity: live ? 0.95 : 0.35,
                  weight: 2,
                  className: live ? 'hq-marker-live' : undefined,
                }}
              >
                {/* Permanent, not on hover: during a demo nobody is touching the laptop, and an
                    unlabelled dot raises the question the map exists to answer. */}
                <Tooltip permanent direction="right" offset={[9, 0]} opacity={1} className="hq-tip hq-tip-label">
                  <b>{s.name}</b>
                  {s.onDuty > 0 && <span className="hq-tip-n"> {s.onDuty}</span>}
                </Tooltip>
              </CircleMarker>
            </Fragment>
          )
        })}
      </MapContainer>

      {/* Our own controls rather than Leaflet's: its default chrome is a white box, which on a dark
          board looks like something broke. */}
      <div className="hq-map-ctl">
        <button type="button" aria-label="Yaxınlaşdır" onClick={() => map?.zoomIn()}>+</button>
        <button type="button" aria-label="Uzaqlaşdır" onClick={() => map?.zoomOut()}>−</button>
        <button
          type="button"
          className="hq-map-ctl-wide"
          onClick={() => fitFn?.run()}
        >
          Hamısı
        </button>
      </div>

      {!wheelArmed && sites.length > 0 && (
        <div className="hq-map-hint">Yaxınlaşdırmaq üçün xəritəyə klikləyin</div>
      )}
    </div>
  )
}
