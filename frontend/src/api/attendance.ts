import { apiRequest } from './client'
import { type AttendanceReport } from './admin'

/** POST /api/attendance/reason — attach a late-arrival ("late") or early-departure ("early") reason to
 * the caller's own record. Skippable, so failures are non-fatal. */
export function submitAttendanceReason(recordId: string, kind: 'late' | 'early', reason: string) {
  return apiRequest<{ ok: boolean } | { error: string }>('/api/attendance/reason', {
    method: 'POST',
    body: { recordId, kind, reason },
  })
}

export interface AttendanceRecord {
  recordId: string
  attendanceDate: string // "yyyy-MM-dd"
  locationId: string
  checkInAtUtc: string | null
  checkOutAtUtc: string | null
  status: 'OnTime' | 'Late' | 'Absent' | 'Incomplete'
  // Face audit (optional — older backends omit).
  faceMatchScore?: number | null
  faceMatchStatus?: string
  // Name of the admin/manager who set this record by hand (null/absent for a real scan).
  manualByName?: string | null
  /** The check-out came from this employee's own field check-out — they went home from the site
   *  rather than back past the poster. NOT a manual entry: it carries their GPS and selfie. */
  closedByFieldVisit?: boolean
}

/** GET /api/attendance/me — this employee's full check-in/out history, newest first. Self-scoped
 * server-side from the JWT, no params. */
export function getMyAttendance() {
  return apiRequest<AttendanceRecord[]>('/api/attendance/me')
}

/** GET /api/attendance/me/today — just today's record (null if none yet). Cheap, single indexed row;
 *  the Scan page waits on this before opening the camera, so it must not pull the whole history. */
export function getMyToday() {
  return apiRequest<AttendanceRecord | null>('/api/attendance/me/today')
}

/** POST /api/attendance/scan-failure — report a scan that never left the phone (no GPS, permission
 * denied, position too coarse). Fire-and-forget: the employee's flow never waits on it, and the
 * server de-duplicates retries — but the attempt now shows up in the admin "Problemlər" screen
 * instead of vanishing. */
/** `scanAtUtc` is WHEN the scan was taken, for an offline report sent later — the audit row's own
 *  timestamp is the moment of reporting, so without it an admin cannot tell which day was lost. */
/** Which OS and what the browser thinks of the geolocation permission, best-effort and never throwing. */
async function failureTags(): Promise<{ platform: string; permissionState: string }> {
  try {
    const { platform, permissionState } = await import('../lib/geo')
    return { platform: platform(), permissionState: await permissionState() }
  } catch {
    return { platform: 'other', permissionState: 'unknown' }
  }
}

export async function reportScanFailure(
  reason: string,
  accuracyMeters?: number,
  scanAtUtc?: string,
  /** Send as a saved profile rather than the active session — see RequestOptions.token. */
  token?: string,
) {
  // Platform + permission state collected HERE, not at the call sites: every failure report should
  // carry them, and there are a dozen callers. 62 of 132 GPS-blocked people had phones that WORKED
  // and then broke — without knowing the platform, the "iOS Bir dəfə icazə ver" theory was untestable.
  const { platform, permissionState } = await failureTags()
  return apiRequest<void>('/api/attendance/scan-failure', {
    method: 'POST',
    body: {
      reason, accuracyMeters: accuracyMeters ?? null, scanAtUtc: scanAtUtc ?? null,
      platform, permissionState,
    },
    ...(token ? { token } : {}),
  })
}

export interface MyDeviceStatus {
  bound: boolean
  /** Killed by an admin — a scan will NOT adopt it back; the employee has to ask. */
  revoked: boolean
  deviceLabel: string | null
  boundAtUtc: string | null
  activeDeviceCount: number
  autoBindEnabled: boolean
  /** The employee's assigned location, for the pre-scan geofence check (null if none). */
  location: { name: string; latitude: number; longitude: number; radiusMeters: number } | null
}

/** GET /api/attendance/me/device — is THIS browser bound to my account? Safari and the installed app
 * are separate contexts, so an employee can be bound in one and not the other without knowing. */
export function getMyDeviceStatus(fingerprint: string) {
  return apiRequest<MyDeviceStatus>(`/api/attendance/me/device?fingerprint=${encodeURIComponent(fingerprint)}`)
}

