// Where the suite runs and with whose credentials — both decided here, once, and refused loudly.
//
// Credentials come from the process environment ONLY: the cloud environment's secrets, a CI secret, or
// variables set for one command on a laptop. There is no file to read them from, so there is no file to
// commit by mistake. Nothing here is ever printed: a missing value is reported by its NAME.

export class E2EConfigError extends Error {
  constructor(message: string) {
    super(`E2E konfiqurasiya xətası: ${message}`)
    this.name = 'E2EConfigError'
  }
}

export function envValue(name: string): string | undefined {
  const v = process.env[name]
  return v ? v.trim() : undefined
}

export const WEB_URL = envValue('E2E_BASE_URL') ?? 'https://test.qrlog.az'
export const API_URL = envValue('E2E_API_URL') ?? 'https://api-test.qrlog.az'

// The suite WRITES — E2E branches, people, role grants, PIN resets. The only hosts it may ever talk to are
// staging's two and a local stack. Not an opt-in, not an override: a production host is refused here, by
// every request helper (support/api.ts) and by every browser context (support/fixtures.ts).
const ALLOWED_HOSTS = new Set(['test.qrlog.az', 'api-test.qrlog.az', 'localhost', '127.0.0.1'])

/** A QRLog host that is not staging — production, a company subdomain, the app shell, the operator console. */
export function isProductionHost(host: string): boolean {
  const h = host.toLowerCase()
  return (h === 'qrlog.az' || h.endsWith('.qrlog.az')) && !ALLOWED_HOSTS.has(h)
}

export function assertAllowedUrl(url: string): void {
  const host = new URL(url).hostname.toLowerCase()
  if (isProductionHost(host)) throw new E2EConfigError(`${host} production-dır — E2E testləri orada işləmir`)
  if (!ALLOWED_HOSTS.has(host)) throw new E2EConfigError(`${host} icazəli deyil (yalnız ${[...ALLOWED_HOSTS].join(', ')})`)
}

assertAllowedUrl(WEB_URL)
assertAllowedUrl(API_URL)

/** The staging company the suite may write to, checked against the API's own answer before anything is. */
export const EXPECTED_COMPANY = envValue('E2E_EXPECT_COMPANY') ?? 'Test şirkəti'

export interface Credentials {
  phone: string
  pin: string
}

/** The admin given to the suite. Missing → a configuration error, never a silent skip. */
export function adminCredentials(): Credentials {
  const phone = envValue('E2E_ADMIN_PHONE')
  const pin = envValue('E2E_ADMIN_PIN')
  const missing = [!phone && 'E2E_ADMIN_PHONE', !pin && 'E2E_ADMIN_PIN'].filter(Boolean)
  if (missing.length) {
    throw new E2EConfigError(
      `${missing.join(' və ')} təyin olunmayıb. Staging test şirkətinin admin girişini mühitin secret-lərinə ` +
        'əlavə edin (cloud mühiti: Environment → Edit; CI: `staging-e2e` environment secret-i). Fayl oxunmur.',
    )
  }
  return { phone: phone!, pin: pin! }
}
