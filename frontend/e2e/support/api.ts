import { request, type APIRequestContext, type APIResponse } from '@playwright/test'
import { API_URL, WEB_URL, type Credentials } from './env'

// The API attributes a request to a company by its Origin (fail-closed: no company, no answer), so
// every call carries the staging site's origin, exactly as the browser's own calls do.
const ORIGIN = new URL(WEB_URL).origin

export class LoginError extends Error {
  constructor(readonly status: number, readonly code: string | undefined) {
    super(`login ${status}${code ? ` ${code}` : ''}`)
  }
}

/**
 * Real login against the real endpoint. Plain fetch rather than Playwright's request context on
 * purpose: this runs in setup, outside any test, so the PIN in the body never reaches a trace.
 * The error carries the status and the server's error code, never the credentials.
 */
export async function loginToken(creds: Credentials): Promise<string> {
  const r = await fetch(`${API_URL}/api/auth/login`, {
    method: 'POST',
    headers: { Origin: ORIGIN, 'Content-Type': 'application/json' },
    body: JSON.stringify({ email: creds.phone, password: creds.pin }),
  })
  const body = (await r.json().catch(() => ({}))) as { token?: string; error?: string }
  if (r.status !== 200 || !body.token) throw new LoginError(r.status, body.error)
  return body.token
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
  async json<T>(path: string): Promise<T> {
    const r = await this.get(path)
    if (!r.ok()) throw new Error(`GET ${path} → ${r.status()}`)
    return (await r.json()) as T
  }

  dispose(): Promise<void> {
    return this.ctx.dispose()
  }
}
