/**
 * What the "Kitabxana 2.0-a giriş" approval screen may conclude from a server answer.
 *
 * Kept out of the component for the same reason as forgotPinOutcome: `apiRequest` does not throw on a
 * non-2xx, so "we called the server" never means "the employee is signed in". Only 204 does — and each
 * refusal has its own way out, which the copy has to name, because the person is standing there with a
 * screen in another tab waiting for them.
 */

/**
 * Where to send the employee back to once they have approved: the screen they came from, so an
 * administrator who started in the panel returns to the panel and a player returns to the game.
 *
 * Only an address on the quiz's own site is accepted. It arrives in a link, and a link can be sent by
 * anyone, so an unchecked one would turn this page into a redirect somebody else gets to aim.
 */
export function readReturnUrl(raw: string | null | undefined): string | null {
  if (!raw) return null
  try {
    const url = new URL(raw)
    return url.protocol === 'https:' && url.hostname === 'book.qrlog.az' ? url.toString() : null
  } catch {
    return null
  }
}

/** The code the quiz put in its QR: hex, and nothing else may be sent as one. */
export function readCode(raw: string | null | undefined): string | null {
  const code = (raw ?? '').trim()
  return /^[0-9a-fA-F]{8,64}$/.test(code) ? code.toLowerCase() : null
}

export type SignInOutcome =
  | { kind: 'signed-in' }
  /** The sign-in cannot be completed as things stand; `message` says what to do about it. */
  | { kind: 'refused'; message: string }

export const EXPIRED_MESSAGE =
  'Girişin vaxtı bitib. Kitabxana səhifəsində yeni kod alın və yenidən "QRLog ilə təsdiqlə" düyməsini basın.'
export const NOT_ELIGIBLE_MESSAGE = 'Bu yarış sizin şirkət üçün açıq deyil.'
export const NO_PHONE_MESSAGE =
  'Kadr qeydinizdə mobil nömrə yoxdur. Yarış oyunçunu nömrə ilə tanıyır, ona görə rəhbərinizə müraciət edin.'
export const NOT_CONFIGURED_MESSAGE = 'Bu giriş hələ açılmayıb. Bir az sonra yenidən yoxlayın.'
export const SERVER_MESSAGE = 'Sistem cavab vermədi. Bir neçə dəqiqə sonra yenidən cəhd edin.'
export const NETWORK_MESSAGE = 'İnternet əlaqəsi yoxdur. Əlaqəni yoxlayın və yenidən cəhd edin.'

export function classifySignIn(status: number, data: unknown): SignInOutcome {
  if (status === 204) return { kind: 'signed-in' }

  const error = (data as { error?: unknown } | null)?.error
  if (error === 'CodeExpired' || status === 409) return { kind: 'refused', message: EXPIRED_MESSAGE }
  if (error === 'NotEligible') return { kind: 'refused', message: NOT_ELIGIBLE_MESSAGE }
  if (error === 'NoPhoneNumber' || error === 'BadPhoneNumber') return { kind: 'refused', message: NO_PHONE_MESSAGE }
  if (error === 'NotConfigured') return { kind: 'refused', message: NOT_CONFIGURED_MESSAGE }
  // Includes "InvalidCode": the code was mangled on the way here, and a fresh one is the way out.
  if (error === 'InvalidCode') return { kind: 'refused', message: EXPIRED_MESSAGE }
  return { kind: 'refused', message: SERVER_MESSAGE }
}
