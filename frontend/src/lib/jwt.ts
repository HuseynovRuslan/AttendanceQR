export type Role = 'Admin' | 'Manager' | 'Employee'

export interface JwtClaims {
  sub?: string
  email?: string
  role?: Role
  exp?: number
}

/** Decodes a JWT payload (no signature check — that's the backend's job). Returns null if malformed. */
export function decodeJwt(token: string): JwtClaims | null {
  try {
    const payload = token.split('.')[1]
    if (!payload) return null
    const json = atob(payload.replace(/-/g, '+').replace(/_/g, '/'))
    return JSON.parse(json) as JwtClaims
  } catch {
    return null
  }
}
