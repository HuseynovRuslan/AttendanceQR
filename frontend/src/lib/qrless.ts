// Whether THIS phone's branch has a QR poster — remembered on the device, and the rule that turns the
// answer into a route on the scan screen.
//
// The fact comes from /me/profile (Location.QrlessCheckIn). The scan screen has to know it BEFORE it
// opens a camera, and it must know it with no signal too: a worker on a stretch of road with one bar
// is exactly who this feature is for, and a profile request that has not answered must not decide
// between "open the QR viewfinder" and "take the selfie". So every profile load writes the fact here,
// keyed by employee — a crew phone carries several people — and the scan screen reads it back when the
// request is late. A stale value fails loud, not silent: the server refuses an empty token at a
// branch that has a poster, and the screen names that refusal.

const key = (employeeId: string) => `attendanceqr.qrless.${employeeId}`

/** The corner of Storage this module needs — injectable so the rule can be tested without a browser. */
export interface KeyValueStore {
  getItem(k: string): string | null
  setItem(k: string, v: string): void
}

function browserStore(): KeyValueStore | null {
  try {
    return typeof localStorage === 'undefined' ? null : localStorage
  } catch {
    return null
  }
}

export function rememberQrless(employeeId: string | null, qrless: boolean, store: KeyValueStore | null = browserStore()): void {
  if (!employeeId || !store) return
  try {
    store.setItem(key(employeeId), qrless ? '1' : '0')
  } catch {
    // Storage full or blocked: the next profile load tries again; the scan screen falls back to "unknown".
  }
}

/** True / false when the phone has heard about this person's branch; null when it never has. */
export function recallQrless(employeeId: string | null, store: KeyValueStore | null = browserStore()): boolean | null {
  if (!employeeId || !store) return null
  try {
    const v = store.getItem(key(employeeId))
    return v === '1' ? true : v === '0' ? false : null
  } catch {
    return null
  }
}

export type QrlessRoute = 'selfie' | 'camera'

/**
 * Where the scan screen goes once the fence has passed. Decided by PLACE, the way the server decides:
 * an empty token is fenced to the employee's OWN branch, so the selfie is only right when the phone
 * is inside that branch (or inside no known fence at all — the escape hatch, where the server is the
 * judge either way). Inside a DIFFERENT branch's fence there is a poster to scan, and the camera is the
 * path that still works — a Socar-1 driver helping at the depot for a day must not be told he is
 * 12 km from Socar-1.
 *
 * `known` is the branch fact: true = no poster, false = has one, null = never learned. Unknown means
 * the camera, which has been the default for every scan ever made — and the screen says the branch
 * could not be loaded rather than leaving somebody aiming at nothing.
 */
export function qrlessRoute(args: {
  known: boolean | null
  ownLocationId?: string | null
  insideId?: string | null
}): QrlessRoute {
  if (args.known !== true) return 'camera'
  if (args.insideId == null) return 'selfie'
  // An older server sends branches without ids; the fallback list is the assigned branch itself.
  if (!args.ownLocationId) return 'selfie'
  return args.insideId === args.ownLocationId ? 'selfie' : 'camera'
}
