import { randomInt } from 'node:crypto'
import { WEB_URL } from './env'
import { Direct, loginToken } from './api'
import { NAMES, PEOPLE, type Branch, type PersonKey } from './world'

// The admin-side operations global setup and teardown share. All of them go through Direct (plain
// fetch), never a traced context.

const BAKU = { latitude: 40.3777, longitude: 49.892 }

export function strongPin(): string {
  // PinRules refuses 1111, 1234/4321 and 1212-style PINs; draw until one passes.
  for (;;) {
    const p = String(randomInt(0, 10000)).padStart(4, '0')
    const d = p.split('').map(Number)
    const same = d.every((x) => x === d[0])
    const up = d.every((x, i) => i === 0 || x === d[i - 1] + 1)
    const down = d.every((x, i) => i === 0 || x === d[i - 1] - 1)
    const pairs = p.slice(0, 2) === p.slice(2)
    if (!same && !up && !down && !pairs) return p
  }
}

export function storageState(token: string): string {
  return JSON.stringify({
    cookies: [],
    origins: [{ origin: new URL(WEB_URL).origin, localStorage: [{ name: 'attendanceqr.jwt', value: token }] }],
  })
}

export function tokenSubject(token: string): string {
  return JSON.parse(Buffer.from(token.split('.')[1], 'base64url').toString('utf8')).sub as string
}

type Loc = { id: string; name: string; isActive: boolean; structureId: string | null }

export async function ensureBranches(admin: Direct): Promise<Record<Branch, string>> {
  const all = await admin.ok<Loc[]>('GET', '/api/admin/locations')
  const want: Record<Branch, string> = { A1: NAMES.branchA1, A2: NAMES.branchA2, B: NAMES.branchB }
  const out = {} as Record<Branch, string>
  for (const [key, name] of Object.entries(want) as [Branch, string][]) {
    let loc = all.find((l) => l.name === name)
    if (!loc) {
      loc = await admin.ok<Loc>('POST', '/api/admin/locations', {
        name,
        ...BAKU,
        radiusMeters: 100,
        shiftStart: '09:00',
        shiftEnd: '18:00',
        lateThresholdMinutes: 15,
        workDaysMask: 126,
        qrlessCheckIn: false,
        requireGeofence: false,
      })
    } else if (!loc.isActive) {
      await admin.ok('PUT', `/api/admin/locations/${loc.id}/active`, { isActive: true })
    }
    out[key] = loc.id
  }
  return out
}

export async function ensureStructure(admin: Direct, branches: Record<Branch, string>): Promise<string> {
  type S = { id: string; name: string; archived: boolean; locations: { id: string }[] }
  const list = await admin.ok<{ structures: S[] }>('GET', '/api/admin/structures')
  let s = list.structures.find((x) => x.name === NAMES.structure)
  if (!s) {
    const created = await admin.ok<{ id: string }>('POST', '/api/admin/structures', { name: NAMES.structure })
    s = { id: created.id, name: NAMES.structure, archived: false, locations: [] }
  } else if (s.archived) {
    await admin.ok('POST', `/api/admin/structures/${s.id}/restore`)
  }
  // A1 and A2 in the structure; B in none — the structure's edge is what the scope tests probe.
  const inside = new Set(s.locations.map((l) => l.id))
  if (!inside.has(branches.A1) || !inside.has(branches.A2)) {
    await admin.ok('PUT', '/api/admin/structures/assign', { structureId: s.id, locationIds: [branches.A1, branches.A2] })
  }
  const bInSome = list.structures.some((x) => x.locations.some((l) => l.id === branches.B))
  if (bInSome) await admin.ok('PUT', '/api/admin/structures/assign', { structureId: null, locationIds: [branches.B] })
  return s.id
}

export async function ensureShifts(admin: Direct, branches: Record<Branch, string>) {
  type Sh = { id: string; name: string }
  const all = await admin.ok<Sh[]>('GET', '/api/admin/schedules')
  const make = async (name: string, start: string, end: string) =>
    all.find((x) => x.name === name)?.id ??
    (
      await admin.ok<Sh>('POST', '/api/admin/schedules', {
        name,
        shiftStart: start,
        shiftEnd: end,
        locationId: branches.A1,
        lateThresholdMinutes: 15,
        workDaysMask: 126,
      })
    ).id
  return { morning: await make(NAMES.shiftMorning, '08:00', '17:00'), evening: await make(NAMES.shiftEvening, '14:00', '23:00') }
}

/** The admin's full update body from the admin's own read — EmployeeUpdateRequest blanks what it omits. */
export function updateBody(e: any, change: Record<string, unknown>) {
  return {
    fullName: e.fullName,
    email: e.email,
    locationId: e.locationId,
    role: e.role,
    isActive: e.isActive,
    phoneNumber: e.phoneNumber,
    fatherName: e.fatherName,
    position: e.position,
    birthYear: e.birthYear,
    birthDate: e.birthDate,
    workStart: e.workStart,
    workEnd: e.workEnd,
    monthlySalary: e.monthlySalary,
    photoExempt: e.photoExempt,
    canFieldCheckIn: e.canFieldCheckIn,
    canShareDevice: e.canShareDevice,
    qrlessCheckInOverride: e.qrlessCheckInOverride,
    requireGeofenceOverride: e.requireGeofenceOverride,
    scheduleId: e.scheduleId,
    workCycleDays: e.workCycleDays,
    workCycleOnDays: e.workCycleOnDays,
    workCycleAnchor: e.workCycleAnchor,
    managedLocationIds: e.managedLocationIds,
    firstName: e.firstName,
    lastName: e.lastName,
    paperEmployer: e.paperEmployer,
    paperSite: e.paperSite,
    ...change,
  }
}

