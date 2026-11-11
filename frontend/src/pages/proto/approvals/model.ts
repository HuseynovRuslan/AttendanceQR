/**
 * The approval prototype's rules, kept pure so the screens and the tests read the same decisions.
 *
 * This is a design prototype on demo data (see ProtoApp): nothing here talks to the server. The rules are the
 * ones the backend plan (docs/tesdiq-sistemi-plan.md) proposes, so the screens can be judged against them before
 * a line of server code exists:
 * - a Timekeeper asks, for their own branch only, and never decides;
 * - the Operator of the branch's structure decides; a branch in no structure goes to the Company administrator;
 * - nobody decides a request they created;
 * - a refusal needs a reason; a decision happens once (a second one is refused, not repeated);
 * - an Operator may act directly inside their structure — applied at once, written to the audit trail;
 * - a person with history is never deleted — deactivation is the way to let somebody go.
 */

export type RoleKind = 'companyAdmin' | 'operator' | 'viewer' | 'timekeeper'

export type RequestType =
  | 'employee_create'
  | 'employee_update'
  | 'employee_activation'
  | 'employee_delete'
  | 'permission'
  | 'vacation'
  | 'sick'
  | 'attendance_fix'
  | 'shift_change'

export type RequestStatus = 'pending' | 'approved' | 'rejected' | 'cancelled'

export interface Structure {
  id: string
  name: string
  operatorId: string
  viewerIds: string[]
}

export interface Branch {
  id: string
  name: string
  structureId: string | null
  timekeeperId: string | null
  viewerIds: string[]
}

export interface Employee {
  id: string
  name: string
  position: string
  branchId: string
  phone: string
  shift: string
  active: boolean
  /** Has attendance or other records — such a person is never deleted, only deactivated. */
  hasHistory: boolean
}

/** Somebody using the panel in the prototype — the roles of the approved model, on fictional people. */
export interface Persona {
  id: string
  name: string
  kind: RoleKind
  /** Structure scope (Operator, structure Viewer) or branch scope (Timekeeper, branch Viewer). */
  structureId?: string
  branchId?: string
}

export interface Change {
  field: string
  label: string
  before: string | null
  after: string | null
}

export type EventKind = 'created' | 'approved' | 'rejected' | 'cancelled' | 'applied' | 'direct'

export interface RequestEvent {
  at: string
  actorId: string
  kind: EventKind
  note?: string
}

export interface ChangeRequest {
  id: string
  type: RequestType
  status: RequestStatus
  /** Null for a new person (employee_create). */
  employeeId: string | null
  branchId: string
  createdById: string
  createdAt: string
  dateFrom?: string
  dateTo?: string
  timeFrom?: string
  timeTo?: string
  note: string
  changes: Change[]
  decidedById?: string
  decidedAt?: string
  decisionNote?: string
  /** Started and applied by an Operator inside their own structure — no approval step. */
  direct?: boolean
  events: RequestEvent[]
}

export interface World {
  companyName: string
  structures: Structure[]
  branches: Branch[]
  employees: Employee[]
  personas: Persona[]
  requests: ChangeRequest[]
}

export const TYPE_LABEL: Record<RequestType, string> = {
  employee_create: 'Yeni işçi',
  employee_update: 'Məlumat dəyişikliyi',
  employee_activation: 'Deaktiv / aktiv etmək',
  employee_delete: 'İşçini silmək',
  permission: 'Saatlıq və ya günlük icazə',
  vacation: 'Məzuniyyət',
  sick: 'Xəstəlik',
  attendance_fix: 'Davamiyyət düzəlişi',
  shift_change: 'Növbə dəyişikliyi',
}

export const STATUS_LABEL: Record<RequestStatus, string> = {
  pending: 'Gözləyir',
  approved: 'Təsdiqləndi',
  rejected: 'Rədd edildi',
  cancelled: 'Ləğv edildi',
}

export const ROLE_LABEL: Record<RoleKind, string> = {
  companyAdmin: 'Şirkət administratoru',
  operator: 'Operator',
  viewer: 'Müşahidəçi',
  timekeeper: 'Tabelçi',
}

/** Types that need a date range; `permission` may also carry hours (one day, from–to). */
export const NEEDS_DATES: ReadonlySet<RequestType> = new Set(['permission', 'vacation', 'sick', 'attendance_fix', 'shift_change'])
/** Types about an existing person (everything but a new one). */
export const NEEDS_EMPLOYEE: ReadonlySet<RequestType> = new Set(
  (Object.keys(TYPE_LABEL) as RequestType[]).filter((t) => t !== 'employee_create'),
)

