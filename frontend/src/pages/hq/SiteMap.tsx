import { useEffect, useMemo } from 'react'
import { CircleMarker, MapContainer, TileLayer, Tooltip, useMap } from 'react-leaflet'
import type { GroupSite } from '../../api/hq'

/** Fits the view to every site once the list arrives, so the board never opens on the wrong city or
 *  zoomed into a car park. Re-fits only when the set of sites changes, not on every refresh — the
 *  map jumping every twenty seconds while someone is looking at it would be worse than useless. */
function FitToSites({ sites }: { sites: GroupSite[] }) {
  const map = useMap()
  const key = sites.map((s) => s.id).join(',')

  useEffect(() => {
    if (sites.length === 0) return
    if (sites.length === 1) {
      map.setView([sites[0].lat, sites[0].lng], 13)
      return
    }
    map.fitBounds(sites.map((s) => [s.lat, s.lng] as [number, number]), { padding: [42, 42] })
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

  return (
    <div className="hq-map">
      <MapContainer
        center={centre}
        zoom={11}
        scrollWheelZoom={false}
        zoomControl={false}
        attributionControl={false}
        style={{ height: '100%', width: '100%', background: '#0B1020' }}
      >
        <TileLayer url="https://{s}.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}{r}.png" />
        <FitToSites sites={sites} />
        {sites.map((s) => {
          const colour = accentOf(s.companyIndex < 0 ? 0 : s.companyIndex)
          const live = s.onDuty > 0
          return (
            <CircleMarker
              key={s.id}
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
              <Tooltip direction="top" offset={[0, -6]} opacity={1} className="hq-tip">
                <b>{s.name}</b>
                <br />
                {s.onDuty > 0 ? `${s.onDuty} nəfər iş başında` : 'hazırda boş'}
              </Tooltip>
            </CircleMarker>
          )
        })}
      </MapContainer>
    </div>
  )
}
