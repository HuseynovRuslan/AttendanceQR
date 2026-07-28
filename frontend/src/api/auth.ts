import { apiRequest } from './client'

export interface TokenResponse {
  token: string
  employeeId?: string
  /** set-initial-pin only: true only for a brand-new account that still needs to enrol a reference
   *  selfie. False after an admin PIN reset (the employee already has one), so the client skips it. */
  needsReferencePhoto?: boolean
}

export interface ApiErrorBody {
  error: string
  /** Login lockout: how many minutes the cool-off lasts (on TooManyAttempts). */
  minutes?: number
  /** Login: attempts left before lockout (on InvalidCredentials). */
  remaining?: number
}

/** POST /api/auth/login — email + password → JWT. */
export function login(email: string, password: string) {
  return apiRequest<TokenResponse | ApiErrorBody>('/api/auth/login', {
    method: 'POST',
    auth: false,
    body: { email, password },
  })
}

/** POST /api/auth/forgot-pin — an employee who forgot their PIN (and so can't sign in) asks the admin
 *  to reset it. Always resolves the same way whether or not the identifier matches an account (no
 *  existence leak); the request shows up in the admin's queue. */
export function forgotPin(identifier: string) {
  return apiRequest<{ ok: boolean } | ApiErrorBody>('/api/auth/forgot-pin', {
    method: 'POST',
    auth: false,
    body: { identifier },
  })
}

/** POST /api/auth/activate — activation token + new PIN + device fingerprint (+ friendly device name) → JWT. */
export function activate(
  activationToken: string,
  password: string,
  deviceFingerprint: string,
  deviceLabel?: string,
  photoBase64?: string,
) {
  return apiRequest<TokenResponse | ApiErrorBody>('/api/auth/activate', {
    method: 'POST',
    auth: false,
    body: { activationToken, password, deviceFingerprint, deviceLabel, ...(photoBase64 ? { photoBase64 } : {}) },
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

/** POST /api/auth/set-initial-pin — first-time PIN for an account still on a temporary PIN (no current
 * PIN asked, since they just signed in with the temp one) → a fresh JWT without the "mcp" flag. */
export function setInitialPin(newPin: string) {
  return apiRequest<TokenResponse | ApiErrorBody>('/api/auth/set-initial-pin', {
    method: 'POST',
    body: { newPin },
  })
}