/** A request has waited too long when it is still pending a day after it was made. */
export const LONG_WAIT_HOURS = 24

// --- who sees and does what ------------------------------------------------------------------------------

export function branchById(w: World, id: string): Branch | undefined {
  return w.branches.find((b) => b.id === id)
}

export function structureOf(w: World, branchId: string): Structure | undefined {
  const b = branchById(w, branchId)
  return b?.structureId ? w.structures.find((s) => s.id === b.structureId) : undefined
}

/** The branches a persona's area covers. */
export function branchesInScope(w: World, p: Persona): Branch[] {
  switch (p.kind) {
    case 'companyAdmin':
      return w.branches
    case 'operator':
      return w.branches.filter((b) => b.structureId === p.structureId)
    case 'viewer':
      return p.structureId ? w.branches.filter((b) => b.structureId === p.structureId) : w.branches.filter((b) => b.id === p.branchId)
    case 'timekeeper':
      return w.branches.filter((b) => b.id === p.branchId)
  }
}

export function inScope(w: World, p: Persona, branchId: string): boolean {
  return branchesInScope(w, p).some((b) => b.id === branchId)
}

/** Who decides a request from this branch: the Operator of its structure, else the Company administrator. */
export function approverIdFor(w: World, branchId: string): string | undefined {
  const s = structureOf(w, branchId)
  if (s) return s.operatorId
  return w.personas.find((p) => p.kind === 'companyAdmin')?.id
}

export function canCreate(w: World, p: Persona, branchId: string): boolean {
  if (p.kind === 'timekeeper') return p.branchId === branchId
  if (p.kind === 'operator') return inScope(w, p, branchId)
  return false
}

export function canDecide(w: World, p: Persona, r: ChangeRequest): boolean {
  return r.status === 'pending' && r.createdById !== p.id && approverIdFor(w, r.branchId) === p.id
}

export function canCancel(p: Persona, r: ChangeRequest): boolean {
  return r.status === 'pending' && r.createdById === p.id
}

export function visibleRequests(w: World, p: Persona): ChangeRequest[] {
  const scope = new Set(branchesInScope(w, p).map((b) => b.id))
  return w.requests.filter((r) => scope.has(r.branchId))
}

export function hoursWaiting(r: ChangeRequest, now: Date): number {
  return (now.getTime() - new Date(r.createdAt).getTime()) / 3_600_000
}

export function isLongWait(r: ChangeRequest, now: Date): boolean {
  return r.status === 'pending' && hoursWaiting(r, now) >= LONG_WAIT_HOURS
}

// --- a new request --------------------------------------------------------------------------------------

export interface Draft {
  type: RequestType
  branchId: string
  employeeId: string | null
  dateFrom: string
  dateTo: string
  timeFrom: string
  timeTo: string
  note: string
  /** employee_create / employee_update: the proposed values. */
  fields: { name: string; position: string; phone: string }
  /** employee_activation: the state asked for. */
  makeActive: boolean
  shift: string
}

export function emptyDraft(branchId: string): Draft {
  return {
    type: 'permission', branchId, employeeId: null, dateFrom: '', dateTo: '', timeFrom: '', timeTo: '', note: '',
    fields: { name: '', position: '', phone: '' }, makeActive: false, shift: '',
  }
}