/** The E2E person by name, confirmed by email — a namesake somebody else created is not ours. */
async function findPerson(admin: Direct, name: string, email: string): Promise<any | undefined> {
  for (const showLeft of [false, true]) {
    const r = await admin.ok<{ items: { id: string; fullName: string }[] }>(
      'GET',
      // Searched by the prefix, matched by full name here: the list's server-side search does not
      // find "E2E İşçi A1" by its own name (the capital İ does not lower-case the same way on both
      // sides of the comparison).
      `/api/admin/employees?pageSize=200&showLeft=${showLeft}&search=E2E`,
    )
    for (const hit of r.items.filter((e) => e.fullName === name)) {
      const e = await admin.ok('GET', `/api/admin/employees/${hit.id}`)
      if ((e.email ?? '').toLowerCase() === email) return e
    }
  }
  return undefined
}

export async function ensurePerson(admin: Direct, key: PersonKey, branches: Record<Branch, string>) {
  const spec = PEOPLE[key]
  let e = await findPerson(admin, spec.name, spec.email)
  if (!e) {
    const created = await admin.ok<{ employeeId: string }>('POST', '/api/admin/employees/invite', {
      fullName: spec.name,
      email: spec.email,
      locationId: branches[spec.home],
      role: spec.accountRole,
      // Signing-in people start on a temporary PIN (changed below); the rest never sign in, and
      // an unused 72-hour activation link is the least credential a record can have.
      activateWithPin: spec.signsIn,
    })
    e = await admin.ok('GET', `/api/admin/employees/${created.employeeId}`)
  }
  // Bring back what an interrupted run or a person may have changed.
  const want: Record<string, unknown> = {}
  if (!e.isActive) want.isActive = true
  if (e.locationId !== branches[spec.home]) want.locationId = branches[spec.home]
  if (e.role !== spec.accountRole) want.role = spec.accountRole
  const managed = (spec.managed ?? []).map((b) => branches[b]).sort()
  if (spec.accountRole === 'Manager' && JSON.stringify([...(e.managedLocationIds ?? [])].sort()) !== JSON.stringify(managed)) {
    want.managedLocationIds = managed
  }
  if (Object.keys(want).length) {
    await admin.ok('PUT', `/api/admin/employees/${e.id}`, updateBody(e, want))
  }
  return { id: e.id as string, name: e.fullName as string, email: spec.email }
}

/** Exactly the baseline grant, nothing left over from an interrupted live-change test. */
export async function ensureGrant(
  admin: Direct,
  key: PersonKey,
  personId: string,
  roles: Record<string, string>,
  structureId: string,
  branches: Record<Branch, string>,
) {
  type Row = { id: string; roleId: string; scope: string; structureId: string | null; locationId: string | null; system: boolean }
  const rows = await admin.ok<Row[]>('GET', `/api/admin/access/assignments?employeeId=${personId}`)
  const g = PEOPLE[key].grant
  const wanted = (r: Row) =>
    !!g &&
    r.roleId === roles[g.template] &&
    r.scope === g.scope &&
    (g.scope === 'Structure'
      ? r.structureId === structureId
      : (g.branches ?? []).some((b) => branches[b] === r.locationId))
  for (const r of rows) {
    if (!r.system && !wanted(r)) await admin.ok('POST', `/api/admin/access/assignments/${r.id}/revoke`)
  }
  if (!g) return
  const missing = g.scope === 'Structure' ? !rows.some(wanted) : (g.branches ?? []).some((b) => !rows.some((r) => wanted(r) && r.locationId === branches[b]))
  if (missing) {
    const r = await admin.post('/api/admin/access/assignments', {
      employeeId: personId,
      roleId: roles[g.template],
      scope: g.scope,
      structureId: g.scope === 'Structure' ? structureId : null,
      locationIds: g.scope === 'Branch' ? (g.branches ?? []).map((b) => branches[b]) : null,
    })
    if (r.status !== 200 && r.body?.error !== 'AlreadyHeld') {
      throw new Error(`rol verilmədi (${key}): ${r.status} ${r.body?.error ?? ''}`)
    }
  }
}

/** Employees a previous run's tests added and did not get to delete. */
export async function purgeCreated(admin: Direct) {
  for (const showLeft of [false, true]) {
    const r = await admin.ok<{ items: { id: string; fullName: string }[] }>(
      'GET',
      `/api/admin/employees?pageSize=200&showLeft=${showLeft}&search=${encodeURIComponent(NAMES.created)}`,
    )
    for (const e of r.items.filter((x) => x.fullName.startsWith(NAMES.created))) {
      await admin.ok('DELETE', `/api/admin/employees/${e.id}?force=true`)
    }
  }
}

/** Fresh credentials for one E2E person: admin reset → temporary PIN → their own new PIN. */
export async function rotate(admin: Direct, personId: string, email: string): Promise<{ pin: string; token: string }> {
  const { tempPin } = await admin.ok<{ tempPin: string }>('POST', `/api/admin/employees/${personId}/reset-pin`)
  const temp = await loginToken({ phone: email, pin: tempPin })
  const pin = strongPin()
  const set = await new Direct(temp).ok<{ token: string }>('POST', '/api/auth/set-initial-pin', { newPin: pin })
  return { pin, token: set.token }
}

