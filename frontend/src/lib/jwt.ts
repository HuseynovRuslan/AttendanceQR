export type Role = 'Admin' | 'Manager' | 'Employee'

export interface JwtClaims {
  sub?: string
  email?: string
  role?: Role
  exp?: number
  /** "1" while the account is on a temporary PIN and must set its own before using the app. */
  mcp?: string
  /** The account's TokenVersion when this token was issued — it changes whenever the sessions are ended
   *  (PIN reset, «hər yerdən çıxış»). A device's push subscription is bound to it; see lib/push.ts. */
  tv?: string
  /** Set on an operator's support/view session in someone else's account. */
  imp?: string
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