/** Every reason a draft cannot go yet, by field — the form shows them where they belong. */
export function validate(w: World, d: Draft): Partial<Record<'employee' | 'dates' | 'times' | 'note' | 'fields' | 'shift', string>> {
  const errors: Partial<Record<'employee' | 'dates' | 'times' | 'note' | 'fields' | 'shift', string>> = {}
  const emp = d.employeeId ? w.employees.find((e) => e.id === d.employeeId) : undefined
  if (NEEDS_EMPLOYEE.has(d.type) && !emp) errors.employee = 'İşçini seçin'
  if (emp && emp.branchId !== d.branchId) errors.employee = 'Bu işçi başqa filialdadır'
  if (d.type === 'employee_delete' && emp?.hasHistory)
    errors.employee = 'Bu işçinin davamiyyət tarixçəsi var — silinmir. İşdən çıxma üçün deaktivasiya müraciəti göndərin.'
  if (d.type === 'employee_activation' && emp && emp.active === d.makeActive)
    errors.employee = emp.active ? 'İşçi artıq aktivdir' : 'İşçi artıq deaktivdir'
  if (NEEDS_DATES.has(d.type)) {
    if (!d.dateFrom) errors.dates = 'Başlanğıc tarixini seçin'
    else if (d.type !== 'permission' && d.dateTo && d.dateTo < d.dateFrom) errors.dates = 'Bitmə tarixi başlanğıcdan əvvəl ola bilməz'
  }
  if (d.type === 'permission' && (d.timeFrom || d.timeTo)) {
    if (!d.timeFrom || !d.timeTo) errors.times = 'Saatlıq icazə üçün hər iki saatı yazın'
    else if (d.timeTo <= d.timeFrom) errors.times = 'Bitmə saatı başlanğıcdan sonra olmalıdır'
  }
  if (d.type === 'employee_create' && (!d.fields.name.trim() || !d.fields.position.trim()))
    errors.fields = 'Ad və vəzifə yazılmalıdır'
  if (d.type === 'employee_update' && emp && !changesFor(w, d).length) errors.fields = 'Heç bir sahə dəyişməyib'
  if (d.type === 'shift_change' && !d.shift.trim()) errors.shift = 'Yeni növbəni seçin'
  if (d.note.trim().length < 3) errors.note = 'Qeyd və ya səbəb yazın (ən azı 3 hərf)'
  return errors
}

/** The before → after rows a draft proposes. */
export function changesFor(w: World, d: Draft): Change[] {
  const emp = d.employeeId ? w.employees.find((e) => e.id === d.employeeId) : undefined
  const rows: Change[] = []
  const add = (field: string, label: string, before: string | null, after: string | null) => {
    if ((before ?? '') !== (after ?? '')) rows.push({ field, label, before, after })
  }
  switch (d.type) {
    case 'employee_create':
      add('name', 'Ad, soyad', null, d.fields.name.trim())
      add('position', 'Vəzifə', null, d.fields.position.trim())
      add('phone', 'Telefon', null, d.fields.phone.trim() || null)
      break
    case 'employee_update':
      if (emp) {
        add('name', 'Ad, soyad', emp.name, d.fields.name.trim() || emp.name)
        add('position', 'Vəzifə', emp.position, d.fields.position.trim() || emp.position)
        add('phone', 'Telefon', emp.phone, d.fields.phone.trim() || emp.phone)
      }
      break
    case 'employee_activation':
      if (emp) add('active', 'Status', emp.active ? 'Aktiv' : 'Deaktiv', d.makeActive ? 'Aktiv' : 'Deaktiv')
      break
    case 'employee_delete':
      if (emp) add('exists', 'Hesab', 'Var', 'Silinir')
      break
    case 'shift_change':
      if (emp) add('shift', 'Növbə', emp.shift, d.shift.trim())
      break
    default:
      break
  }
  return rows
}

// --- the state machine ------------------------------------------------------------------------------------

export type Action =
  | { type: 'create'; draft: Draft; by: Persona; id: string; at: string }
  | { type: 'approve'; id: string; by: Persona; at: string }
  | { type: 'reject'; id: string; by: Persona; at: string; reason: string }
  | { type: 'cancel'; id: string; by: Persona; at: string }

export type Outcome = { world: World; error?: string }

