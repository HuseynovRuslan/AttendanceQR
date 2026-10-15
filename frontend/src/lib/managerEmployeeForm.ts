import type { ManagerEmployee, ManagerEmployeeInput } from '../api/manager'

/** Prefer stored parts; fall back to splitting FullName (last token = surname) for un-backfilled rows. */
export function splitName(
  first: string | null | undefined,
  last: string | null | undefined,
  full: string,
): { first: string; last: string } {
  if (first || last) return { first: first ?? '', last: last ?? '' }
  const toks = (full ?? '').trim().split(/\s+/).filter(Boolean)
  if (toks.length <= 1) return { first: toks[0] ?? '', last: '' }
  return { first: toks.slice(0, -1).join(' '), last: toks[toks.length - 1] }
}

/**
 * The edit form for one of a manager's staff, read back from the row the roster returned.
 *
 * Everything the update carries has to land here: the save is a spread of this form, and the server
 * clears whatever it omits (EmployeeUpdateRequest null-defaults every field). Until 15.10.2026 the two
 * check-in overrides were missing, so every save, «İşdən çıxart» and «Geri qaytar» from the manager's
 * roster quietly put somebody with a QR-less or no-fence exception back on their branch's rule.
 */
export function managerFormFrom(e: ManagerEmployee): ManagerEmployeeInput {
  const nm = splitName(e.firstName, e.lastName, e.fullName)
  return {
    fullName: e.fullName, firstName: nm.first, lastName: nm.last,
    // Phone-only employees have a null email now — and legacy placeholder addresses are hidden.
    email: e.email && !e.email.endsWith('@baki.local') ? e.email : null,
    phoneNumber: e.phoneNumber, fatherName: e.fatherName, position: e.position,
    locationId: e.locationId, birthDate: e.birthDate, birthYear: e.birthYear,
    workStart: e.workStart, workEnd: e.workEnd, photoExempt: e.photoExempt,
    canFieldCheckIn: e.canFieldCheckIn, isActive: e.isActive,
    scheduleId: e.scheduleId,
    workCycleDays: e.workCycleDays, workCycleOnDays: e.workCycleOnDays, workCycleAnchor: e.workCycleAnchor,
    qrlessCheckInOverride: e.qrlessCheckInOverride ?? null,
    requireGeofenceOverride: e.requireGeofenceOverride ?? null,
    paperEmployer: e.paperEmployer ?? null, paperSite: e.paperSite ?? null,
  }
}
