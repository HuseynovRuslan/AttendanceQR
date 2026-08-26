export const API_BASE_URL = import.meta.env.VITE_API_URL ?? 'http://localhost:5103'
const TOKEN_KEY = 'attendanceqr.jwt'

// --- JWT storage -----------------------------------------------------------

export function getToken(): string | null {
  return localStorage.getItem(TOKEN_KEY)
}

export function setToken(token: string): void {
  localStorage.setItem(TOKEN_KEY, token)
}

export function clearToken(): void {
  localStorage.removeItem(TOKEN_KEY)
  // Ending a session must never strand the impersonation backup — that stash (IMPERSONATION_BACKUP_KEY)
  // holds the operator's OWN long-lived, cross-tenant super-admin token. Every caller of clearToken()
  // (logout, the 401 handler) means "this session is over"; if they cleared only the active token, an
  // operator who logged out mid-impersonation — or whose 60-min impersonation token simply expired —
  // would leave their super-admin JWT sitting in plaintext localStorage for the next person on a shared
  // support machine. exitImpersonation() reads the backup BEFORE this runs, so its restore is unaffected.
  localStorage.removeItem(IMPERSONATION_BACKUP_KEY)
  localStorage.removeItem(IMPERSONATION_INFO_KEY)
}

// --- Super-admin impersonation --------------------------------------------
// While impersonating a tenant admin for support, the operator's OWN token is stashed here and the
// active token is swapped for the short-lived impersonation one. "Exit" restores the stash. A global
// banner reads getImpersonation() so the operator can never forget they are inside someone else's
// account. Kept in localStorage (not memory) so a reload — which the token swap forces — keeps it.
const IMPERSONATION_BACKUP_KEY = 'attendanceqr.jwt.super'
const IMPERSONATION_INFO_KEY = 'attendanceqr.impersonation'

export interface ImpersonationInfo {
  tenantName: string
  adminName: string
}

/** Begin impersonating: stash the operator's own token, switch to the impersonation token. */
export function startImpersonation(token: string, info: ImpersonationInfo): void {
  const current = getToken()
  if (current) localStorage.setItem(IMPERSONATION_BACKUP_KEY, current)
  localStorage.setItem(IMPERSONATION_INFO_KEY, JSON.stringify(info))
  setToken(token)
}

/** The banner info while impersonating, or null when not. Requires BOTH the stash and the info, so a
 *  half-cleared state never shows a stuck banner over a real session. */
export function getImpersonation(): ImpersonationInfo | null {
  const raw = localStorage.getItem(IMPERSONATION_INFO_KEY)
  if (!raw || !localStorage.getItem(IMPERSONATION_BACKUP_KEY)) return null
  try {
    return JSON.parse(raw) as ImpersonationInfo
  } catch {
    return null
  }
}

/** Stop impersonating: restore the operator's own token. */
export function exitImpersonation(): void {
  const backup = localStorage.getItem(IMPERSONATION_BACKUP_KEY)
  localStorage.removeItem(IMPERSONATION_BACKUP_KEY)
  localStorage.removeItem(IMPERSONATION_INFO_KEY)
  if (backup) setToken(backup)
  else clearToken()
}

// --- 401 handling ----------------------------------------------------------
// AuthContext registers a handler so an expired/invalid token bounces to /login.

let onUnauthorized: (() => void) | null = null

export function setUnauthorizedHandler(handler: () => void): void {
  onUnauthorized = handler
}

// --- request ---------------------------------------------------------------

export interface ApiResponse<T> {
  status: number
  data: T
}

interface RequestOptions {
  method?: string
  body?: unknown
  /** Attach the JWT (default true). Login/activate pass false so their 401 doesn't redirect. */
  auth?: boolean
  /**
   * Abort the request after this many ms; the fetch then THROWS (a TimeoutError), which callers see
   * exactly like a dropped network. Set only where hanging is worse than failing — the scan uses it
   * so a wedged connection sends the tap to the offline queue instead of a spinner that never ends.
   */
  timeoutMs?: number
  /**
   * Speak as SOMEBODY ELSE on this device, using their saved profile's JWT instead of the active
   * session's. Set by exactly one caller: the offline drain, which has to replay each queued scan
   * under the account that made it. A crew phone can come back from a day with no signal holding
   * thirty people's taps, and draining only the profile that happens to be active would leave the
   * other twenty-nine queued until somebody switched to each of them in turn.
   *
   * A 401 on such a request is that PROFILE's problem — its PIN was changed, its token retired — and
   * must never end the session of the person actually holding the phone, so the bounce below is
   * suppressed whenever this is set.
   */
  token?: string
}

export async function apiRequest<T = unknown>(
  path: string,
  options: RequestOptions = {},
): Promise<ApiResponse<T>> {
  const { method = 'GET', body, auth = true, timeoutMs, token: asToken } = options

  const headers: Record<string, string> = { 'Content-Type': 'application/json' }
  if (auth) {
    const token = asToken ?? getToken()
    if (token) headers.Authorization = `Bearer ${token}`
  }

  const res = await fetch(`${API_BASE_URL}${path}`, {
    method,
    headers,
    body: body !== undefined ? JSON.stringify(body) : undefined,
    // Guarded: an old WebView without AbortSignal.timeout simply gets no deadline, as before.
    signal: timeoutMs !== undefined && typeof AbortSignal !== 'undefined' && 'timeout' in AbortSignal
      ? AbortSignal.timeout(timeoutMs)
      : undefined,
  })

  // Only bounce to login for authenticated calls — a 401 from login itself means bad credentials —
  // and never for a call deliberately made as another saved profile (see RequestOptions.token).
  if (res.status === 401 && auth && asToken === undefined) {
    clearToken()
    onUnauthorized?.()
  }

  const text = await res.text()
  const data = text ? JSON.parse(text) : null
  return { status: res.status, data: data as T }
}
