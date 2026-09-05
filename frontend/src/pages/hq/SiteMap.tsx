import { Fragment, useEffect, useMemo, useState } from 'react'
import { Circle, MapContainer, Marker, Popup, TileLayer, useMap } from 'react-leaflet'
import L from 'leaflet'
import { basemap } from '../../lib/basemap'
import type { Map as LeafletMap } from 'leaflet'
import type { GroupCompany, GroupSite } from '../../api/hq'

// Resolved once: the key does not change while the page is open. This console is dark, so a
// keyless build gets light tiles inverted rather than a white slab under dark chrome.
const BASE = basemap('dark')

/** Roughly 55 km. Sites inside this of each other count as one working area. */
const CLUSTER_DEGREES = 0.5

/**
 * The marker IS the number.
 *
 * It was a circle sized by headcount, and sizing alone could not answer the question the map is
 * asked: a director looking at a wall board wants "how many are at Ramana right now", and a slightly
 * larger dot never says forty. Worse, at city zoom the sized circles overlapped into one pink smear
 * across central Baku — the sites most worth reading were the least readable.
 *
 * So the count is printed inside, and the size is a coarse band rather than a continuous scale:
 * three sizes are enough to rank a glance, and they stop a busy site from swallowing its neighbours.
 * An empty branch keeps its place and states its zero in a dashed outline — «which of my branches has
 * nobody on it» is half of what this map is for, and an invisible marker cannot answer it.
 */
function markerIcon(onDuty: number, colour: string, isFocused: boolean, isHovered: boolean): L.DivIcon {
  const size = onDuty >= 50 ? 40 : onDuty >= 10 ? 34 : 28
  const state = onDuty === 0 ? 'is-idle' : 'is-live'
  const lift = `${isFocused ? ' is-focused' : ''}${isHovered ? ' is-hovered' : ''}`
  return L.divIcon({
    className: 'hq-map-pin',
    html:
      `<div class="hq-pin ${state}${lift}" ` +
      `style="--dot:${colour};width:${size}px;height:${size}px"><span>${onDuty}</span></div>`,
    iconSize: [size, size],
    iconAnchor: [size / 2, size / 2],
    popupAnchor: [0, -size / 2],
  })
}

