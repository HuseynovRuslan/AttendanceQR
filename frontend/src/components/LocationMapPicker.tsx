import { useEffect } from 'react'
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

// Keeps the map view following the coordinates whether they changed via a map click or the
// admin typing directly into the lat/lng number fields — both are the same "set the point" action.
function RecenterOnChange({ lat, lng }: { lat: number; lng: number }) {
  const map = useMap()
  useEffect(() => {
    map.setView([lat, lng], map.getZoom())
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [lat, lng])
  return null
}

/**
 * Click-to-pick location map (Leaflet + free OpenStreetMap tiles, no API key). Renders the
 * current point as a plain colored dot — deliberately not the default Leaflet Marker, whose icon
 * image path breaks under most bundlers without extra asset config — plus a translucent circle
 * showing the geofence radius, so the admin can see exactly where check-in will be accepted.
 */
export function LocationMapPicker({ latitude, longitude, radiusMeters, onPick }: LocationMapPickerProps) {
  return (
    <div style={{ borderRadius: 12, overflow: 'hidden', border: '1px solid var(--c200)' }}>
      <MapContainer
        center={[latitude, longitude]}
        zoom={15}
        style={{ height: 320, width: '100%' }}
        scrollWheelZoom
      >
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
  )
}