export interface MyProfile {
  fullName: string
  /** Null for phone-only employees (email became optional). */
  email: string | null
  role: string
  position: string | null
  locationName: string | null
  /** Full date of birth "yyyy-MM-dd" (null if unset) — for the home-screen birthday greeting. */
  birthDate?: string | null
  /** False when an admin has waived the check-in selfie for this employee. */
  photoRequired?: boolean
  /** True when an admin has granted this employee field/mobile check-in ("Sahə ziyarəti"). Gates the
   *  menu row + the home self-report; off by default so a plain office worker never sees any of it. */
  canFieldCheckIn?: boolean
  /** May this person take part in a shared brigade phone? Gates the account switcher: without it the
   *  server refuses to adopt the handset, so the button could only ever fail at the poster. */
  canShareDevice?: boolean
  /** Whether a profile picture is set. Deliberately not a URL — see getMyAvatar. */
  hasAvatar?: boolean
  /** When it last changed; the client's cached copy is stale when this differs. */
  avatarUpdatedAtUtc?: string | null
  /** True until the employee has accepted the data-processing notice — the app blocks until then. */
  consentRequired?: boolean
  /** Check-ins this month whose photo showed no face — shown to the employee themselves. */
  unverifiedCheckIns?: number
  /** Whether the most recent check-in was one of them; the scan screen warns before the camera opens. */
  lastCheckInUnverified?: boolean
  /** The employee's effective shift hours ("HH:mm"), so the home screen can tell when a still-open
   *  check-in is overdue (shift is over) and nudge them to check out. */
  shiftStart?: string | null
  shiftEnd?: string | null
}

/** Records the employee's acceptance of the data-processing notice. Idempotent — the first
 *  acceptance is the one that stands. */
export function acceptConsent() {
  return apiRequest<{ acceptedAtUtc: string } | { error: string }>('/api/attendance/me/consent', {
    method: 'POST',
  })
}

/** GET /api/attendance/me/profile — the caller's own name/location for the home greeting + menu. */
export function getMyProfile(
  /** Ask about a SAVED PROFILE rather than the active session — used when adding one, whose name is
   *  not in the JWT. Omitted everywhere else. See RequestOptions.token. */
  token?: string,
) {
  return apiRequest<MyProfile>('/api/attendance/me/profile', token ? { token } : {})
}

// --- profile picture (PROFİL ŞƏKLİ) -------------------------------------------------------------
// A picture the employee chose. Kept strictly apart from the reference selfie below: nothing compares
// this to anything and it never reaches the face-match worker.

/** POST /api/attendance/me/avatar — set (or replace) my own profile picture. */
export function setMyAvatar(photoBase64: string) {
  return apiRequest<{ ok: boolean; avatarUpdatedAtUtc: string } | { error: string }>('/api/attendance/me/avatar', {
    method: 'POST',
    body: { photoBase64 },
  })
}

/** DELETE /api/attendance/me/avatar — back to initials. */
export function deleteMyAvatar() {
  return apiRequest<{ ok: boolean } | { error: string }>('/api/attendance/me/avatar', { method: 'DELETE' })
}

/**
 * GET /api/attendance/me/avatar — the picture itself, as a data URL, so it can be kept and shown with
 * no signal. 404 when there is none.
 *
 * `token` asks on behalf of a SAVED PROFILE rather than the active session — the account switcher
 * calls this once, when an account is added, so thirty faces cost thirty requests in total.
 */
export function getMyAvatar(token?: string) {
  return apiRequest<{ dataUrl: string } | { error: string }>(
    '/api/attendance/me/avatar',
    token ? { token } : {},
  )
}

/** POST /api/attendance/me/reference-photo — set the caller's own face-audit reference selfie. Used by
 * the first-login flow for temp-PIN accounts (which never took an activation selfie). */
export function setMyReferencePhoto(photoBase64: string) {
  return apiRequest<{ ok: boolean } | { error: string }>('/api/attendance/me/reference-photo', {
    method: 'POST',
    body: { photoBase64 },
  })
}

export interface MissedCheckoutStatusResp {
  /** The oldest past day left open (check-in, no check-out), or null. */
  openDay: { recordId: string; attendanceDate: string; checkInAtUtc: string } | null
  /** This calendar month's self-reports (pending/approved). */
  monthlyCount: number
  limit: number
  /** A request for the open day is already awaiting approval. */
  pending: boolean
}

/** GET /api/attendance/missed-checkout — does the caller have a forgotten-checkout day to fix, and
 * how many times have they used this path this month (for the home banner + its deterrent count). */
export function getMissedCheckoutStatus() {
  return apiRequest<MissedCheckoutStatusResp>('/api/attendance/missed-checkout')
}

/** POST /api/attendance/missed-checkout — report the time they actually left for a forgotten day. It
 * becomes a request a manager/admin approves; the employee never writes their own hours. */
export function submitMissedCheckout(recordId: string, checkOutAtUtc: string, reason: string) {
  return apiRequest<{ id: string } | { error: string }>('/api/attendance/missed-checkout', {
    method: 'POST',
    body: { recordId, checkOutAtUtc, reason },
  })
}

