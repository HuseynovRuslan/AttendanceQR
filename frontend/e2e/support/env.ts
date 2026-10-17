import { existsSync, readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'

// Credentials for the E2E accounts. They come from the process environment (the cloud environment's
// settings, a CI secret) or from frontend/.env.e2e.local, which `.env.*.local` keeps out of git.
// Nothing here is ever printed: a missing value is reported by its NAME only.
const FILE = fileURLToPath(new URL('../../.env.e2e.local', import.meta.url))

function fromFile(): Record<string, string> {
  if (!existsSync(FILE)) return {}
  const out: Record<string, string> = {}
  for (const line of readFileSync(FILE, 'utf8').split(/\r?\n/)) {
    const m = /^\s*([A-Z0-9_]+)\s*=\s*(.*?)\s*$/.exec(line)
    if (m) out[m[1]] = m[2].replace(/^(['"])(.*)\1$/, '$2')
  }
  return out
}

const file = fromFile()

/** Every E2E_* setting, from the environment and the local file together. */
export function e2eSettings(): Record<string, string> {
  const out: Record<string, string> = { ...file }
  for (const [k, v] of Object.entries(process.env)) if (k.startsWith('E2E_') && v) out[k] = v
  return out
}

export function envValue(name: string): string | undefined {
  const v = process.env[name] ?? file[name]
  return v ? v : undefined
}

export const WEB_URL = envValue('E2E_BASE_URL') ?? 'https://test.qrlog.az'
export const API_URL = envValue('E2E_API_URL') ?? 'https://api-test.qrlog.az'

// The suite writes: it creates employees, structures and role assignments. It must never do that to
// a company people clock in at, so anything but the staging host needs an explicit opt-in.
const host = new URL(WEB_URL).hostname
export const IS_STAGING = host === 'test.qrlog.az' || host === 'localhost' || host === '127.0.0.1'
if (!IS_STAGING && envValue('E2E_ALLOW_NON_STAGING') !== '1') {
  throw new Error(`E2E: ${host} staging deyil. Yazı edən testlər yalnız test.qrlog.az-da işləyir.`)
}

export interface Credentials {
  phone: string
  pin: string
}

export function credentials(prefix: string): Credentials | undefined {
  const phone = envValue(`${prefix}_PHONE`)
  const pin = envValue(`${prefix}_PIN`)
  return phone && pin ? { phone, pin } : undefined
}
