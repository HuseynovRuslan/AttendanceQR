import { useEffect, useMemo, useState } from 'react'
import { Circle, CircleMarker, MapContainer, TileLayer, Tooltip, useMap, useMapEvents } from 'react-leaflet'
import 'leaflet/dist/leaflet.css'

/** Below this zoom, individual people pile into an unreadable blob at a small site, so the map shows
 *  one marker per site sized by its headcount. Zoom in past it and the people themselves appear. */
const PEOPLE_ZOOM = 15

/**
 * The company's people on a light map — each a dot where they actually scanned in, green if still on
 * duty, blue once they've checked out. The single-company answer to the group board's map: no
 * companies to colour by, so the signal is the real spread of people across the sites right now.
 *
 * Where a person's scan position isn't known (a record from before check-in coordinates were kept,
 * or an admin-created one), the site itself stands in — one marker at the site centre sized by its
 * headcount. So the map is never empty just because history has no coordinates.
 *
 * Deliberately light (Apple-plain white tiles) rather than the group board's dark glass — this lives
 * in the admin panel next to white cards, and a dark slab would fight them.
 */

export interface DashSite {
  id: string
  name: string
  lat: number
  lng: number
  radiusMeters: number
  onDuty: number
  staff: number
}

export interface DashPerson {
  id: string
  name: string
  lat: number
  lng: number
  onDuty: boolean
  siteName: string
}

/** ~55 km — sites within this of each other are one working area, so the map opens on the city the
 *  company actually operates in rather than framing a lone outlier and collapsing the rest. */
const CLUSTER_DEG = 0.5

function densestCluster(sites: DashSite[]): DashSite[] {
  if (sites.length < 2) return sites
  const near = (a: DashSite, b: DashSite) =>
    Math.abs(a.lat - b.lat) < CLUSTER_DEG && Math.abs(a.lng - b.lng) < CLUSTER_DEG
  let best: DashSite[] = []
  for (const anchor of sites) {
    const group = sites.filter((s) => near(anchor, s))
    if (group.length > best.length) best = group
  }
  return best.length > 1 ? best : sites
}

function FitTo({ sites, people }: { sites: DashSite[]; people: DashPerson[] }) {
  const map = useMap()
  const key = sites.map((s) => s.id).join(',') + '::' + people.length
  useEffect(() => {
    // Frame the densest cluster of sites; if we have individual people, include them too.
    const sub = densestCluster(sites)
    const pts: [number, number][] = [
      ...sub.map((s) => [s.lat, s.lng] as [number, number]),
      ...people
        .filter((p) => sub.some((s) => Math.abs(s.lat - p.lat) < CLUSTER_DEG && Math.abs(s.lng - p.lng) < CLUSTER_DEG))
        .map((p) => [p.lat, p.lng] as [number, number]),
    ]
    if (pts.length === 0) return
    if (pts.length === 1) { map.setView(pts[0], 15); return }
    map.fitBounds(pts, { padding: [40, 40], maxZoom: 16 })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key])
  return null
}

/** Tracks the current zoom so the map can switch between site markers and individual people. */
function ZoomWatch({ onZoom }: { onZoom: (z: number) => void }) {
  const map = useMapEvents({ zoomend: () => onZoom(map.getZoom()) })
  return null
}

export function DashboardMap({ sites, people }: { sites: DashSite[]; people: DashPerson[] }) {
  const [zoom, setZoom] = useState(13)

  const centre = useMemo<[number, number]>(() => {
    if (sites.length === 0) return [40.4093, 49.8671] // Baku
    const c = densestCluster(sites)
    return [c.reduce((s, x) => s + x.lat, 0) / c.length, c.reduce((s, x) => s + x.lng, 0) / c.length]
  }, [sites])

  // Site markers by default (clean at any density); individual people once zoomed in, when they no
  // longer overlap. If there are no site coordinates at all, people are all we have — show them.
  const showPeople = people.length > 0 && (zoom >= PEOPLE_ZOOM || sites.length === 0)
  const showSiteMarkers = !showPeople
  const busiest = Math.max(1, ...sites.map((s) => s.onDuty))
  const siteRadius = (n: number) => 8 + Math.sqrt(n / busiest) * 16

  if (sites.length === 0 && people.length === 0) return null

  return (
    <div className="lux-map">
      <MapContainer
        center={centre}
        zoom={13}
        scrollWheelZoom={false}
        zoomControl={false}
        attributionControl={false}
        style={{ height: '100%', width: '100%', background: '#eef2f7' }}
      >
        <TileLayer url="https://{s}.basemaps.cartocdn.com/light_all/{z}/{x}/{y}{r}.png" />
        <FitTo sites={sites} people={people} />
        <ZoomWatch onZoom={setZoom} />

        {/* Geofence circles — faint context for the dots. */}
        {sites.map((s) => (
          <Circle
            key={`geo-${s.id}`}
            center={[s.lat, s.lng]}
            radius={s.radiusMeters}
            pathOptions={{ color: '#94a3b8', fillColor: '#94a3b8', fillOpacity: 0.05, weight: 1 }}
          />
        ))}

        {/* Individual people, once zoomed in enough that they don't overlap. */}
        {showPeople && people.map((p) => {
          const colour = p.onDuty ? '#16a34a' : '#2563eb'
          return (
            <CircleMarker
              key={`p-${p.id}`}
              center={[p.lat, p.lng]}
              radius={7}
              pathOptions={{ color: '#fff', weight: 2, fillColor: colour, fillOpacity: 0.92 }}
            >
              <Tooltip direction="top">
                <b>{p.name}</b><br />
                {p.onDuty ? 'işdə' : 'çıxış edib'} · {p.siteName}
              </Tooltip>
            </CircleMarker>
          )
        })}

        {/* Fallback: no individual coordinates yet — mark the sites, sized by headcount. */}
        {showSiteMarkers && sites.map((s) => {
          const live = s.onDuty > 0
          return (
            <CircleMarker
              key={`site-${s.id}`}
              center={[s.lat, s.lng]}
              radius={siteRadius(s.onDuty)}
              pathOptions={{ color: '#fff', weight: 2, fillColor: live ? '#16a34a' : '#94a3b8', fillOpacity: live ? 0.85 : 0.5 }}
            >
              <Tooltip direction="top"><b>{s.name}</b><br />{s.onDuty} / {s.staff} işdə</Tooltip>
            </CircleMarker>
          )
        })}
      </MapContainer>
    </div>
  )
}
