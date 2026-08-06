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

/** POST /api/auth/login — email + password → JWT. Tenant comes from the subdomain (web). */
export function login(email: string, password: string) {
  return apiRequest<TokenResponse | ApiErrorBody>('/api/auth/login', {
    method: 'POST',
    auth: false,
    body: { email, password },
  })
}

/** POST /api/auth/app-login — the single-URL native app has no company subdomain, so the tenant is
 *  resolved from the credentials (searched across all companies). Same shape as login. */
export function appLogin(email: string, password: string) {
  return apiRequest<TokenResponse | ApiErrorBody>('/api/auth/app-login', {
    method: 'POST',
    auth: false,
    body: { email, password },
  })
}

/** POST /api/auth/operator-login — the platform operator console (admin.qrlog.az). Like app-login it
 *  resolves the account across every company, but additionally requires the super-admin allowlist; a
 *  non-operator fails exactly like a wrong PIN, so it never reveals who is an operator. */
export function operatorLogin(email: string, password: string) {
  return apiRequest<TokenResponse | ApiErrorBody>('/api/auth/operator-login', {
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

/** POST /api/auth/forgot-pin/verify — self-service reset: prove it's you with a selfie (matched to your
 *  reference photo) from a device already bound to your account. On success returns a fresh temp PIN;
 *  otherwise `verified: false` (the caller then offers the admin-queue path). */
export function forgotPinVerify(identifier: string, deviceFingerprint: string, photoBase64: string) {
  return apiRequest<{ verified: boolean; pin?: string } | ApiErrorBody>('/api/auth/forgot-pin/verify', {
    method: 'POST',
    auth: false,
    body: { identifier, deviceFingerprint, photoBase64 },
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
