/**
 * When a scan soon after arriving must be confirmed as LEAVING before it is saved on the phone.
 *
 * Mirrors the server's EarlyCheckOutRules. Online the server asks; offline there is nobody to ask, and
 * «did it work?» retries queued with no signal were replayed later as check-outs — the day that began
 * at 07:38 closed at 07:44, and the real exit that evening was refused. So the phone asks first.
 */

/** The server's EarlyCheckOutRules.ConfirmWithinMinutes. */
export const EARLY_CHECKOUT_CONFIRM_MS = 120 * 60_000

/** The server's double-tap window (TooSoonToCheckOut): inside it a scan is refused whatever the
 *  answer, so asking «are you leaving?» there would be a question with no effect. */
export const DOUBLE_TAP_MS = 5 * 60_000

/** Would a tap at `tapIso` be an EARLY check-out of a stretch that began at `checkInIso`? */
export function isEarlyCheckOut(checkInIso: string, tapIso: string): boolean {
  const gap = Date.parse(tapIso) - Date.parse(checkInIso)
  return gap >= DOUBLE_TAP_MS && gap < EARLY_CHECKOUT_CONFIRM_MS
}