interface MapActions {
  fit: (subset: GroupSite[]) => void
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

/**
 * Sets the opening view and hands the map's controls back up.
 *
 * Re-runs only when the SET of sites changes — which now includes a company filter being applied, so
 * picking a company reframes onto its branches. It must never re-run on the twenty-second refresh: a
 * map that re-frames itself while someone is looking at it is worse than useless.
 */
function FitTo({ sites, cluster, register }: {
  sites: GroupSite[]
  cluster: boolean
  register: (fns: MapActions) => void
}) {
  const map = useMap()
  const key = sites.map((s) => s.id).join(',')

  useEffect(() => {
    const fit = (subset: GroupSite[]) => {
      if (subset.length === 0) return
      if (subset.length === 1) { map.flyTo([subset[0].lat, subset[0].lng], 14, { duration: 0.7 }); return }
      map.fitBounds(subset.map((s) => [s.lat, s.lng] as [number, number]), { padding: [46, 46], maxZoom: 14 })
    }

    register({ fit, flyTo: (site) => map.flyTo([site.lat, site.lng], 15, { duration: 0.9 }) })
    if (sites.length === 0) return
    // Filtered to one company: show ALL of that company's branches. Unfiltered: the densest cluster,
    // or a single far-flung site would frame the whole country.
    fit(cluster ? defaultCluster(sites) : sites)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key, cluster])

  return null
}

/**
 * The group's sites on a live map, each one stating how many people are working there right now.
 *
 * A director recognises their own city and their own sites instantly, and "our people are at these
 * places right now" lands in a way that "21 filial" never does.
 *
 * Names live in the list beside the map rather than on it: labels drawn on the map collided into an
 * unreadable pile as soon as two sites sat near each other — which, in a city, is most of them.
 */
export function SiteMap({ sites, companies = [], accentOf }: {
  sites: GroupSite[]
  companies?: GroupCompany[]
  accentOf: (i: number) => string
}) {
  const [map, setMap] = useState<LeafletMap | null>(null)
  const [actions, setActions] = useState<MapActions | null>(null)
  const [focused, setFocused] = useState<string | null>(null)
  const [onlyCompany, setOnlyCompany] = useState<number | null>(null)
  const [hovered, setHovered] = useState<string | null>(null)
  // «Yalnız boş filiallar» — the amber line is a question and this makes it answerable in one press
  // instead of scrolling a list of twenty-one to find the three that matter.
  const [emptyOnly, setEmptyOnly] = useState(false)
  // The wheel is claimed only after a deliberate click on the map. Enabling it on hover would mean
  // scrolling past the board zooms the map instead of moving the page — the kind of thing that
  // happens exactly once, in front of the person you are demonstrating to.
  const [wheelArmed, setWheelArmed] = useState(false)

  const shown = useMemo(
    () => (onlyCompany === null ? sites : sites.filter((s) => s.companyIndex === onlyCompany)),
    [sites, onlyCompany],
  )

  const centre = useMemo<[number, number]>(() => {
    if (sites.length === 0) return [40.4093, 49.8671] // Baku, until the first site loads
    const cluster = defaultCluster(sites)
    const lat = cluster.reduce((s, x) => s + x.lat, 0) / cluster.length
    const lng = cluster.reduce((s, x) => s + x.lng, 0) / cluster.length
    return [lat, lng]
  }, [sites])

  function armWheel() {
    if (!map || wheelArmed) return
    map.scrollWheelZoom.enable()
    setWheelArmed(true)
  }

  function focus(s: GroupSite) {
    setFocused(s.id)
    // Central Baku holds most of the branches within a few hundred metres of each other, so at the
    // opening zoom they sit on top of one another. Flying in on a press is what pulls them apart —
    // the alternative is asking a director to pinch-zoom a wall screen.
    actions?.flyTo(s)
    // …and bring the list to the same branch. Pressing a dot and then hunting the name in a list of
    // twenty-one is two jobs for one question.
    requestAnimationFrame(() => {
      document.getElementById(`hq-site-${s.id}`)?.scrollIntoView({ behavior: 'smooth', block: 'nearest' })
    })
  }

  const ordered = [...shown].sort((a, b) => b.onDuty - a.onDuty)
  const emptyCount = shown.filter((s) => s.onDuty === 0).length
  const listed = emptyOnly ? ordered.filter((s) => s.onDuty === 0) : ordered

  return (
    <div className="hq-mapwrap">
      {companies.length > 1 && (
        <div className="hq-map-filters">
          <button
            type="button"
            className={`hq-map-filter${onlyCompany === null ? ' is-on' : ''}`}
            onClick={() => { setOnlyCompany(null); setFocused(null) }}
          >
            Hamısı <b>{sites.length}</b>
          </button>
          {companies.map((c, i) => {
            const n = sites.filter((s) => s.companyIndex === i).length
            if (n === 0) return null
            return (
              <button
                key={c.id}
                type="button"
                className={`hq-map-filter${onlyCompany === i ? ' is-on' : ''}`}
                style={{ ['--filter-accent' as string]: accentOf(i) }}
                onClick={() => { setOnlyCompany(onlyCompany === i ? null : i); setFocused(null) }}
              >
                <i style={{ background: accentOf(i) }} />
                {c.name} <b>{n}</b>
              </button>
            )
          })}
        </div>
      )}

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
          <TileLayer
            url={BASE.url}
            attribution={BASE.attribution}
            subdomains={BASE.subdomains}
            maxZoom={BASE.maxZoom}
            // Only when standing in with light tiles; the real dark_all needs no help.
            className={BASE.needsDarkFilter ? 'tiles-dark' : undefined}
          />
          <FitTo sites={shown} cluster={onlyCompany === null} register={setActions} />
          {shown.map((s) => {
            const colour = accentOf(s.companyIndex < 0 ? 0 : s.companyIndex)
            const live = s.onDuty > 0
            const isFocused = focused === s.id
            const company = companies[s.companyIndex]?.name ?? ''
            return (
              // Fragment, not a wrapper element: react-leaflet renders children into the map
              // container, and a stray div there sits on top of the map and swallows clicks.
              <Fragment key={s.id}>
                {/* The geofence, to scale — for the site being looked at, or for a whole company once
                    one is filtered to.
                    Drawn for EVERY site at once it stopped being information and became a stain:
                    these are real-world radii (500 m for a park, 2,000 m for one site) and at group
                    zoom the circles of central Baku merge into a single smear that hides the markers
                    they were meant to explain. Narrowed to one company there are few enough to read,
                    and that is exactly when someone is asking about coverage. */}
                {(isFocused || onlyCompany !== null) && (
                  <Circle
                    center={[s.lat, s.lng]}
                    radius={s.radiusMeters}
                    pathOptions={{
                      color: colour,
                      fillColor: colour,
                      fillOpacity: isFocused ? 0.09 : 0.04,
                      opacity: isFocused ? 0.7 : 0.3,
                      weight: 1,
                      dashArray: '4 5',
                    }}
                  />
                )}
                <Marker
                  position={[s.lat, s.lng]}
                  icon={markerIcon(s.onDuty, colour, isFocused, hovered === s.id)}
                  // Busiest on top. Leaflet stacks markers by latitude, which is arbitrary here and
                  // had the consequence that the branch with 67 people sat UNDER two dots reading 7
                  // and 1 — the one number worth seeing was the one hidden. Ranking by headcount puts
                  // the important dot in front; whatever is being pointed at beats both.
                  zIndexOffset={isFocused || hovered === s.id ? 10000 : s.onDuty * 10}
                  eventHandlers={{
                    click: () => focus(s),
                    mouseover: () => setHovered(s.id),
                    mouseout: () => setHovered(null),
                  }}
                >
                  <Popup className="hq-pop" closeButton={false}>
                    <div className="hq-pop-box">
                      <div className="hq-pop-top">
                        <span className="hq-pop-co" style={{ color: colour }}>{company}</span>
                        <span className={`hq-pop-state ${live ? 'is-live' : 'is-idle'}`}>
                          {live ? 'iş gedir' : 'hazırda boş'}
                        </span>
                      </div>
                      <div className="hq-pop-name">{s.name}</div>
                      <div className="hq-pop-count hq-num">
                        <b>{s.onDuty}</b>
                        {s.staff > 0 && <small> / {s.staff} nəfər heyət</small>}
                      </div>
                      <div className="hq-pop-geo">GPS nəzarət zonası · {s.radiusMeters} m</div>
                    </div>
                  </Popup>
                </Marker>
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
            onClick={() => { setFocused(null); actions?.fit(onlyCompany === null ? sites : shown) }}
          >
            Hamısı
          </button>
        </div>

        {!wheelArmed && shown.length > 0 && (
          <div className="hq-map-hint">Yaxınlaşdırmaq üçün xəritəyə klikləyin</div>
        )}

        {/* Over the map, because it is a fact ABOUT the map: an empty branch draws the smallest
            marker there is, so absence is the one thing these dots cannot say for themselves.
            Pressable — it states a fact whose obvious next question is «which ones». */}
        {emptyCount > 0 && (
          <button
            type="button"
            className={`hq-site-note${emptyOnly ? ' is-on' : ''}`}
            onClick={() => setEmptyOnly((v) => !v)}
            title={emptyOnly ? 'Bütün filiallara qayıt' : 'Yalnız boşları göstər'}
          >
            <em>{emptyCount}</em>
            <span>filialda hazırda heç kim yoxdur</span>
            <b>{emptyOnly ? '✕' : '→'}</b>
          </button>
        )}
      </div>

      {/* Names, readable, ordered by where the work is. Clicking one flies the map to it — the single
          most useful thing to be able to do while someone is watching. */}
      <ul className="hq-sitelist">
        {listed.map((s) => (
          <li
            id={`hq-site-${s.id}`}
            key={s.id}
            className={`hq-site${focused === s.id ? ' is-focused' : ''}${hovered === s.id ? ' is-hovered' : ''}${s.onDuty === 0 ? ' is-idle' : ''}`}
            onClick={() => focus(s)}
            onMouseEnter={() => setHovered(s.id)}
            onMouseLeave={() => setHovered(null)}
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
