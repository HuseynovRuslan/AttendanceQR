import { useEffect, useMemo } from 'react'
import { Circle, CircleMarker, MapContainer, TileLayer, Tooltip, useMap } from 'react-leaflet'
import 'leaflet/dist/leaflet.css'
import type { MapGeofence, ProblemRow } from '../../api/admin'
import { fmtTime } from '../../lib/format'

/**
 * Where the "outside the radius" rejections actually happened, drawn against the geofence they fell
 * outside of.
 *
 * The point of the whole thing: a cluster of red dots on one side of the circle says the centre is on
 * the wrong spot; dots hugging the edge say the site is bigger than the radius; dots scattered
 * kilometres away say someone scanned from home. None of that is legible in a list of names — it only
 * reads on a map.
 *
 * Only OutsideRadius rows carry coordinates (the scan sent a position; the server stored it on
 * rejection). Every other reason — GPS blocked, device, token — never had a position to plot.
 */

export interface RejectPoint {
  lat: number
  lng: number
  distanceM: number
  name: string
  atUtc: string
}

/** Parse the "lat,lng,dist" the server stashed in an OutsideRadius row's detail. Returns null for a
 *  row without coordinates (an older rejection, from before capture existed). */
export function parseRejectPoints(rows: ProblemRow[]): RejectPoint[] {
  const out: RejectPoint[] = []
  for (const r of rows) {
    if (r.reason !== 'OutsideRadius' || !r.detail) continue
    const parts = r.detail.split(',')
    if (parts.length < 3) continue
    const lat = Number(parts[0])
    const lng = Number(parts[1])
    const dist = Number(parts[2])
    if (!Number.isFinite(lat) || !Number.isFinite(lng)) continue
    out.push({ lat, lng, distanceM: dist, name: r.employeeName, atUtc: r.atUtc })
  }
  return out
}

/** Frames the map to hold every geofence circle and every rejected point, once, when they change. */
function FitBounds({ pts, fences }: { pts: RejectPoint[]; fences: MapGeofence[] }) {
  const map = useMap()
  const key =
    pts.map((p) => `${p.lat},${p.lng}`).join('|') + '::' + fences.map((f) => `${f.latitude},${f.longitude}`).join('|')
  useEffect(() => {
    const coords: [number, number][] = [
      ...fences.map((f) => [f.latitude, f.longitude] as [number, number]),
      ...pts.map((p) => [p.lat, p.lng] as [number, number]),
    ]
    if (coords.length === 0) return
    if (coords.length === 1) { map.setView(coords[0], 15); return }
    map.fitBounds(coords, { padding: [40, 40], maxZoom: 16 })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key])
  return null
}

export function ProblemsMap({ rows, geofences }: { rows: ProblemRow[]; geofences: MapGeofence[] }) {
  const points = useMemo(() => parseRejectPoints(rows), [rows])

  const centre = useMemo<[number, number]>(() => {
    if (geofences.length > 0) return [geofences[0].latitude, geofences[0].longitude]
    if (points.length > 0) return [points[0].lat, points[0].lng]
    return [40.4093, 49.8671] // Baku
  }, [geofences, points])

  // Nothing to draw: no geofence to anchor on, or no captured points yet (capture is new — older
  // rejections have no coordinates, so the map fills in over the next day or two).
  if (geofences.length === 0 && points.length === 0) return null

  return (
    <div className="card" style={{ marginBottom: 14, overflow: 'hidden' }}>
      <div className="card-pad" style={{ paddingBottom: 8 }}>
        <div style={{ fontWeight: 700, color: 'var(--c900)' }}>🗺️ Rədlər xəritədə</div>
        <div className="muted" style={{ fontSize: 12, marginTop: 2 }}>
          Mavi dairə — icazə verilən ərazi. Qırmızı nöqtələr — «iş yerindən kənarda» rədd alınan yerlər.
          Nöqtələr bir tərəfə yığılıbsa mərkəz səhvdir; kənarda düzülübsə ərazi radiusdan böyükdür.
        </div>
        {points.length === 0 && (
          <div className="fb fb-info" style={{ marginTop: 8, fontSize: 12 }}>
            <span>
              Hələ koordinatlı nöqtə yoxdur — rədlərin yerini qeyd etmə bu gün əlavə olundu. Yeni
              «iş yerindən kənarda» rədləri gəldikcə xəritədə görünəcək. Aşağıdakı dairə isə sistemin
              bu ərazini harada bildiyini göstərir.
            </span>
          </div>
        )}
      </div>
      <div className="lux-map" style={{ height: 380, width: '100%' }}>
        <MapContainer
          center={centre}
          zoom={14}
          scrollWheelZoom={false}
          style={{ height: '100%', width: '100%' }}
        >
          <TileLayer
            url="https://{s}.basemaps.cartocdn.com/light_all/{z}/{x}/{y}{r}.png"
            attribution="&copy; OpenStreetMap &copy; CARTO"
          />
          <FitBounds pts={points} fences={geofences} />

          {geofences.map((f) => (
            <Circle
              key={`${f.locationName}-${f.latitude}-${f.longitude}`}
              center={[f.latitude, f.longitude]}
              radius={f.radiusMeters}
              pathOptions={{ color: '#2563eb', fillColor: '#3b82f6', fillOpacity: 0.08, weight: 2 }}
            >
              <Tooltip>{f.locationName} · {f.radiusMeters} m radius</Tooltip>
            </Circle>
          ))}

          {points.map((p, i) => (
            <CircleMarker
              key={`${p.lat}-${p.lng}-${i}`}
              center={[p.lat, p.lng]}
              radius={6}
              pathOptions={{ color: '#dc2626', fillColor: '#ef4444', fillOpacity: 0.85, weight: 1.5 }}
            >
              <Tooltip>
                <b>{p.name}</b><br />
                {fmtTime(p.atUtc)} · {p.distanceM} m kənarda
              </Tooltip>
            </CircleMarker>
          ))}
        </MapContainer>
      </div>
    </div>
  )
}