/** GET /api/reports/summary — aggregated totals for this employee over a date range. Employee-role
 * JWTs are forced to their own records server-side regardless of any other param, so this is the
 * same endpoint the admin reports page uses, just naturally self-scoped. */
/**
 * The signed-in person's OWN figures for the profile screen.
 *
 * Deliberately not getSummary(), which scopes by role: an admin calling it got the whole company's
 * totals rendered as their personal card — 947 days, 7,893 hours, and a banner saying 601 days absent
 * this month, which told them to ask their manager to fix an absence that was never theirs.
 */
export function getMySummary(from: string, to: string) {
  const q = new URLSearchParams({ from, to })
  return apiRequest<AttendanceReport | { error: string }>(`/api/reports/my-summary?${q}`)
}

export type { AttendanceReport }

// --- admin: view/correct another employee's raw records ------------------

/** GET /api/attendance/employee/{id} — admin/manager view of one employee's full history. */
export function getEmployeeAttendance(employeeId: string) {
  return apiRequest<AttendanceRecord[] | { error: string }>(`/api/attendance/employee/${employeeId}`)
}

/** Photo-audit: presigned MinIO URLs for a record's check-in selfie + the employee's reference selfie. */
export interface PhotoUrlResponse {
  hasPhoto: boolean
  checkInPhotoUrl: string | null
  checkInPhotoTakenAtUtc: string | null
  referencePhotoUrl: string | null
  faceMatchScore?: number | null
  faceMatchStatus?: string
}

/** GET /api/attendance/{recordId}/photo-url — short-lived (~5 min) presigned URLs for the two photos
 * so the manager/admin can eyeball them side by side. Scoped server-side by LocationScopeRules; the
 * URLs expire, so re-call each time a photo is (re)opened rather than caching them. */
export function getPhotoUrl(recordId: string) {
  return apiRequest<PhotoUrlResponse | { error: string }>(`/api/attendance/${recordId}/photo-url`)
}

export interface AdminAttendanceRecord {
  recordId: string
  employeeId: string
  attendanceDate: string
  checkInAtUtc: string | null
  checkOutAtUtc: string | null
  status: string
}

export interface OpenRecord {
  recordId: string
  employeeId: string
  employeeName: string
  locationName: string
  attendanceDate: string
  checkInAtUtc: string
}

/** GET /api/admin/attendance/open — past days with a check-in but no check-out (today excluded).
 * The nightly summary counts these as 0 hours worked until an admin sets a check-out time. */
export function getOpenRecords() {
  return apiRequest<OpenRecord[]>('/api/admin/attendance/open')
}

/** PUT /api/admin/attendance/{recordId} — correct an existing record's check-in/out (either or
 * both; omitted fields are left as-is). Recomputes that date's summary immediately. */
export function adminUpdateRecord(recordId: string, checkInAtUtc?: string, checkOutAtUtc?: string) {
  return apiRequest<AdminAttendanceRecord | { error: string }>(`/api/admin/attendance/${recordId}`, {
    method: 'PUT',
    body: { checkInAtUtc: checkInAtUtc ?? null, checkOutAtUtc: checkOutAtUtc ?? null },
  })
}

/** POST /api/admin/attendance — create a record for a day the employee never scanned at all.
 * checkInAtUtc required; 409 if a record for that (employee, date) already exists. */
export function adminCreateRecord(employeeId: string, date: string, checkInAtUtc: string, checkOutAtUtc?: string) {
  return apiRequest<AdminAttendanceRecord | { error: string }>('/api/admin/attendance', {
    method: 'POST',
    body: { employeeId, date, checkInAtUtc, checkOutAtUtc: checkOutAtUtc ?? null },
  })
}

/** POST /api/admin/attendance/{recordId}/clear-checkout — undo an accidental check-out (record
 * goes back to "checked in, not out" so the employee can check out properly later). */
export function adminClearCheckout(recordId: string) {
  return apiRequest<AdminAttendanceRecord | { error: string }>(`/api/admin/attendance/${recordId}/clear-checkout`, {
    method: 'POST',
  })
}

/**
 * DELETE /api/admin/attendance/{recordId} — remove a record entirely.
 *
 * For a PHANTOM day, which editing cannot fix: a night worker whose shift was not known to be a
 * night shift scans twice in the morning, the first scan opens a day that never happened and the
 * second closes it. That row then blocks every later scan with `AlreadyCompleted`, and any times at
 * all on that date keep the block — so the row itself has to go. Irreversible; the selfie goes with
 * it and an audit line records what was removed.
 */
export function adminDeleteRecord(recordId: string) {
  return apiRequest<{ deleted: string } | { error: string }>(`/api/admin/attendance/${recordId}`, {
    method: 'DELETE',
  })
}
