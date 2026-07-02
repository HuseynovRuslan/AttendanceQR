const BASE_URL = import.meta.env.VITE_API_URL ?? 'http://localhost:5103'
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
}

export async function apiRequest<T = unknown>(
  path: string,
  options: RequestOptions = {},
): Promise<ApiResponse<T>> {
  const { method = 'GET', body, auth = true } = options

  const headers: Record<string, string> = { 'Content-Type': 'application/json' }
  if (auth) {
    const token = getToken()
    if (token) headers.Authorization = `Bearer ${token}`
  }

  const res = await fetch(`${BASE_URL}${path}`, {
    method,
    headers,
    body: body !== undefined ? JSON.stringify(body) : undefined,
  })

  // Only bounce to login for authenticated calls — a 401 from login itself means bad credentials.
  if (res.status === 401 && auth) {
    clearToken()
    onUnauthorized?.()
  }

  const text = await res.text()
  const data = text ? JSON.parse(text) : null
  return { status: res.status, data: data as T }
}
