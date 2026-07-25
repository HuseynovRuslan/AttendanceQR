import { useEffect, useMemo } from 'react'
import { Circle, CircleMarker, MapContainer, TileLayer, Tooltip, useMap } from 'react-leaflet'
import 'leaflet/dist/leaflet.css'

/**
 * The company's sites on a light map, each sized by how many people are standing at work there right
 * now. The single-company answer to the group board's map: no companies to colour by, so the signal
 * is live headcount per site.
 *
 * Deliberately light (Apple-plain white tiles, one green accent) rather than the group board's dark
 * glass — this lives inside the admin panel, next to white cards, and a dark slab would fight them.
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

function FitTo({ sites }: { sites: DashSite[] }) {
  const map = useMap()
  const key = sites.map((s) => s.id).join(',')
  useEffect(() => {
    if (sites.length === 0) return
    const sub = densestCluster(sites)
    if (sub.length === 1) { map.setView([sub[0].lat, sub[0].lng], 14); return }
    map.fitBounds(sub.map((s) => [s.lat, s.lng] as [number, number]), { padding: [40, 40], maxZoom: 14 })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key])
  return null
}

export function DashboardMap({ sites }: { sites: DashSite[] }) {
  const centre = useMemo<[number, number]>(() => {
    if (sites.length === 0) return [40.4093, 49.8671] // Baku
    const c = densestCluster(sites)
    return [c.reduce((s, x) => s + x.lat, 0) / c.length, c.reduce((s, x) => s + x.lng, 0) / c.length]
  }, [sites])

  // Marker radius carries the headcount. Square-root, not linear, so one busy site doesn't swell into
  // a blob that swallows its neighbours.
  const busiest = Math.max(1, ...sites.map((s) => s.onDuty))
  const radiusOf = (n: number) => 8 + Math.sqrt(n / busiest) * 16

  if (sites.length === 0) return null

  return (
    <div className="lux-map">
      <MapContainer
        center={centre}
        zoom={12}
        scrollWheelZoom={false}
        zoomControl={false}
        attributionControl={false}
        style={{ height: '100%', width: '100%', background: '#eef2f7' }}
      >
        <TileLayer url="https://{s}.basemaps.cartocdn.com/light_all/{z}/{x}/{y}{r}.png" />
        <FitTo sites={sites} />
        {sites.map((s) => {
          const live = s.onDuty > 0
          const colour = live ? '#22a06b' : '#b9c2cf'
          return (
            <Circle
              key={`geo-${s.id}`}
              center={[s.lat, s.lng]}
              radius={s.radiusMeters}
              pathOptions={{ color: colour, fillColor: colour, fillOpacity: 0.06, weight: 1 }}
            />
          )
        })}
        {sites.map((s) => {
          const live = s.onDuty > 0
          const colour = live ? '#16a34a' : '#94a3b8'
          return (
            <CircleMarker
              key={`pin-${s.id}`}
              center={[s.lat, s.lng]}
              radius={radiusOf(s.onDuty)}
              pathOptions={{ color: '#fff', weight: 2, fillColor: colour, fillOpacity: live ? 0.9 : 0.5 }}
            >
              <Tooltip direction="top">
                <b>{s.name}</b><br />
                {s.onDuty} / {s.staff} işdə
              </Tooltip>
            </CircleMarker>
          )
        })}
      </MapContainer>
    </div>
  )
}
