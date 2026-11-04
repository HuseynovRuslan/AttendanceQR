import { test } from '@playwright/test'
import { API_URL, WEB_URL, assertAllowedUrl, type Credentials } from './env'

// Every API call the suite makes goes through `send` — plain fetch, never a Playwright request context, so
// no call's headers or body are written into a trace: not the PIN of a login, not the token of a session.
// What a test did shows in its report as a step («API tabelci: PUT /api/manager/employees/…») and in its
// assertions; the credential that carried it does not show anywhere.

// The API attributes an anonymous request to a company by its Origin (fail-closed: no company, no answer).
// Signed-in calls are attributed by the token, but carrying the origin costs nothing and is what the
// browser does anyway.
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
  const url = `${API_URL}${path}`
  assertAllowedUrl(url)
  const r = await fetch(url, {
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

/** Real login against the real endpoint. The error carries the status and the server's code, never the credentials. */
export async function loginToken(creds: Credentials): Promise<string> {
  const r = await send<{ token?: string; error?: string }>('POST', '/api/auth/login', null, {
    email: creds.phone,
    password: creds.pin,
  })
  if (r.status !== 200 || !r.body?.token) throw new LoginError(r.status, r.body?.error)
  return r.body.token
}

/** The company's name as the API tells a browser at the staging site — who setup is about to write to. */
export async function companyName(): Promise<string | undefined> {
  const r = await send<{ displayName?: string }>('GET', '/api/tenant/branding', null)
  return r.body?.displayName
}

/** Calls made as a person outside any test — setup, teardown — and the admin's side of a test. */
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

/** What a test reads back from a call — the shape of Playwright's APIResponse, minus anything that leaks. */
export class Answer {
  constructor(private readonly reply: Reply) {}
  status(): number {
    return this.reply.status
  }
  ok(): boolean {
    return this.reply.status >= 200 && this.reply.status < 300
  }
  async json(): Promise<any> {
    return this.reply.body
  }
}

/** The API as one signed-in role, inside a test: each call is a report step, nothing is traced. */
export class RoleApi {
  constructor(private readonly who: string, private readonly token: string) {}

  private call(method: string, path: string, body?: unknown): Promise<Answer> {
    return test.step(`API ${this.who}: ${method} ${path}`, async () => new Answer(await send(method, path, this.token, body)))
  }

  get(path: string) {
    return this.call('GET', path)
  }
  post(path: string, data?: unknown) {
    return this.call('POST', path, data ?? {})
  }
  put(path: string, data?: unknown) {
    return this.call('PUT', path, data ?? {})
  }
  delete(path: string) {
    return this.call('DELETE', path)
  }

  /** GET that must succeed; returns the parsed body. */
  async json<T = any>(path: string): Promise<T> {
    const r = await this.get(path)
    if (!r.ok()) throw new Error(`GET ${path} → ${r.status()}`)
    return (await r.json()) as T
  }
}
