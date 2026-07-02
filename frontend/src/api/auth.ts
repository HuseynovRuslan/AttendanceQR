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

/** POST /api/auth/activate — activation token + new password + device fingerprint → JWT. */
export function activate(activationToken: string, password: string, deviceFingerprint: string) {
  return apiRequest<TokenResponse | ApiErrorBody>('/api/auth/activate', {
    method: 'POST',
    auth: false,
    body: { activationToken, password, deviceFingerprint },
  })
}
