import { Fragment, useEffect, useMemo, useState } from 'react'
import { Circle, CircleMarker, MapContainer, TileLayer, Tooltip, useMap } from 'react-leaflet'
import type { Map as LeafletMap } from 'leaflet'
import type { GroupSite } from '../../api/hq'

/** Roughly 55 km. Sites inside this of each other count as one working area. */
const CLUSTER_DEGREES = 0.5

interface MapActions {
  fitAll: () => void
  flyTo: (site: GroupSite) => void
}

/**
 * The view the board opens on: the area holding the most sites, not the bounds of every site.
 *
 * Fitting everything sounds right and looks wrong. One remote site drags the frame out to the whole
 * country, the ones in the city collapse into a single blob, and half the panel is sea. Opening on
 * the densest cluster shows separated, readable sites; "Hamısı" is one click away for the rest.
 */
function defaultCluster(sites: GroupSite[]): GroupSite[] {
  if (sites.length < 2) return sites
  const near = (a: GroupSite, b: GroupSite) =>
    Math.abs(a.lat - b.lat) < CLUSTER_DEGREES && Math.abs(a.lng - b.lng) < CLUSTER_DEGREES

  let best: GroupSite[] = []
  for (const anchor of sites) {
    const group = sites.filter((s) => near(anchor, s))
    if (group.length > best.length) best = group
  }
  return best.length > 1 ? best : sites
}

/** Sets the opening view once and hands the map's controls back up. Re-runs only when the set of
 *  sites changes — a map that re-frames itself every twenty seconds while someone is looking at it
 *  would be worse than useless. */
function FitTo({ sites, register }: { sites: GroupSite[]; register: (fns: MapActions) => void }) {
  const map = useMap()
  const key = sites.map((s) => s.id).join(',')

  useEffect(() => {
    if (sites.length === 0) return

    const fit = (subset: GroupSite[]) => {
      if (subset.length === 0) return
      if (subset.length === 1) { map.setView([subset[0].lat, subset[0].lng], 14); return }
      map.fitBounds(subset.map((s) => [s.lat, s.lng] as [number, number]), { padding: [46, 46], maxZoom: 14 })
    }

    fit(defaultCluster(sites))
    register({
      fitAll: () => fit(sites),
      flyTo: (site) => map.flyTo([site.lat, site.lng], 15, { duration: 0.9 }),
    })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key])

  return null
}

/**
 * The group's sites on a live map, sized by how many people are working at each right now.
 *
 * A director recognises their own city and their own sites instantly, and "our people are at these
 * places right now" lands in a way that "12 ərazi" never does.
 *
 * Names live in the list beside the map rather than on it: labels drawn on the map collided into an
 * unreadable pile as soon as two sites sat near each other — which, in a city, is most of them.
 */
export function SiteMap({ sites, accentOf }: { sites: GroupSite[]; accentOf: (i: number) => string }) {
  const [map, setMap] = useState<LeafletMap | null>(null)
  const [actions, setActions] = useState<MapActions | null>(null)
  const [focused, setFocused] = useState<string | null>(null)
  // The wheel is claimed only after a deliberate click on the map. Enabling it on hover would mean
  // scrolling past the board zooms the map instead of moving the page — the kind of thing that
  // happens exactly once, in front of the person you are demonstrating to.
  const [wheelArmed, setWheelArmed] = useState(false)

  const centre = useMemo<[number, number]>(() => {
    if (sites.length === 0) return [40.4093, 49.8671] // Baku, until the first site loads
    const cluster = defaultCluster(sites)
    const lat = cluster.reduce((s, x) => s + x.lat, 0) / cluster.length
    const lng = cluster.reduce((s, x) => s + x.lng, 0) / cluster.length
    return [lat, lng]
  }, [sites])

  // Marker size carries the headcount. Square-root rather than linear: a site with forty people
  // would otherwise draw a blob that swallows its neighbours.
  const busiest = Math.max(1, ...sites.map((s) => s.onDuty))
  const radiusOf = (onDuty: number) => 7 + Math.sqrt(onDuty / busiest) * 14

  function armWheel() {
    if (!map || wheelArmed) return
    map.scrollWheelZoom.enable()
    setWheelArmed(true)
  }

  const ordered = [...sites].sort((a, b) => b.onDuty - a.onDuty)

  return (
    <div className="hq-mapwrap">
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
          <FitTo sites={sites} register={setActions} />
          {sites.map((s) => {
            const colour = accentOf(s.companyIndex < 0 ? 0 : s.companyIndex)
            const live = s.onDuty > 0
            const isFocused = focused === s.id
            return (
              // Fragment, not a wrapper element: react-leaflet renders children into the map
              // container, and a stray div there sits on top of the map and swallows clicks.
              <Fragment key={s.id}>
                {/* The geofence, to scale — the only place the GPS rule is shown rather than said. */}
                <Circle
                  center={[s.lat, s.lng]}
                  radius={s.radiusMeters}
                  pathOptions={{
                    color: colour,
                    fillColor: colour,
                    fillOpacity: live ? 0.1 : 0.04,
                    opacity: isFocused ? 0.75 : live ? 0.4 : 0.18,
                    weight: 1,
                    dashArray: '4 5',
                  }}
                />
                <CircleMarker
                  center={[s.lat, s.lng]}
                  radius={radiusOf(s.onDuty) + (isFocused ? 4 : 0)}
                  pathOptions={{
                    color: colour,
                    fillColor: colour,
                    // A site with nobody on it stays visible but recedes — the eye should go to
                    // where work is actually happening.
                    fillOpacity: live ? 0.5 : 0.12,
                    opacity: live ? 0.95 : 0.35,
                    weight: isFocused ? 3 : 2,
                    className: live ? 'hq-marker-live' : undefined,
                  }}
                  eventHandlers={{ click: () => { setFocused(s.id); actions?.flyTo(s) } }}
                >
                  <Tooltip direction="top" offset={[0, -6]} opacity={1} className="hq-tip">
                    <b>{s.name}</b>
                    <br />
                    {s.onDuty > 0 ? `${s.onDuty} nəfər iş başında` : 'hazırda boş'}
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
            onClick={() => { setFocused(null); actions?.fitAll() }}
          >
            Hamısı
          </button>
        </div>

        {!wheelArmed && sites.length > 0 && (
          <div className="hq-map-hint">Yaxınlaşdırmaq üçün xəritəyə klikləyin</div>
        )}
      </div>

      {/* Names, readable, ordered by where the work is. Clicking one flies the map to it — the single
          most useful thing to be able to do while someone is watching. */}
      <ul className="hq-sitelist">
        {ordered.map((s) => (
          <li
            key={s.id}
            className={`hq-site${focused === s.id ? ' is-focused' : ''}${s.onDuty === 0 ? ' is-idle' : ''}`}
            onClick={() => { setFocused(s.id); actions?.flyTo(s) }}
          >
            <i style={{ background: accentOf(s.companyIndex < 0 ? 0 : s.companyIndex) }} />
            <span className="hq-site-name">{s.name}</span>
            <span className="hq-site-n hq-num">{s.onDuty > 0 ? s.onDuty : '—'}</span>
          </li>
        ))}
      </ul>
    </div>
  )
}
