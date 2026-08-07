import { useEffect, useState, type FormEvent } from 'react'
import { Circle, CircleMarker, MapContainer, TileLayer, useMap, useMapEvents } from 'react-leaflet'
import 'leaflet/dist/leaflet.css'

interface LocationMapPickerProps {
  latitude: number
  longitude: number
  radiusMeters: number
  onPick: (lat: number, lng: number) => void
}

function ClickHandler({ onPick }: { onPick: (lat: number, lng: number) => void }) {
  useMapEvents({
    click(e) {
      onPick(e.latlng.lat, e.latlng.lng)
    },
  })
  return null
}

// Keeps the map view following the coordinates whether they changed via a map click, a search result,
// or the admin typing directly into the lat/lng number fields — all the same "set the point" action.
function RecenterOnChange({ lat, lng }: { lat: number; lng: number }) {
  const map = useMap()
  useEffect(() => {
    map.setView([lat, lng], map.getZoom())
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [lat, lng])
  return null
}

interface GeoHit {
  lat: number
  lng: number
  label: string
}

/**
 * Click-to-pick location map (Leaflet + free OpenStreetMap tiles, no API key) with an address SEARCH
 * (Nominatim geocoding, also free / no key) so a place can be found by name instead of hunting the map
 * by hand. Renders the current point as a plain colored dot — deliberately not the default Leaflet
 * Marker, whose icon path breaks under most bundlers — plus a translucent circle showing the radius.
 */
export function LocationMapPicker({ latitude, longitude, radiusMeters, onPick }: LocationMapPickerProps) {
  const [q, setQ] = useState('')
  const [hits, setHits] = useState<GeoHit[]>([])
  const [searching, setSearching] = useState(false)
  const [noHit, setNoHit] = useState(false)

  async function search(e?: FormEvent) {
    e?.preventDefault()
    const term = q.trim()
    if (term.length < 3) return
    setSearching(true)
    setNoHit(false)
    try {
      // Biased to Azerbaijan (every tenant is here), localised place names.
      const url =
        `https://nominatim.openstreetmap.org/search?format=json&limit=6&accept-language=az&countrycodes=az&q=${encodeURIComponent(term)}`
      const res = await fetch(url, { headers: { Accept: 'application/json' } })
      const data = (await res.json()) as Array<{ lat: string; lon: string; display_name: string }>
      const mapped = Array.isArray(data)
        ? data.map((d) => ({ lat: parseFloat(d.lat), lng: parseFloat(d.lon), label: d.display_name }))
        : []
      setHits(mapped)
      setNoHit(mapped.length === 0)
    } catch {
      setHits([])
      setNoHit(true)
    } finally {
      setSearching(false)
    }
  }

  function choose(h: GeoHit) {
    onPick(h.lat, h.lng)
    setHits([])
    setNoHit(false)
    setQ(h.label.split(',').slice(0, 2).join(',').trim())
  }

  return (
    <div style={{ position: 'relative' }}>
      <form onSubmit={search} style={{ display: 'flex', gap: 6, marginBottom: 8 }}>
        <input
          className="inp"
          value={q}
          onChange={(e) => {
            setQ(e.target.value)
            setNoHit(false)
          }}
          placeholder="Ünvan / yer axtar (məs. Nərimanov parkı, Bakı)"
          style={{ flex: 1 }}
        />
        <button type="submit" className="btn btn-sm" disabled={searching || q.trim().length < 3}>
          {searching ? '…' : '🔍 Axtar'}
        </button>
      </form>

      {(hits.length > 0 || noHit) && (
        <div
          className="card"
          style={{ position: 'absolute', top: 46, left: 0, right: 0, zIndex: 1100, maxHeight: 220, overflowY: 'auto', boxShadow: '0 8px 24px rgba(0,0,0,.16)' }}
        >
          {noHit ? (
            <div className="muted" style={{ padding: '10px 12px', fontSize: 13 }}>Nəticə tapılmadı</div>
          ) : (
            hits.map((h, i) => (
              <button
                key={i}
                type="button"
                onClick={() => choose(h)}
                style={{ display: 'block', width: '100%', textAlign: 'left', padding: '9px 12px', border: 'none', borderBottom: '1px solid var(--c50)', background: 'transparent', cursor: 'pointer', fontSize: 13, color: 'var(--c700)' }}
              >
                {h.label}
              </button>
            ))
          )}
        </div>
      )}

      <div style={{ borderRadius: 12, overflow: 'hidden', border: '1px solid var(--c200)' }}>
        <MapContainer center={[latitude, longitude]} zoom={15} style={{ height: 230, width: '100%' }} scrollWheelZoom>
          <TileLayer
            attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors'
            url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png"
          />
          <ClickHandler onPick={onPick} />
          <RecenterOnChange lat={latitude} lng={longitude} />
          {radiusMeters > 0 && (
            <Circle
              center={[latitude, longitude]}
              radius={radiusMeters}
              pathOptions={{ color: '#7CB342', fillColor: '#7CB342', fillOpacity: 0.12, weight: 1.5 }}
            />
          )}
          <CircleMarker
            center={[latitude, longitude]}
            radius={8}
            pathOptions={{ color: '#4E7D26', fillColor: '#7CB342', fillOpacity: 1, weight: 2 }}
          />
        </MapContainer>
      </div>
      <div className="muted" style={{ fontSize: 11, marginTop: 4 }}>
        Axtarın və nəticəni seçin, yaxud xəritəyə toxunaraq dəqiq nöqtəni qoyun.
      </div>
    </div>
  )
}
