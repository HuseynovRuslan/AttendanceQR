/**
 * What the "PIN-i unutdum" screen is allowed to conclude from a server answer.
 *
 * Kept out of the component because these are the decisions that used to be wrong, and they are the
 * ones worth pinning with tests: `apiRequest` does NOT throw on a non-2xx, so "we called the server"
 * never meant "the server accepted it" — the screen used to show 📨 "Sorğu göndərildi" after a 500.
 * And a rejected verify says nothing about WHY (the endpoint deliberately answers the same
 * `verified: false` for a wrong face, an unbound device, a throttle and a missing account), so the
 * UI must not claim to know which one it was.
 */

/** No connection at all — fetch itself threw, or the deadline aborted it. */
export const NETWORK_MESSAGE = 'İnternet əlaqəsi yoxdur. Əlaqəni yoxlayın və yenidən cəhd edin.'
/** The server answered, but not with an acceptance (500, 502, gateway…). */
export const SERVER_MESSAGE = 'Sistem cavab vermədi. Bir neçə dəqiqə sonra yenidən cəhd edin.'
/** Too many tries from this connection. */
export const BUSY_MESSAGE = 'Çox sayda cəhd oldu. Bir az gözləyin və yenidən cəhd edin.'

export type VerifyOutcome =
  | { kind: 'verified'; pin: string }
  /** The server answered and said no. It never says which factor failed — do not guess in the copy. */
  | { kind: 'rejected' }
  /** We never got an answer we can trust. Retrying (or the admin queue) is the way out. */
  | { kind: 'unreachable'; message: string }

export function classifyVerify(status: number, data: unknown): VerifyOutcome {
  const body = (data ?? null) as { verified?: unknown; pin?: unknown } | null
  if (status >= 200 && status < 300) {
    if (body?.verified === true && typeof body.pin === 'string' && body.pin.length > 0) {
      return { kind: 'verified', pin: body.pin }
    }
    // Includes the shouldn't-happen "verified with no PIN": there is nothing to show the employee,
    // so treat it as a no and send them to the admin queue rather than to a blank success screen.
    // It also includes a throttled verify, which answers a plain `verified: false` — indistinguishable
    // from a real rejection, and one more reason the failure copy must not name a cause.
    return { kind: 'rejected' }
  }
  return { kind: 'unreachable', message: status === 429 ? BUSY_MESSAGE : SERVER_MESSAGE }
}

export type AdminRequestOutcome =
  | { kind: 'accepted' }
  | { kind: 'failed'; message: string }

/**
 * 'accepted' means "the server did not report a failure" — NOT "the request was filed".
 *
 * POST /api/auth/forgot-pin answers 200 `{ ok: true }` on paths where it deliberately writes nothing:
 * an unknown or not-yet-activated identifier (so a stranger cannot probe who has an account), and a
 * per-IP throttle (a whole site behind one router, or a carrier NAT, shares that counter). The
 * response is byte-identical by design, so no amount of status inspection here can tell them apart.
 * That is why the 📨 screen must not promise that an admin will act: it shows the number that was
 * submitted, so a typo is visible, and names a second route that works whether or not this landed.
 */
export function classifyAdminRequest(status: number, data: unknown): AdminRequestOutcome {
  const body = (data ?? null) as { ok?: unknown; error?: unknown } | null
  if (status >= 200 && status < 300 && body?.error === undefined && body?.ok !== false) {
    return { kind: 'accepted' }
  }
  return { kind: 'failed', message: status === 429 ? BUSY_MESSAGE : SERVER_MESSAGE }
}