export function reduce(w: World, a: Action): Outcome {
  switch (a.type) {
    case 'create': {
      if (!canCreate(w, a.by, a.draft.branchId)) return { world: w, error: 'Bu filial üçün müraciət yaratmaq hüququnuz yoxdur' }
      const errors = validate(w, a.draft)
      if (Object.keys(errors).length) return { world: w, error: Object.values(errors)[0] }
      const d = a.draft
      const base: ChangeRequest = {
        id: a.id, type: d.type, status: 'pending', employeeId: d.type === 'employee_create' ? null : d.employeeId,
        branchId: d.branchId, createdById: a.by.id, createdAt: a.at,
        dateFrom: NEEDS_DATES.has(d.type) ? d.dateFrom : undefined,
        dateTo: NEEDS_DATES.has(d.type) ? d.dateTo || d.dateFrom : undefined,
        timeFrom: d.type === 'permission' ? d.timeFrom || undefined : undefined,
        timeTo: d.type === 'permission' ? d.timeTo || undefined : undefined,
        note: d.note.trim(), changes: changesFor(w, d), events: [{ at: a.at, actorId: a.by.id, kind: 'created', note: d.note.trim() }],
      }
      // An Operator acting inside their own structure: applied at once, on the record as a direct act.
      if (a.by.kind === 'operator') {
        const done: ChangeRequest = {
          ...base, status: 'approved', direct: true, decidedById: a.by.id, decidedAt: a.at,
          events: [...base.events, { at: a.at, actorId: a.by.id, kind: 'direct' }, { at: a.at, actorId: a.by.id, kind: 'applied' }],
        }
        return { world: apply({ ...w, requests: [done, ...w.requests] }, done) }
      }
      return { world: { ...w, requests: [base, ...w.requests] } }
    }
    case 'approve':
    case 'reject': {
      const r = w.requests.find((x) => x.id === a.id)
      if (!r) return { world: w, error: 'Müraciət tapılmadı' }
      if (r.status !== 'pending') return { world: w, error: `Bu müraciət artıq «${STATUS_LABEL[r.status].toLowerCase()}» — ikinci qərar verilmir` }
      if (r.createdById === a.by.id) return { world: w, error: 'Öz müraciətinizə qərar verə bilməzsiniz' }
      if (!canDecide(w, a.by, r)) return { world: w, error: 'Bu müraciət üzrə qərar vermək hüququnuz yoxdur' }
      if (a.type === 'reject') {
        const reason = a.reason.trim()
        if (reason.length < 3) return { world: w, error: 'Rədd səbəbini yazın (ən azı 3 hərf)' }
        const next: ChangeRequest = {
          ...r, status: 'rejected', decidedById: a.by.id, decidedAt: a.at, decisionNote: reason,
          events: [...r.events, { at: a.at, actorId: a.by.id, kind: 'rejected', note: reason }],
        }
        return { world: { ...w, requests: w.requests.map((x) => (x.id === r.id ? next : x)) } }
      }
      // Re-checked at decision time: the person may have gained history since the request was made.
      if (r.type === 'employee_delete') {
        const emp = w.employees.find((e) => e.id === r.employeeId)
        if (emp?.hasHistory) return { world: w, error: 'İşçinin tarixçəsi var — silinmə təsdiqlənmir. Deaktivasiyanı təklif edin.' }
      }
      const next: ChangeRequest = {
        ...r, status: 'approved', decidedById: a.by.id, decidedAt: a.at,
        events: [...r.events, { at: a.at, actorId: a.by.id, kind: 'approved' }, { at: a.at, actorId: a.by.id, kind: 'applied' }],
      }
      return { world: apply({ ...w, requests: w.requests.map((x) => (x.id === r.id ? next : x)) }, next) }
    }
    case 'cancel': {
      const r = w.requests.find((x) => x.id === a.id)
      if (!r) return { world: w, error: 'Müraciət tapılmadı' }
      if (!canCancel(a.by, r)) return { world: w, error: 'Yalnız özünüzün gözləyən müraciətini ləğv edə bilərsiniz' }
      const next: ChangeRequest = { ...r, status: 'cancelled', events: [...r.events, { at: a.at, actorId: a.by.id, kind: 'cancelled' }] }
      return { world: { ...w, requests: w.requests.map((x) => (x.id === r.id ? next : x)) } }
    }
  }
}

/** What an approved request does to the demo roster. Leaves, sickness and corrections touch days, not people. */
function apply(w: World, r: ChangeRequest): World {
  const get = (field: string) => r.changes.find((c) => c.field === field)?.after ?? null
  switch (r.type) {
    case 'employee_create':
      return {
        ...w,
        employees: [...w.employees, {
          id: `e-${r.id}`, name: get('name') ?? 'Yeni işçi', position: get('position') ?? '', branchId: r.branchId,
          phone: get('phone') ?? '', shift: '08:00–17:00', active: true, hasHistory: false,
        }],
      }
    case 'employee_update':
      return {
        ...w,
        employees: w.employees.map((e) => (e.id === r.employeeId
          ? { ...e, name: get('name') ?? e.name, position: get('position') ?? e.position, phone: get('phone') ?? e.phone }
          : e)),
      }
    case 'employee_activation':
      return { ...w, employees: w.employees.map((e) => (e.id === r.employeeId ? { ...e, active: get('active') === 'Aktiv' } : e)) }
    case 'employee_delete':
      return { ...w, employees: w.employees.filter((e) => e.id !== r.employeeId || e.hasHistory) }
    case 'shift_change':
      return { ...w, employees: w.employees.map((e) => (e.id === r.employeeId ? { ...e, shift: get('shift') ?? e.shift } : e)) }
    default:
      return w
  }
}
