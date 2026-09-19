/**
 * "QRLog ilə daxil ol" for other applications of ours (the generalised Kitabxana approval), see
 * ExternalSignInPage. Kept out of the component so the rules about what may be sent, and where a person may be
 * sent back to, are plain functions with tests.
 */

export interface ExternalApp {
  /** The path segment and the configuration key on the server (ExternalSignIn:Apps:<key>). */
  key: string
  name: string
  /** What the person is told they are signing in to. */
  description: string
  /** The only host a `return` address may point at. A link can be sent by anyone. */
  returnHost: string
  homeUrl: string
}

export const EXTERNAL_APPS: Record<string, ExternalApp> = {
  meydan: {
    key: 'meydan',
    name: 'MEYDAN',
    description: 'Bakı kəndləri yaradıcı müsabiqələri platforması',
    returnHost: 'meydan.qrlog.az',
    homeUrl: 'https://meydan.qrlog.az',
  },
}

export function readApp(raw: string | null | undefined): ExternalApp | null {
  if (!raw) return null
  return Object.prototype.hasOwnProperty.call(EXTERNAL_APPS, raw) ? EXTERNAL_APPS[raw] : null
}

/** The code the app put in its link: hex, 16–64 characters, and nothing else may be sent as one. */
export function readCode(raw: string | null | undefined): string | null {
  const code = (raw ?? '').trim()
  return /^[0-9a-fA-F]{16,64}$/.test(code) ? code.toLowerCase() : null
}

/**
 * Where to send the person once they have approved (or declined): only an https address on the app's own host.
 * An unchecked one would turn this page into a redirect somebody else gets to aim.
 */
export function readReturnUrl(raw: string | null | undefined, app: ExternalApp | null): string | null {
  if (!raw || !app) return null
  try {
    const url = new URL(raw)
    return url.protocol === 'https:' && url.hostname === app.returnHost ? url.toString() : null
  } catch {
    return null
  }
}

/** The same address with `cancelled=1` added, so the app can end the ticket and say so. */
export function cancelUrl(returnUrl: string): string {
  const url = new URL(returnUrl)
  url.searchParams.set('cancelled', '1')
  return url.toString()
}

export type SignInOutcome = { kind: 'signed-in' } | { kind: 'refused'; message: string }

export const EXPIRED_MESSAGE = (app: string) =>
  `Girişin vaxtı bitib. ${app} səhifəsində yenidən "QRLog ilə daxil ol" düyməsini basın.`
export const NOT_ELIGIBLE_MESSAGE = 'Bu giriş sizin şirkət üçün açıq deyil.'
export const NOT_CONFIGURED_MESSAGE = 'Bu giriş hələ açılmayıb. Bir az sonra yenidən yoxlayın.'
export const IMPERSONATION_MESSAGE = 'Dəstək sessiyasından başqa tətbiqə giriş edilmir.'
export const SERVER_MESSAGE = 'Sistem cavab vermədi. Bir neçə dəqiqə sonra yenidən cəhd edin.'
export const NETWORK_MESSAGE = 'İnternet əlaqəsi yoxdur. Əlaqəni yoxlayın və yenidən cəhd edin.'

export function classifySignIn(status: number, data: unknown, appName: string): SignInOutcome {
  if (status === 204) return { kind: 'signed-in' }
  const error = (data as { error?: unknown } | null)?.error
  if (error === 'CodeExpired' || error === 'InvalidCode' || status === 409) return { kind: 'refused', message: EXPIRED_MESSAGE(appName) }
  if (error === 'NotEligible') return { kind: 'refused', message: NOT_ELIGIBLE_MESSAGE }
  if (error === 'NotDuringImpersonation') return { kind: 'refused', message: IMPERSONATION_MESSAGE }
  if (error === 'NotConfigured' || error === 'UnknownApp') return { kind: 'refused', message: NOT_CONFIGURED_MESSAGE }
  return { kind: 'refused', message: SERVER_MESSAGE }
}
