import { useEffect, useMemo } from 'react'
import { Circle, CircleMarker, MapContainer, TileLayer, Tooltip, useMap } from 'react-leaflet'
import 'leaflet/dist/leaflet.css'

/**
 * The company's people on a light map — each a dot where they actually scanned in, green if still on
 * duty, blue once they've checked out. The single-company answer to the group board's map: no
 * companies to colour by, so the signal is the real spread of people across the sites right now.
 *
 * Both layers are always drawn, on purpose: the site itself (its geofence and centre) marks the
 * location, and every employee with a known scan position gets their own dot on top of it — the
 * "where did each person actually clock in" the admin asked to see, not just where the site is.
 * The wheel zooms (scrollWheelZoom), so overlapping dots at a busy site can be separated by zooming.
 *
 * A person whose scan position isn't known (a record from before check-in coordinates were kept, or
 * an admin-created one) simply has no dot; the site centre still stands for the location.
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
    if (pts.length === 1) { map.setView(pts[0], 16); return }
    map.fitBounds(pts, { padding: [40, 40], maxZoom: 17 })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key])
  return null
}

export function DashboardMap({ sites, people }: { sites: DashSite[]; people: DashPerson[] }) {
  const centre = useMemo<[number, number]>(() => {
    if (sites.length === 0) return [40.4093, 49.8671] // Baku
    const c = densestCluster(sites)
    return [c.reduce((s, x) => s + x.lat, 0) / c.length, c.reduce((s, x) => s + x.lng, 0) / c.length]
  }, [sites])

  if (sites.length === 0 && people.length === 0) return null

  return (
    <div className="lux-map">
      <MapContainer
        center={centre}
        zoom={14}
        scrollWheelZoom={true}
        zoomControl={true}
        doubleClickZoom={true}
        attributionControl={false}
        style={{ height: '100%', width: '100%', background: '#eef2f7' }}
      >
        <TileLayer url="https://{s}.basemaps.cartocdn.com/light_all/{z}/{x}/{y}{r}.png" />
        <FitTo sites={sites} people={people} />

        {/* Location layer — geofence + a small centre pin, always drawn so the site is marked even
            when nobody there has a known scan position. */}
        {sites.map((s) => (
          <Circle
            key={`geo-${s.id}`}
            center={[s.lat, s.lng]}
            radius={s.radiusMeters}
            pathOptions={{ color: '#94a3b8', fillColor: '#94a3b8', fillOpacity: 0.06, weight: 1 }}
          />
        ))}
        {sites.map((s) => (
          <CircleMarker
            key={`site-${s.id}`}
            center={[s.lat, s.lng]}
            radius={5}
            pathOptions={{ color: '#fff', weight: 2, fillColor: '#64748b', fillOpacity: 0.9 }}
          >
            <Tooltip direction="top"><b>{s.name}</b><br />{s.onDuty} / {s.staff} işdə</Tooltip>
          </CircleMarker>
        ))}

        {/* Each employee at their real scan coordinate: green = still on duty, blue = checked out. */}
        {people.map((p) => {
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
      </MapContainer>
    </div>
  )
}
