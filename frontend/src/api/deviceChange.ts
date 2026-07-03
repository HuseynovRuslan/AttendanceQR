import { apiRequest } from './client'

export interface DeviceChangeRequestResult {
  requestId: string
  status: 'Pending'
}

export interface ApiErrorBody {
  error: string
}

/** POST /api/device-change/request — ask admin approval to bind a new device to this account. */
export function requestDeviceChange(newDeviceFingerprint: string) {
  return apiRequest<DeviceChangeRequestResult | ApiErrorBody>('/api/device-change/request', {
    method: 'POST',
    body: { newDeviceFingerprint },
  })
}
