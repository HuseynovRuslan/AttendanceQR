import type { AdminEmployeeRow, EmployeeSelectionRow, EmployeeStats } from '../api/admin'
import type { ManagerEmployee } from '../api/manager'

/**
 * A branch manager's staff list, drawn by the same page the admin's is (Phase 4 — one staff list).
 *
 * The data stays the manager's own: GET /api/manager/employees, which the server narrows to their
 * branches and which never carries a salary, a role or a colleague's contacts. This turns it into the rows,
 * counts and selections the shared page draws — filtering and paging in the browser, as the manager's old
 * screen did (their roster is their branches, tens of people, not the company's nine hundred).
 */

export interface RosterQuery {
  page: number
  pageSize: number
  search: string
  locationId: string | null
  showLeft: boolean
}

/** A manager's row as the shared list draws it, with the server's answer to «may I change this one». */
export type ManagerRosterRow = AdminEmployeeRow & {
  /** The server's per-row answer — their branch and plain staff (or, since 2026-09-18, a fellow manager). */
  manageable: boolean
  /** Another manager, or the admin who clocks in at their branch. */
  isColleague: boolean
}

export function toRosterRow(e: ManagerEmployee): ManagerRosterRow {
  return {
    id: e.id,
    fullName: e.fullName,
    fatherName: e.fatherName,
    position: e.position,
    paperEmployer: e.paperEmployer ?? null,
    paperSite: e.paperSite ?? null,
    birthYear: e.birthYear,
    birthDate: e.birthDate,
    workStart: e.workStart,
    workEnd: e.workEnd,
    canFieldCheckIn: e.canFieldCheckIn,
    canShareDevice: e.canShareDevice,
    scheduleId: e.scheduleId,
    scheduleName: null,
    workCycleDays: e.workCycleDays,
    workCycleOnDays: e.workCycleOnDays,
    phoneNumber: e.phoneNumber,
    // Never sent to a manager — the list does not show the column for them.
    role: 'Employee',
    locationId: e.locationId,
    locationName: e.locationName,
    managedLocationIds: [],
    managedLocationNames: [],
    isActive: e.isActive,
    activated: e.activated,
    mustChangePin: false,
    lastActiveAtUtc: null,
    hasDevice: false,
    deviceLabel: null,
    boundAtUtc: null,
    manageable: e.manageable !== false,
    isColleague: e.isColleague === true,
  }
}

function matches(e: ManagerEmployee, q: Omit<RosterQuery, 'page' | 'pageSize'>): boolean {
  if (q.showLeft ? e.isActive : !e.isActive) return false
  if (q.locationId && e.locationId !== q.locationId) return false
  const s = q.search.trim().toLocaleLowerCase('az')
  if (!s) return true
  // The admin list's four fields, plus the branch name the manager's old screen searched too.
  return `${e.fullName} ${e.phoneNumber ?? ''} ${e.position ?? ''} ${e.locationName ?? ''} ${e.id}`
    .toLocaleLowerCase('az')
    .includes(s)
}

/** Everybody the filters match, in the order an Azerbaijani reader expects. */
export function filterRoster(all: ManagerEmployee[], q: Omit<RosterQuery, 'page' | 'pageSize'>): ManagerEmployee[] {
  return all.filter((e) => matches(e, q)).sort((a, b) => a.fullName.localeCompare(b.fullName, 'az') || a.id.localeCompare(b.id))
}

/** One page of it, and how many there are in all — the shape the admin roster endpoint answers with. */
export function rosterPage(all: ManagerEmployee[], q: RosterQuery): { items: ManagerRosterRow[]; total: number } {
  const matched = filterRoster(all, q)
  const start = (Math.max(1, q.page) - 1) * q.pageSize
  return { items: matched.slice(start, start + q.pageSize).map(toRosterRow), total: matched.length }
}

/**
 * The headline counts over the manager's active staff, narrowed by branch only — the same rule the admin
 * stats endpoint follows. Push and device facts are not part of a manager's roster, so those two read 0 and
 * the page does not show them to a manager.
 */
export function rosterStats(all: ManagerEmployee[], locationId: string | null): EmployeeStats {
  const active = all.filter((e) => e.isActive && (!locationId || e.locationId === locationId))
  const activated = active.filter((e) => e.activated).length
  return {
    total: active.length,
    activated,
    notStarted: active.length - activated,
    noPush: 0,
    noDevice: 0,
    leftCount: all.filter((e) => !e.isActive).length,
  }
}

/**
 * «Hamısını seç» for a manager: every matching person a bulk action may reach — their own branches' plain
 * staff. A colleague is editable one at a time, and the server's bulk grant never reaches them, so a count
 * that included them would report changing people it did not.
 */
export function rosterSelection(all: ManagerEmployee[], q: Omit<RosterQuery, 'page' | 'pageSize'>): EmployeeSelectionRow[] {
  return filterRoster(all, q)
    .filter((e) => e.manageable !== false && e.isColleague !== true)
    .map((e) => ({
      id: e.id,
      fullName: e.fullName,
      locationName: e.locationName,
      isActive: e.isActive,
      activated: e.activated,
      mustChangePin: false,
      canShareDevice: e.canShareDevice === true,
      canFieldCheckIn: e.canFieldCheckIn === true,
    }))
}
