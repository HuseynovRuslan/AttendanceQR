export type Role = 'Admin' | 'Manager' | 'Employee'

export interface JwtClaims {
  sub?: string
  email?: string
  role?: Role
  exp?: number
}

/** The screen a role lands on after login: staff → admin panel, everyone else → the mobile home. */
export function roleHome(role: Role | null | undefined): string {
  return role === 'Admin' || role === 'Manager' ? '/admin' : '/home'
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
