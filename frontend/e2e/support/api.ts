import { request, type APIRequestContext, type APIResponse } from '@playwright/test'
import { API_URL, WEB_URL, type Credentials } from './env'

// The API attributes an anonymous request to a company by its Origin (fail-closed: no company, no
// answer). Signed-in calls are attributed by the token, but carrying the origin costs nothing and is
// what the browser does anyway.
const ORIGIN = new URL(WEB_URL).origin

export class LoginError extends Error {
  constructor(readonly status: number, readonly code: string | undefined) {
    super(`login ${status}${code ? ` ${code}` : ''}`)
  }
}

export interface Reply<T = any> {
  status: number
  body: T
}

async function send<T>(method: string, path: string, token: string | null, body?: unknown): Promise<Reply<T>> {
  const r = await fetch(`${API_URL}${path}`, {
    method,
    headers: {
      Origin: ORIGIN,
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  const text = await r.text()
  let parsed: unknown = text
  try {
    parsed = text ? JSON.parse(text) : null
  } catch {
    /* not JSON — keep the text */
  }
  return { status: r.status, body: parsed as T }
}

/**
 * Real login against the real endpoint. Plain fetch rather than Playwright's request context on
 * purpose: setup runs outside any test, so the PIN in the body never reaches a trace. The error
 * carries the status and the server's error code, never the credentials.
 */
export async function loginToken(creds: Credentials): Promise<string> {
  const r = await send<{ token?: string; error?: string }>('POST', '/api/auth/login', null, {
    email: creds.phone,
    password: creds.pin,
  })
  if (r.status !== 200 || !r.body?.token) throw new LoginError(r.status, r.body?.error)
  return r.body.token
}

/**
 * Calls made as a person, outside the trace — setup, teardown, and the admin's side of a test (the
 * company administrator's token is the one credential here that no run can rotate).
 */
export class Direct {
  constructor(private readonly token: string) {}

  get<T = any>(path: string) {
    return send<T>('GET', path, this.token)
  }
  post<T = any>(path: string, body: unknown = {}) {
    return send<T>('POST', path, this.token, body)
  }
  put<T = any>(path: string, body: unknown = {}) {
    return send<T>('PUT', path, this.token, body)
  }
  delete<T = any>(path: string) {
    return send<T>('DELETE', path, this.token)
  }

  /** A call that must succeed. The message names the endpoint and the server's error code only. */
  async ok<T = any>(method: 'GET' | 'POST' | 'PUT' | 'DELETE', path: string, body?: unknown): Promise<T> {
    const r = await send<T>(method, path, this.token, method === 'GET' || method === 'DELETE' ? undefined : (body ?? {}))
    if (r.status < 200 || r.status >= 300) {
      const code = (r.body as { error?: string } | null)?.error
      throw new Error(`${method} ${path} → ${r.status}${code ? ` ${code}` : ''}`)
    }
    return r.body
  }
}

/** An API client acting as one signed-in person. Its calls show up in the test's trace. */
export class Api {
  private constructor(readonly ctx: APIRequestContext) {}

  static async as(token: string): Promise<Api> {
    const ctx = await request.newContext({
      baseURL: API_URL,
      extraHTTPHeaders: { Origin: ORIGIN, Authorization: `Bearer ${token}` },
    })
    return new Api(ctx)
  }

  get(path: string): Promise<APIResponse> {
    return this.ctx.get(path)
  }
  post(path: string, data?: unknown): Promise<APIResponse> {
    return this.ctx.post(path, { data: data ?? {} })
  }
  put(path: string, data?: unknown): Promise<APIResponse> {
    return this.ctx.put(path, { data: data ?? {} })
  }
  delete(path: string): Promise<APIResponse> {
    return this.ctx.delete(path)
  }

  /** GET that must succeed; returns the parsed body. */
  async json<T = any>(path: string): Promise<T> {
    const r = await this.get(path)
    if (!r.ok()) throw new Error(`GET ${path} → ${r.status()}`)
    return (await r.json()) as T
  }

  dispose(): Promise<void> {
    return this.ctx.dispose()
  }
}
