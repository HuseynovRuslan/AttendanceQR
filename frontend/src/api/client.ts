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
// Where the session was started from, so leaving it puts the operator back where they were rather
// than somewhere that used to be the only possibility. It WAS only ever started from the operator
// console's companies list, so "exit" hard-coded /tenants — then a company card on the group board
// at app.qrlog.az started one too, and on that host /tenants is not a route at all: exiting would
// have dropped the group head onto nothing. Recorded rather than assumed.
const IMPERSONATION_RETURN_KEY = 'attendanceqr.impersonation.from'

export interface ImpersonationInfo {
  tenantName: string
  adminName: string
  /**
   * A read-only «baxış» session rather than a support impersonation.
   *
   * The two look identical to the token machinery and are worlds apart to the person holding it. A
   * support session ACTS AS that admin — the borrowed name is the important fact and the banner must
   * shout it. A view session cannot change anything (ViewOnlyBoundary refuses every write), and the
   * seat it borrows is an implementation detail: the server picks the company's oldest non-operator
   * admin, so the group head — an admin of that very company — was told he was «baxırsınız as
   * Dmitriy Qriqoryev», a colleague he has nothing to do with. Naming a stranger for a session that
   * changes nothing is confusing, and it makes the loud support banner cry wolf.
   */
  readOnly?: boolean
}

/** Begin impersonating: stash the operator's own token, switch to the impersonation token. */
export function startImpersonation(token: string, info: ImpersonationInfo): void {
  const current = getToken()
  if (current) localStorage.setItem(IMPERSONATION_BACKUP_KEY, current)
  localStorage.setItem(IMPERSONATION_INFO_KEY, JSON.stringify(info))
  localStorage.setItem(IMPERSONATION_RETURN_KEY, window.location.pathname)
  setToken(token)
}

/** The path the session was started from — where "Çıx" should land. */
export function impersonationReturnPath(): string {
  return localStorage.getItem(IMPERSONATION_RETURN_KEY) || '/tenants'
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
  localStorage.removeItem(IMPERSONATION_RETURN_KEY)
  if (backup) setToken(backup)
  else clearToken()
}

// --- 401 handling ----------------------------------------------------------
// AuthContext registers a handler so an expired/invalid token bounces to /login. It is HANDED the token
// the refused request carried — the one that just proved dead — rather than reading the active slot for
// itself: clearToken() has emptied that slot by the time the handler runs, so it used to find null, and
// the dead account was never dropped from the crew phone's saved profiles. The holder then saw a row that
// looked like a way in, and found out it was not standing at the poster.

let onUnauthorized: ((deadToken: string | null) => void) | null = null

/**
 * A 401 the server sends on a token it has just ACCEPTED. There is one: «PIN-i dəyiş» posts the current
 * PIN, and a wrong one comes back as 401 InvalidCurrentPassword. Bounced like a dead session, a worker
 * unsure of their PIN who guessed wrong was signed out — and, since the handler drops the dead token's
 * saved profile, left on /login with no row to tap and only the PIN they could not remember to get back
 * in with, the day's check-in included. The sheet shows «Cari PIN yanlışdır» instead.
 *
 * Named, not inferred from the endpoint: change-password's OTHER 401 (InvalidToken — the account behind
 * the token is gone) is a dead session like any other. Anything this does not name, a 401 with no body
 * or one that cannot be read included, still ends the session.
 */
function answeredOnGoodToken(data: unknown): boolean {
  return (data as { error?: unknown } | null)?.error === 'InvalidCurrentPassword'
}

export function setUnauthorizedHandler(handler: (deadToken: string | null) => void): void {
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
  /** Extra request headers — the approval requests' Idempotency-Key, for one. */
  headers?: Record<string, string>
}

