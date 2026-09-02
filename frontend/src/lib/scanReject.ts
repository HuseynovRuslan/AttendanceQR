/**
 * Which server refusals a QUEUED offline scan can never overcome by being sent again from THIS phone.
 *
 * The offline drainer treats a 401/403 as a transient state of one ACCOUNT — a reset PIN, a still-
 * temporary PIN — and rightly keeps the scan queued, because that clears the moment somebody picks a
 * PIN. But a device-permission refusal is not about the account's PIN; it is about the PHONE. It will
 * answer 403 identically on every retry, and the 60-second heartbeat kept resending it — 75 times at
 * Qafur Məmmədov Parkı on 02.09.2026, the worker's screen still reading «göndərilməyi gözləyir» the
 * whole morning while, on the board, they were simply Qayıb.
 *
 * These codes are the ones `AttendanceController` returns from `RejectDeviceAsync`. A scan refused
 * with one of them is dropped and reported like any other definitive refusal, so the person is told
 * plainly instead of watching a spinner lie to them.
 */
export const DEVICE_PERMANENT_CODES = new Set([
  'SharedDeviceNotAllowed',  // this account may not use a shared phone; the fix is admin permission
  'DeviceAccountLimit',      // the phone already carries the maximum number of accounts
  'DeviceMismatch',          // the account is bound to a DIFFERENT phone
  'NoDeviceBound',           // the account has no phone bound at all
])

/** A 403 whose code means "this phone, permanently" — not "this account's PIN, for now". */
export function isPermanentDeviceReject(status: number, code: string | undefined): boolean {
  return status === 403 && DEVICE_PERMANENT_CODES.has(code ?? '')
}
