/**
 * Does a string the camera decoded LOOK like a QRLog poster token?
 *
 * html5-qrcode hands over whatever it manages to read, and in a dark morning that is sometimes
 * garbage: a partial read, a neighbouring advert's QR, a barcode on a crate. The scan flow used to
 * take any decode at face value — stop the camera, walk the worker through the selfie, send it, and
 * only then hear «TokenMalformed» from the server and show a red failure screen. Seventeen attempts
 * in two hours, standing at the poster, one of them failing exactly this way (Bibiheybət, 02.09).
 *
 * A decode that does not even have the token's shape should never leave the phone: ignore it and let
 * the camera keep looking — the next frame is another chance, which is how a scanner is supposed to
 * feel. Only a stable read of a FOREIGN code (the same wrong text, frame after frame) is worth a
 * word, because that is a person aiming at the wrong QR on purpose or by mistake.
 *
 * THE ONE RULE OF THIS FILE: a real token must never be rejected. The checks below are copied from
 * the server's `QrTokenService.Validate` TokenMalformed branches — base64url, five dot-parts, a GUID,
 * two integers — and NOTHING more. No length limits, no expiry reading, no signature guessing: any
 * of those would be a place where this file could drift stricter than the server and silently brick
 * every poster. If the shape here passes, the server takes over with real answers.
 */

const BASE64URL = /^[A-Za-z0-9_-]+$/
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const INT = /^\d+$/

export function looksLikeQrToken(text: string): boolean {
  const token = text.trim()
  if (!token || !BASE64URL.test(token)) return false

  let payload: string
  try {
    const b64 = token.replace(/-/g, '+').replace(/_/g, '/')
    payload = atob(b64 + '='.repeat((4 - (b64.length % 4)) % 4))
  } catch {
    return false
  }

  const parts = payload.split('.')
  if (parts.length !== 5) return false
  if (!GUID.test(parts[0])) return false
  if (!INT.test(parts[1])) return false
  if (!INT.test(parts[2])) return false
  return true
}

/**
 * Tells camera noise apart from a genuinely foreign QR code.
 *
 * Noise never reads the same twice — each bad frame decodes to different garbage, so the right
 * response is silence and another frame. A real code that is simply not ours decodes IDENTICALLY
 * on every frame, and the person holding the phone deserves to be told they are aiming at the
 * wrong thing — without the camera stopping, because the poster is usually a hand's width away.
 */
export class ForeignQrDetector {
  private last = ''
  private count = 0

  /** Feed one rejected decode; true once the same text has been seen enough to mean "aimed, not noise". */
  seen(text: string): boolean {
    if (text === this.last) this.count += 1
    else { this.last = text; this.count = 1 }
    return this.count >= 2
  }

  reset(): void {
    this.last = ''
    this.count = 0
  }
}
