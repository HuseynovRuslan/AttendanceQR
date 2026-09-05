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

/**
 * The same question for a rejection that is not about the phone at all: where the scan was taken.
 *
 * A queued scan carries the position it was captured with, frozen into the payload. So an
 * OutsideRadius refusal can never be overcome by sending it again — the coordinates in the retry are
 * byte-for-byte the ones that were just refused. The drainer nevertheless read its 403 as an
 * ACCOUNT state ("the PIN was reset, it will clear") and kept the item, so the 60-second heartbeat
 * resent one frozen tap for hours: 132 refusals from a single pair of coordinates at Bayıl yolu,
 * 277 log rows from 58 real taps across the company.
 *
 * Two costs, and the second is the one the worker feels. The admin's Problems screen fills with
 * phantom rejections that make a geofence look broken when one person tapped once; and the phone
 * keeps promising «göndərilməyi gözləyir» for a scan that will never land, so nobody goes looking
 * for the real problem until they are marked absent.
 *
 * Dropped and reported instead — exactly what the device codes above earned for the same reason.
 */
export function isPermanentQueuedReject(status: number, code: string | undefined): boolean {
  return isPermanentDeviceReject(status, code) || (status === 403 && code === 'OutsideRadius')
}
