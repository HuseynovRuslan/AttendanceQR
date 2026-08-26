// Several accounts on ONE phone — the crew phone.
//
// Around 260 of these workers have no phone at all. They water trees along the airport road, sweep a
// stretch with no gate and no poster, and the brigadier who assigns their work does it by telephone
// from somewhere else. The obvious answer — let the brigadier tick a list of who turned up — is a
// DECLARATION, and a list of ticks is exactly as easy to fill in from a sofa as from a work site.
//
// So instead the phone changes hands. Whoever is on site holds it, and each worker switches to their
// own profile, scans, and takes their own selfie. The GPS is the phone's real position and the face is
// the worker's real face, which is what makes the record evidence rather than a claim — and the reason
// it is worth the twenty seconds per person that switching costs.
//
// Nothing here is a new kind of session. A profile IS the ordinary JWT that ordinary login issues; the
// only new idea is that the device keeps more than one of them and can put a different one into the
// active slot. Everything downstream — scan, device binding, the offline queue — already works per
// employee and needed no change to accept this.
import { getImpersonation, getToken } from '../api/client'
import { isOperatorHost } from './host'
import { decodeJwt } from './jwt'

const KEY = 'attendanceqr.profiles'

/**
 * How many accounts one device may hold. A crew is 20–30 people and the biggest we were told about is
 * 30, so 60 leaves room to spare while still bounding what a wedged phone can accumulate. Each entry
 * is a JWT and a name — well under a kilobyte — so the cap is about keeping the list COMPREHENSIBLE
 * on a small screen, not about storage.
 */
export const MAX_PROFILES = 60

/** One saved account: enough to show a row and to speak as that person. */
export interface SavedProfile {
  employeeId: string
  /** Full name, fetched once when the profile was added. Shown in the switcher. */
  name: string
  /** That account's own JWT. Long-lived (the backend issues ~100-year tokens), so it keeps working. */
  token: string
  addedAtMs: number
}

// --- pure list operations (no browser needed, so they are testable) ---------------------------------

/**
 * Add or refresh one profile. Keyed on employeeId, so re-adding somebody who changed their PIN
 * REPLACES the dead token instead of leaving two rows with the same name and one of them broken.
 * The refreshed entry keeps its original position — a switcher whose rows jump around as people scan
 * is one where the holder taps the wrong name.
 */
export function upsert(list: SavedProfile[], profile: SavedProfile): SavedProfile[] {
  const at = list.findIndex((p) => p.employeeId === profile.employeeId)
  if (at >= 0) {
    const next = list.slice()
    next[at] = { ...profile, addedAtMs: list[at].addedAtMs }
    return next
  }
  // Oldest goes when full. The crew phone is set up once and then used; the entry nobody has switched
  // to since the beginning is the one least likely to be needed at the poster this morning.
  return [...list, profile].slice(-MAX_PROFILES)
}

export function withoutId(list: SavedProfile[], employeeId: string): SavedProfile[] {
  return list.filter((p) => p.employeeId !== employeeId)
}

// --- storage ----------------------------------------------------------------------------------------

export function listProfiles(): SavedProfile[] {
  try {
    const raw = localStorage.getItem(KEY)
    if (!raw) return []
    const parsed = JSON.parse(raw) as unknown
    if (!Array.isArray(parsed)) return []
    // Anything malformed is dropped rather than trusted: a half-written row would put a broken token
    // behind a real person's name, and the holder would find that out at the poster.
    return parsed.filter(
      (p): p is SavedProfile =>
        !!p && typeof p === 'object' && typeof (p as SavedProfile).employeeId === 'string'
        && typeof (p as SavedProfile).token === 'string' && typeof (p as SavedProfile).name === 'string',
    )
  } catch {
    return []
  }
}

function write(list: SavedProfile[]): void {
  try {
    if (list.length === 0) localStorage.removeItem(KEY)
    else localStorage.setItem(KEY, JSON.stringify(list))
  } catch {
    /* private mode / quota — the active session still works, which is what matters */
  }
}

export function saveProfile(profile: SavedProfile): void {
  write(upsert(listProfiles(), profile))
}

export function removeProfile(employeeId: string): void {
  write(withoutId(listProfiles(), employeeId))
}

export function clearProfiles(): void {
  write([])
}

/**
 * Remember whoever is signed in right now, so the person who set the phone up can always get back to
 * their own account without typing a PIN. Called after every ordinary login.
 *
 * Deliberately refuses in two cases, both for the same reason — a token that outlives the session in
 * a list on a shared machine:
 *   • while impersonating, where the token is an operator borrowing a tenant admin (the exact thing
 *     clearToken() goes out of its way to shred), and
 *   • on the operator console host, where the token is cross-tenant and allowlisted. Switching
 *     accounts is a crew-phone idea; it has no business holding platform credentials.
 */
export function rememberCurrent(name: string, token?: string): void {
  if (getImpersonation() || isOperatorHost()) return
  const t = token ?? getToken()
  if (!t) return
  const sub = decodeJwt(t)?.sub
  if (!sub) return
  saveProfile({ employeeId: sub, name, token: t, addedAtMs: Date.now() })
}

/** The profile currently in the active slot, if it is one we saved. */
export function activeProfileId(): string | null {
  const t = getToken()
  return t ? decodeJwt(t)?.sub ?? null : null
}
