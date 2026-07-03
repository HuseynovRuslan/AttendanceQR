import { apiRequest } from './client'

export interface TokenResponse {
  token: string
  employeeId?: string
}

export interface ApiErrorBody {
  error: string
}

/** POST /api/auth/login — email + password → JWT. */
export function login(email: string, password: string) {
  return apiRequest<TokenResponse | ApiErrorBody>('/api/auth/login', {
    method: 'POST',
    auth: false,
    body: { email, password },
  })
}

/** POST /api/auth/activate — activation token + new PIN + device fingerprint (+ friendly device name) → JWT. */
export function activate(
  activationToken: string,
  password: string,
  deviceFingerprint: string,
  deviceLabel?: string,
) {
  return apiRequest<TokenResponse | ApiErrorBody>('/api/auth/activate', {
    method: 'POST',
    auth: false,
    body: { activationToken, password, deviceFingerprint, deviceLabel },
  })
}

/** POST /api/auth/change-password — current + new PIN → a freshly issued JWT (every other
 * outstanding token for this account stops working, per the backend's TokenVersion check). */
export function changePassword(currentPassword: string, newPassword: string) {
  return apiRequest<TokenResponse | ApiErrorBody>('/api/auth/change-password', {
    method: 'POST',
    body: { currentPassword, newPassword },
  })
}