export async function apiRequest<T = unknown>(
  path: string,
  options: RequestOptions = {},
): Promise<ApiResponse<T>> {
  const { method = 'GET', body, auth = true, timeoutMs, token: asToken, headers: extra } = options

  const headers: Record<string, string> = { 'Content-Type': 'application/json', ...extra }
  // Kept, not just sent: a 401 has to be able to say WHICH token the server refused.
  const sentToken = auth ? asToken ?? getToken() : null
  if (sentToken) headers.Authorization = `Bearer ${sentToken}`

  const res = await fetch(`${API_BASE_URL}${path}`, {
    method,
    headers,
    body: body !== undefined ? JSON.stringify(body) : undefined,
    // Guarded: an old WebView without AbortSignal.timeout simply gets no deadline, as before.
    signal: timeoutMs !== undefined && typeof AbortSignal !== 'undefined' && 'timeout' in AbortSignal
      ? AbortSignal.timeout(timeoutMs)
      : undefined,
  })

  // Read before the 401 decision below, because one 401 is only told apart from a dead session by its
  // body. A body that cannot be parsed still bounces first and throws after, exactly as it did when the
  // bounce ran before the read.
  const text = await res.text()
  let data: unknown = null
  let unreadable: { error: unknown } | null = null
  if (text) {
    try {
      data = JSON.parse(text)
    } catch (error) {
      unreadable = { error }
    }
  }

  // Only bounce to login for authenticated calls — a 401 from login itself means bad credentials —
  // never for a call deliberately made as another saved profile (see RequestOptions.token), and never
  // for a 401 that is about what was typed rather than who is asking (see answeredOnGoodToken).
  if (res.status === 401 && auth && asToken === undefined && !answeredOnGoodToken(data)) {
    clearToken()
    onUnauthorized?.(sentToken)
  }
  if (unreadable) throw unreadable.error

  if (auth && asToken === undefined && impersonationLost(res.status, data)) {
    const info = getImpersonation()
    // Read before exiting — exitImpersonation clears it.
    const back = impersonationReturnPath()
    try {
      sessionStorage.setItem(ENDED_SESSION_KEY, info?.readOnly ? 'view' : 'support')
    } catch {
      /* private window: the screen simply shows without the sentence */
    }
    // Exactly what the banner's «Çıx» does: the operator's own token back, a full reload, and the
    // screen the session was started from.
    exitImpersonation()
    window.location.href = back
  }

  return { status: res.status, data: data as T }
}

// --- A support or view session that ran out ------------------------------------
// Those tokens ran out after an hour until c098fa1; this stays as the net for one the server cannot read.
// (An operator taken off the allowlist is a 401 instead — see the backend's ImpersonationAllowlist.)
// When one expires the server cannot say which company the
// request belongs to — the company is read from the token, and an expired token is not read — so it
// refuses with 400 TenantUnresolved before authentication gets the chance to say 401, and the 401
// bounce above never ran. On 03.10.2026 the attendance board sat under a support banner saying
// «Məlumat yüklənmədi» for nearly two hours after the session had ended, with nothing to say so.
//
// Recognised ONLY while impersonating. An employee's or an admin's own session is never touched here:
// their tokens do not expire, a TenantUnresolved on one of them means something else entirely, and
// signing people out is the one thing this product does not do.
//
// The session ends the way the banner's «Çıx» ends it: the operator's own token is restored and they
// land back where they started, with one sentence saying why. It first shipped ending in a full
// sign-out — the stashed operator token cleared as a 401 clears it — and on 03.10.2026 the owner chose
// the way back instead: signing in again after every hour of support is the cost, and what it bought
// was protection for a session that is left open on the operator's machine anyway whenever they are
// not impersonating. If their own token is no longer good either, the next request is a 401 and the
// ordinary bounce to the login screen takes over.

const ENDED_SESSION_KEY = 'attendanceqr.sessionEnded'

function impersonationLost(status: number, data: unknown): boolean {
  return status === 400
    && (data as { error?: unknown } | null)?.error === 'TenantUnresolved'
    && getImpersonation() !== null
}

/** The sentence to show once a support or view session has run out — or null. Only reads; whoever shows
 *  it calls forgetEndedSessionNotice. */
export function endedSessionNotice(): string | null {
  let kind: string | null = null
  try {
    kind = sessionStorage.getItem(ENDED_SESSION_KEY)
  } catch {
    return null
  }
  // True on every screen it can land on — the operator's own panel, or the login screen when their own
  // session had run out as well — so it says what happened and nothing about where they are.
  if (kind === 'support') return 'Dəstək rejiminin vaxtı bitdi.'
  if (kind === 'view') return 'Baxış sessiyasının vaxtı bitdi.'
  return null
}

/** Said once: a later visit to the login screen has nothing to explain. Kept apart from reading it so
 *  a render that runs twice (React's development checks do) cannot lose the sentence on the way. */
export function forgetEndedSessionNotice(): void {
  try {
    sessionStorage.removeItem(ENDED_SESSION_KEY)
  } catch {
    /* nothing to forget */
  }
}
