import { apiRequest } from './client'

export interface KioskTokenResponse {
  token: string
  locationId: string
  refreshInSeconds: number
}

export interface KioskLocation {
  id: string
  name: string
}

/** Signed QR token for a location. Anonymous — no JWT attached. */
export function getKioskToken(locationId: string) {
  return apiRequest<KioskTokenResponse>(`/api/kiosk/token/${locationId}`, { auth: false })
}

/** Location name (and existence check) for the kiosk header. Anonymous. */
export function getKioskLocation(locationId: string) {
  return apiRequest<KioskLocation | { error: string }>(`/api/kiosk/location/${locationId}`, {
    auth: false,
  })
}
