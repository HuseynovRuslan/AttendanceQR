import { useEffect, useRef, useState, type FormEvent } from 'react'
import { RowActions } from '../../components/RowActions'
import { BulkInvitePage } from './BulkInvitePage'
import { initials } from './todayShift'
import { PositionSelect } from '../../components/PositionSelect'
import { NO_CYCLE, type WorkCycleValue } from '../../components/WorkCyclePicker'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { COMPANY_TZ, fmtFullDateTime, fromCompanyInputValue, toCompanyInputValue } from '../../lib/format'
import {
  bulkResetPin,
  bulkSchedule,
  bulkPermission,
  type BulkPermission,
  type BulkPinResult,
  deleteEmployee,
  getAdminLocations,
  getEmployee,
  getEmployees,
  getEmployeeSelection,
  getEmployeeStats,
  getSchedules,
  invite,
  reinviteEmployee,
  resetAllReferencePhotos,
  resetEmployeeAttendance,
  resetPin,
  resetReferencePhoto,
  updateEmployee,
  type AdminEmployeeRow,
  type AdminLocation,
  type EmployeeSelectionRow,
  type EmployeeStats,
  type InviteResult,
  type Schedule,
} from '../../api/admin'
import { getGroupCompanies } from '../../api/tenant'
import {
  adminClearCheckout,
  adminCreateRecord,
  adminUpdateRecord,
  getEmployeeAttendance,
  type AttendanceRecord,
} from '../../api/attendance'
import type { Role } from '../../lib/jwt'
import { useAuth } from '../../auth/AuthContext'
import { StatusBadge } from '../../components/StatusBadge'
import {
  IconAlert, IconCalendar, IconCheck, IconChevronDown, IconChevronLeft, IconChevronRight, IconColumns,
  IconKey, IconMapPin, IconPhone, IconRefresh, IconSearch, IconSend, IconTrash, IconUserX, IconUsers, IconX,
} from '../../components/icons'

const ATTENDANCE_ERRORS: Record<string, string> = {
  NothingToUpdate: 'Heç nə dəyişmədi',
  RecordNotFound: 'Qeyd tapılmadı',
  LocationNotFound: 'Filial tapılmadı',
  EmployeeNotFound: 'İşçi tapılmadı',
  CheckInInFuture: 'Giriş vaxtı gələcəkdə ola bilməz',
  CheckOutInFuture: 'Çıxış vaxtı gələcəkdə ola bilməz',
  CheckOutBeforeCheckIn: 'Çıxış girişdən əvvəl ola bilməz',
  DateInFuture: 'Tarix gələcəkdə ola bilməz',
  RecordAlreadyExists: 'Bu gün üçün artıq qeyd var',
}

function toLocalInputValue(iso: string | null): string {
  // Through the COMPANY's timezone, not the device's: an admin on a phone set to UTC+3 was shown a
  // check-out an hour early, and "correcting" a time that only looked wrong writes their device's
  // hour into somebody's attendance record.
  return iso ? toCompanyInputValue(iso) : ''
}

function fromLocalInputValue(local: string): string | undefined {
  if (!local) return undefined
  return fromCompanyInputValue(local)
}

const ROLE_LABEL: Record<Role, string> = { Employee: 'İşçi', Manager: 'Menecer', Admin: 'Admin' }

/** Names the common rotations the way a manager says them; anything else falls back to the numbers. */
function cycleLabel(days: number, onDays: number): string {
  if (days === 2 && onDays === 1) return 'Bir gündən bir'
  if (days === 3 && onDays === 1) return 'Sutka (1/2)'
  return `${onDays} iş / ${days - onDays} istirahət`
}

const ERRORS: Record<string, string> = {
  EmailAlreadyExists: 'Bu email artıq mövcuddur',
  PhoneAlreadyExists: 'Bu telefon nömrəsi artıq mövcuddur',
  NeedEmailOrPhone: 'Telefon nömrəsi və ya email lazımdır',
  LocationNotFound: 'Filial tapılmadı',
  EmployeeHasHistory: 'Bu işçinin davamiyyət tarixçəsi var — silmək olmaz, əvəzinə deaktiv edin',
  CannotDeleteSelf: 'Öz hesabınızı silə bilməzsiniz',
  CannotDeactivateSelf: 'Öz hesabınızı deaktiv edə bilməzsiniz — girişiniz bağlanardı',
  CannotChangeOwnRole: 'Öz rolunuzu dəyişə bilməzsiniz — panelə girişinizi itirə bilərsiniz',
  AlreadyActivated: 'İşçi artıq qeydiyyatdan keçib',
  EmployeeNotFound: 'İşçi tapılmadı',
  WorkCycleDaysInvalid: 'Növbə dövrü 2–28 gün aralığında olmalıdır',
  WorkCycleOnDaysInvalid: 'İş günlərinin sayı dövrədən az olmalıdır',
  WorkCycleAnchorRequired: 'Növbə üçün işlədiyi bir gün seçilməlidir',
  CannotManageOperator: 'Bu hesab platforma operatoruna aiddir — buradan idarə olunmur',
  // Dəstək sessiyası (operator müştərinin adminı kimi daxil olub) admin hesabının PIN-inə və ya
  // telefon/email-inə toxuna bilməz — bu, borc alınmış hesabın açarını dəyişmək olardı.
  // One code, two situations, and the old wording described only one of them: an operator who set the
  // role to "Admin" was told something about PINs and phone numbers, which is not what they had done.
  // Menecer is deliberately NOT restricted — appointing branch managers is the setup work.
  NotDuringImpersonation:
    'Dəstək sessiyasında admin TƏYİN etmək və admin hesabının PIN-ini / nömrəsini dəyişmək olmaz. '
    + 'Menecer təyin etmək olar. Admin lazımdırsa: SuperAdmin konsolunda ⋯ → «Admini təyin et».',
}

type FormState = {
  firstName: string
  lastName: string
  fatherName: string
  position: string
  /** «Sənəd üzrə» — the employer and site the paperwork names, when they are not where the person
   *  actually works. Blank on almost everybody; nothing is computed from them. */
  paperEmployer: string
  paperSite: string
  // Year kept only to preserve it for rows that were entered year-only (bulk import); the form edits
  // the full date below. birthDate is "yyyy-MM-dd" (what <input type="date"> emits), blank if unset.
  birthYear: string
  birthDate: string
  email: string
  phoneNumber: string
  locationId: string
  role: Role
  isActive: boolean
  workStart: string
  workEnd: string
  /** Fixed monthly salary in AZN for the payroll report; blank = not set. Kept as a string while typing. */
  monthlySalary: string
  photoExempt: boolean
  /** Whether the employee may use field/mobile check-in ("Səyyar / Sahə ziyarəti"). */
  canFieldCheckIn: boolean
  // '' = follow the branch. Tri-state, kept as a string because that is what a <select> speaks.
  qrlessOverride: '' | 'on' | 'off'
  fenceOverride: '' | 'on' | 'off'
  /** Whether this account may be carried on somebody else's phone — a brigade's shared handset, for
   *  a worker who has none of their own. Off by default: it gives up one-phone-one-employee. */
  canShareDevice: boolean
  /** Manager only: the branches they may SEE in reports. Separate from locationId, which is where
   *  they clock in. Empty on a manager means an empty panel. */
  managedLocationIds: string[]
  /** Rotation ("növbə"); NO_CYCLE = the branch's weekly calendar applies. Ignored when scheduleId
   *  is set — the shift carries its own. */
  cycle: WorkCycleValue
  /** The named shift this employee is on; '' = none. */
  scheduleId: string
}

const EMPTY: FormState = {
  firstName: '',
  lastName: '',
  fatherName: '',
  position: '',
  paperEmployer: '',
  paperSite: '',
  birthYear: '',
  birthDate: '',
  email: '',
  phoneNumber: '',
  locationId: '',
  role: 'Employee',
  isActive: true,
  workStart: '',
  workEnd: '',
  monthlySalary: '',
  photoExempt: false,
  canFieldCheckIn: false,
  qrlessOverride: '',
  fenceOverride: '',
  canShareDevice: false,
  managedLocationIds: [],
  cycle: NO_CYCLE,
  scheduleId: '',
}

/** The form edits Ad + Soyad separately. Prefer the stored parts; for a row not yet backfilled, fall
 *  back to splitting FullName (last token = surname) so the two fields aren't empty on first edit. */
/**
 * What the «Status» select can be narrowed to. Every value is a question somebody actually asks
 * while chasing a rollout — «who has not started», «who does not get announcements», «whose phone is
 * not bound» — rather than a tidy enumeration of the stored flags.
 */
type StatusSel = '' | 'activated' | 'pending' | 'notstarted' | 'nopush' | 'nodevice'

const STATUS_OPTIONS: { value: StatusSel; label: string }[] = [
  { value: '', label: 'Hamısı' },
  { value: 'activated', label: 'Aktivləşdirilib' },
  { value: 'pending', label: 'Dəvət gözləyir' },
  { value: 'notstarted', label: 'Tətbiqi açmayıb' },
  { value: 'nopush', label: 'Bildiriş bağlı' },
  { value: 'nodevice', label: 'Cihaz bağlanmayıb' },
]

/** Which optional columns the list shows. Remembered per browser. */
type EmpCols = { position: boolean; location: boolean; role: boolean; device: boolean; push: boolean; lastActive: boolean; status: boolean }
const EMP_COLS_KEY = 'qrlog.employees.cols'
const EMP_COLS_DEFAULT: EmpCols = {
  position: true, location: true, role: true, device: true, push: true, lastActive: true, status: true,
}

function readEmpCols(): EmpCols {
  // Storage can be absent or throw outright (private window, blocked site data); a remembered column
  // preference is never worth a page that fails to render.
  try {
    const raw = localStorage.getItem(EMP_COLS_KEY)
    return raw ? { ...EMP_COLS_DEFAULT, ...JSON.parse(raw) as Partial<EmpCols> } : EMP_COLS_DEFAULT
  } catch {
    return EMP_COLS_DEFAULT
  }
}

/** How many rows one page of the list holds — and how many the SERVER is asked for. */
const PAGE_SIZE = 25

/** Until the first /stats answers. Shown as zeros rather than as an empty card. */
const ZERO_STATS: EmployeeStats = {
  total: 0, activated: 0, notStarted: 0, noPush: 0, noDevice: 0, leftCount: 0,
}

/**
 * A list row, reduced to what a bulk action decides from.
 *
 * The selection has to survive paging — tick four people on page 1, three on page 2, press «icazə
 * ver» — so it cannot be «the ticked rows of the list currently loaded». It holds these instead, and
 * /employees/selection returns the identical shape for «Hamısını seç».
 */
function selRow(e: AdminEmployeeRow): EmployeeSelectionRow {
  return {
    id: e.id,
    fullName: e.fullName,
    locationName: e.locationName,
    isActive: e.isActive,
    activated: e.activated,
    mustChangePin: e.mustChangePin,
    canShareDevice: e.canShareDevice === true,
    canFieldCheckIn: e.canFieldCheckIn === true,
  }
}

function splitName(first: string | null | undefined, last: string | null | undefined, full: string): { first: string; last: string } {
  if (first || last) return { first: first ?? '', last: last ?? '' }
  const toks = (full ?? '').trim().split(/\s+/).filter(Boolean)
  if (toks.length <= 1) return { first: toks[0] ?? '', last: '' }
  return { first: toks.slice(0, -1).join(' '), last: toks[toks.length - 1] }
}

export function EmployeesPage() {
  // ONE page of the roster — twenty-five rows, narrowed and ordered in SQL. This used to be every
  // employee in the company, with every device binding each of them had ever had.
  const [rows, setRows] = useState<AdminEmployeeRow[]>([])
  /** How many people the current filters match in total, for the pager and «Hamısını seç». */
  const [total, setTotal] = useState(0)
  /** The headline counts, which a page of twenty-five cannot compute. See getEmployeeStats. */
  const [stats, setStats] = useState<EmployeeStats>(ZERO_STATS)
  const [listing, setListing] = useState(true)
  const [locations, setLocations] = useState<AdminLocation[]>([])
  const [schedules, setSchedules] = useState<Schedule[]>([])
  /** The other companies in this owner's group — the «Sənəd üzrə şirkət» picker. Empty for a tenant
   *  that has none configured, and the field then falls back to a plain text box rather than an
   *  empty dropdown nobody can get past. */
  const [groupCompanies, setGroupCompanies] = useState<string[]>([])
  /** The shift the bulk strip will apply; 'none' clears instead. */
  const [bulkShift, setBulkShift] = useState('')
  const navigate = useNavigate()
  const [page, setPage] = useState(1)
  const [filterLoc, setFilterLocRaw] = useState<string | null>(null)
  // Leavers stay (their days are in the tabel and pay) but off the everyday list — «deaktiv etdim, adlar
  // yenə qalır». One button shows them, to bring somebody back or delete a never-used account.
  const [showLeft, setShowLeft] = useState(false)
  // "Bildirişsiz" — show only the people a reminder/announcement can NOT reach, so a manager can go
  // help them switch it on. A workforce that won't self-serve is what keeps reach stuck, and a name
  // list per branch is what actually converts.
  /**
   * The one status filter, replacing two independent booleans.
   *
   * «Bildirişsiz» and «hələ başlamayıb» used to be separate toggles on two separate strips, and both
   * could be on at once — an intersection nobody ever wants, which left the list empty and looking
   * broken. One select, one answer.
   */
  const [statusSel, setStatusSelRaw] = useState<StatusSel>('')
  const [roleFilter, setRoleFilterRaw] = useState<string>('')
  /**
   * The ticked people, by id, each carrying the flags a bulk action reads.
   *
   * A Map rather than a Set of ids because the list is paged now: the rows a selection refers to are
   * often not the rows on screen, so the data has to travel with the tick.
   */
  const [selected, setSelected] = useState<Map<string, EmployeeSelectionRow>>(() => new Map())
  const [selectingAll, setSelectingAll] = useState(false)
  const [empColsOpen, setEmpColsOpen] = useState(false)
  const [moreOpen, setMoreOpen] = useState(false)
  const [empCols, setEmpCols] = useState<EmpCols>(readEmpCols)
  const searchRef = useRef<HTMLInputElement>(null)
  const [search, setSearch] = useState('')
  /** What the server is actually asked for — the box, debounced, so typing is not a request a letter. */
  const [searchQ, setSearchQ] = useState('')
  const [showForm, setShowForm] = useState(false)
  // Adding can be one-at-a-time or in bulk — both live under the single "İşçi əlavə et" button now
  // (Toplu əlavə was removed from the sidebar). Editing always uses the single form.
  const [addMode, setAddMode] = useState<'single' | 'bulk'>('single')
  const [editingId, setEditingId] = useState<string | null>(null)
  const [form, setForm] = useState<FormState>(EMPTY)
  const [error, setError] = useState<string | null>(null)
  // Editing your own row: the two fields that can lock you out are read-only there.
  const { employeeId: myId } = useAuth()
  const isSelf = editingId !== null && editingId === myId
  const [ok, setOk] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  /** Reading one employee for the edit form — the «Redaktə» button waits on this. */
  const [formBusy, setFormBusy] = useState(false)
  const [deletingId, setDeletingId] = useState<string | null>(null)
  const [linkBusyId, setLinkBusyId] = useState<string | null>(null)
  const [resettingId, setResettingId] = useState<string | null>(null)
  const [refBusy, setRefBusy] = useState(false)
  const [link, setLink] = useState<{ name: string; result: InviteResult } | null>(null)
  const [copied, setCopied] = useState(false)
  const [pinReset, setPinReset] = useState<{ name: string; pin: string } | null>(null)

  // Attendance-correction panel (view + fix one employee's raw records).
  const [attendanceEmployee, setAttendanceEmployee] = useState<AdminEmployeeRow | null>(null)
  const [attendanceRecords, setAttendanceRecords] = useState<AttendanceRecord[]>([])
  const [attendanceLoading, setAttendanceLoading] = useState(false)
  const [attendanceError, setAttendanceError] = useState<string | null>(null)
  const [editingRecordId, setEditingRecordId] = useState<string | null>(null)
  const [editCheckIn, setEditCheckIn] = useState('')
  const [editCheckOut, setEditCheckOut] = useState('')
  const [showCreateRecord, setShowCreateRecord] = useState(false)
  const [createDate, setCreateDate] = useState('')
  const [createCheckIn, setCreateCheckIn] = useState('')
  const [createCheckOut, setCreateCheckOut] = useState('')
  const [savingRecord, setSavingRecord] = useState(false)

  /** Everything the current filters mean, in the shape the three endpoints take. */
  const query = {
    page, pageSize: PAGE_SIZE,
    search: searchQ, status: statusSel, locationId: filterLoc, role: roleFilter, showLeft,
  }
  // Responses can come back out of order — a slow «Ə» answering after the «Əl» that replaced it would
  // put the wrong page on screen. Only the newest request is allowed to write state.
  const reqSeq = useRef(0)

  /**
   * One page of the list, plus the counts above it.
   *
   * Also what every mutation on this screen calls when it is done, which is why it re-reads BOTH:
   * deactivating somebody moves them out of the list and out of the headline numbers at once.
   */
  async function refresh() {
    const seq = ++reqSeq.current
    setListing(true)
    const [emp, st] = await Promise.all([getEmployees(query), getEmployeeStats(filterLoc)])
    if (seq !== reqSeq.current) return
    setListing(false)
    if (emp.status === 200 && emp.data && 'items' in emp.data) {
      setRows(emp.data.items)
      setTotal(emp.data.total)
    }
    if (st.status === 200 && st.data) setStats(st.data)
  }

  // Branches, shifts and the group's companies do not depend on the filters and are read once.
  useEffect(() => {
    void (async () => {
      const [locs, scheds, group] = await Promise.all([
        getAdminLocations(), getSchedules(), getGroupCompanies(),
      ])
      if (locs.status === 200 && Array.isArray(locs.data)) setLocations(locs.data)
      if (scheds.status === 200 && Array.isArray(scheds.data)) setSchedules(scheds.data)
      if (group.status === 200 && Array.isArray(group.data)) setGroupCompanies(group.data)
    })()
  }, [])

  // The box is debounced before it becomes a query: a name typed at speed is one request, not nine.
  useEffect(() => {
    const t = setTimeout(() => {
      setSearchQ((prev) => {
        const next = search.trim()
        if (next !== prev) setPage(1)
        return next
      })
    }, 250)
    return () => clearTimeout(t)
  }, [search])

  // The list is re-read whenever what it is asked for changes, and nowhere else.
  useEffect(() => {
    void refresh()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [page, searchQ, statusSel, filterLoc, roleFilter, showLeft])

  /**
   * Any filter change also sends the reader back to page 1.
   *
   * In the setter rather than in an effect on purpose: an effect would let one request go out for
   * page 7 of a result that no longer has seven pages, and then a second for page 1.
   */
  function setFilterLoc(v: string | null) { setFilterLocRaw(v); setPage(1) }
  function setStatusSel(v: StatusSel | ((p: StatusSel) => StatusSel)) { setStatusSelRaw(v); setPage(1) }
  function setRoleFilter(v: string) { setRoleFilterRaw(v); setPage(1) }

  // Opened from an employee's profile ("Redaktə et" → /admin/employees?edit=<id>): jump straight into
  // that employee's edit form once the list has loaded, then drop the query param.
  const [searchParams, setSearchParams] = useSearchParams()
  // It used to wait for the whole roster to arrive and then look the employee up in it, so opening
  // «Redaktə et» on somebody who would have landed on page 7 only worked because page 7 was loaded
  // too. Now it reads that one employee.
  useEffect(() => {
    const eid = searchParams.get('edit')
    if (!eid) return
    setSearchParams({}, { replace: true })
    void startEdit(eid)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [searchParams])

  // ⌘K focuses the search box; ⌘A selects every row the filters have left on screen.
  //
  // ⌘A is the escape hatch the bulk actions depend on: «grant this to the whole branch» has to stay
  // one press, or the permission gets granted carelessly instead. It is deliberately ignored while
  // the caret is in a text field, where ⌘A means «select this text» and always will.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (!(e.metaKey || e.ctrlKey)) return
      const k = e.key.toLowerCase()
      if (k === 'k') {
        e.preventDefault()
        searchRef.current?.focus()
        searchRef.current?.select()
        return
      }
      if (k !== 'a') return
      const el = document.activeElement
      const typing = el instanceof HTMLInputElement || el instanceof HTMLTextAreaElement
        || (el instanceof HTMLElement && el.isContentEditable)
      if (typing) return
      e.preventDefault()
      void selectAll()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [searchQ, statusSel, filterLoc, roleFilter, showLeft])

  /**
   * Tick EVERY person the filters match — all of them, not the twenty-five on screen.
   *
   * This is the one thing server-side paging could quietly have broken. «Hamısını seç» and ⌘A are
   * what make «grant this to the whole branch» one press, and a branch is routinely forty people; if
   * the phrase had come to mean «this page», a permission meant for a brigade would have reached the
   * first page of it and nobody would have noticed. The server returns the whole matching set — ids
   * and the handful of flags a bulk action reads, nothing else.
   */
  async function selectAll() {
    setSelectingAll(true)
    const { status, data } = await getEmployeeSelection({
      search: searchQ, status: statusSel, locationId: filterLoc, role: roleFilter, showLeft,
    })
    setSelectingAll(false)
    if (status !== 200 || !Array.isArray(data)) {
      setError('Seçim alınmadı')
      return
    }
    setSelected(new Map(data.map((r) => [r.id, r])))
  }

  function set<K extends keyof FormState>(key: K, value: FormState[K]) {
    setForm((f) => ({ ...f, [key]: value }))
  }

  function startAdd() {
    setEditingId(null)
    setAddMode('single')
    setForm({ ...EMPTY, locationId: locations[0]?.id ?? '' })
    setError(null)
    setOk(null)
    setLink(null)
    setShowForm(true)
  }

  /**
   * Open the edit form on one employee, read FROM THE DETAIL ENDPOINT.
   *
   * Never from the list row. `EmployeeUpdateRequest` null-defaults every field it is not handed, so
   * saving a form filled from a list row would blank whatever the list does not carry — the email,
   * the salary, the per-person geofence and QR-less overrides, the rotation anchor. The list stopped
   * carrying those on purpose; this is the other half of that change.
   */
  async function startEdit(id: string) {
    setFormBusy(true)
    const { status, data } = await getEmployee(id)
    setFormBusy(false)
    if (status !== 200 || !data || 'error' in data) {
      setError('İşçi məlumatı gəlmədi')
      return
    }
    const e = data
    setEditingId(e.id)
    const parts = splitName(e.firstName, e.lastName, e.fullName)
    setForm({
      firstName: parts.first,
      lastName: parts.last,
      fatherName: e.fatherName ?? '',
      position: e.position ?? '',
      paperEmployer: e.paperEmployer ?? '',
      paperSite: e.paperSite ?? '',
      birthYear: e.birthYear != null ? String(e.birthYear) : '',
      birthDate: e.birthDate ?? '',
      email: e.email ?? '',
      phoneNumber: e.phoneNumber ?? '',
      locationId: e.locationId,
      role: e.role,
      isActive: e.isActive,
      workStart: e.workStart ?? '',
      workEnd: e.workEnd ?? '',
      monthlySalary: e.monthlySalary != null ? String(e.monthlySalary) : '',
      photoExempt: e.photoExempt === true,
      canFieldCheckIn: e.canFieldCheckIn === true,
      qrlessOverride: e.qrlessCheckInOverride == null ? '' : e.qrlessCheckInOverride ? 'on' : 'off',
      fenceOverride: e.requireGeofenceOverride == null ? '' : e.requireGeofenceOverride ? 'off' : 'on',
      canShareDevice: e.canShareDevice === true,
      managedLocationIds: e.managedLocationIds ?? [],
      cycle: e.workCycleDays
        ? { days: e.workCycleDays, onDays: e.workCycleOnDays ?? 1, anchor: e.workCycleAnchor ?? '' }
        : NO_CYCLE,
      scheduleId: e.scheduleId ?? '',
    })
    setError(null)
    setOk(null)
    setLink(null)
    setShowForm(true)
  }

  function closeForm() {
    setShowForm(false)
    setEditingId(null)
    setError(null)
  }

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    setOk(null)
    if (!form.phoneNumber.trim() && !form.email.trim()) {
      setError('Telefon nömrəsi və ya email lazımdır')
      return
    }
    setSaving(true)
    const first = form.firstName.trim()
    const last = form.lastName.trim()
    const payload = {
      // FullName stays the canonical display name (the backend also composes it from the parts).
      fullName: `${first} ${last}`.trim(),
      firstName: first || null,
      lastName: last || null,
      email: form.email.trim() || null,
      phoneNumber: form.phoneNumber.trim() || null,
      locationId: form.locationId,
      role: form.role,
      fatherName: form.fatherName.trim() || null,
      position: form.position.trim() || null,
      paperEmployer: form.paperEmployer.trim() || null,
      paperSite: form.paperSite.trim() || null,
      birthYear: form.birthYear ? Number(form.birthYear) : null,
      birthDate: form.birthDate || null,
      monthlySalary: form.monthlySalary.trim() ? Number(form.monthlySalary) : null,
      photoExempt: form.photoExempt,
      canFieldCheckIn: form.canFieldCheckIn,
      canShareDevice: form.canShareDevice,
      // '' means "follow the branch" and must reach the server as null, not as false — false is a
      // pinned exception that would override a branch which already has the setting on.
      qrlessCheckInOverride: form.qrlessOverride === '' ? null : form.qrlessOverride === 'on',
      // Inverted on purpose: the admin picks «GPS divarı», and the field stores "is it required".
      requireGeofenceOverride: form.fenceOverride === '' ? null : form.fenceOverride === 'off',
      // Sent on create too now, so a schedule (day/night shift) assigned at creation is persisted.
      workStart: form.workStart || null,
      workEnd: form.workEnd || null,
      // Rotation. Sent on BOTH paths and always — the server null-defaults every field it isn't
      // given, so omitting these on an unrelated edit would silently drop someone's rotation and
      // start marking their rest days absent.
      // Always sent, so clearing a shift actually clears it — the server null-defaults it otherwise.
      scheduleId: form.scheduleId || null,
      // The server ignores these while a shift is assigned; sending them keeps whatever the employee
      // had, so unassigning the shift restores their own hours rather than blanking them.
      workCycleDays: form.cycle.days,
      workCycleOnDays: form.cycle.days ? form.cycle.onDays : null,
      workCycleAnchor: form.cycle.days ? form.cycle.anchor || null : null,
      activateWithPin,
    }
    const res = editingId
      ? await updateEmployee(editingId, {
          ...payload,
          isActive: form.isActive,
          workStart: form.workStart || null,
          workEnd: form.workEnd || null,
          // Always sent, so unticking the last branch actually clears it. The server ignores this
          // for non-managers and clears any stale rows itself.
          managedLocationIds: form.managedLocationIds,
        })
      : await invite(payload)
    setSaving(false)

    if (res.status === 200 && res.data && !('error' in res.data)) {
      await refresh()
      if (editingId) {
        setOk('İşçi yeniləndi')
        closeForm()
      } else {
        // Freshly invited — surface the activation link to share by hand.
        setLink({ name: payload.fullName, result: res.data as InviteResult })
        setOk(null)
        setShowForm(false)
      }
    } else if (res.data && 'error' in res.data) {
      setError(ERRORS[res.data.error] ?? 'Yadda saxlanmadı')
    } else {
      setError('Yadda saxlanmadı')
    }
  }

  /** Drop ids the list no longer has — a selection outlives the rows it was made from now. */
  function deselect(ids: Iterable<string>) {
    setSelected((prev) => {
      const next = new Map(prev)
      for (const id of ids) next.delete(id)
      return next
    })
  }

  async function onDelete(e: AdminEmployeeRow) {
    if (!window.confirm(`"${e.fullName}" işçisi silinsin?`)) return
    setError(null)
    setOk(null)
    setDeletingId(e.id)
    const { status, data } = await deleteEmployee(e.id)

    // Blocked because this employee has attendance/device-change history — offer to wipe that
    // history too (common for test accounts) instead of silently failing.
    if (status === 409 && data && typeof data === 'object' && 'error' in data && data.error === 'EmployeeHasHistory') {
      const wipe = window.confirm(
        `"${e.fullName}" işçisinin davamiyyət tarixçəsi var. Tarixçə daxil olmaqla TAM silinsin? Bu geri qaytarılmır.`,
      )
      if (wipe) {
        const forced = await deleteEmployee(e.id, true)
        setDeletingId(null)
        if (forced.status === 200) {
          deselect([e.id])
          await refresh()
        } else {
          setError('Silinmədi')
        }
        return
      }
    }

    setDeletingId(null)
    if (status === 200) {
      deselect([e.id])
      await refresh()
    } else if (data && typeof data === 'object' && 'error' in data) {
      setError(ERRORS[(data as { error: string }).error] ?? 'Silinmədi')
    } else {
      setError('Silinmədi')
    }
  }

  async function onResetAttendance(e: AdminEmployeeRow) {
    if (
      !window.confirm(
        `"${e.fullName}" üçün BÜTÜN giriş/çıxış tarixçəsi silinsin? Hesab və cihaz bağlantısı qalır — yenidən skan testi edə bilərsiniz.`,
      )
    )
      return
    setError(null)
    setOk(null)
    setResettingId(e.id)
    const { status, data } = await resetEmployeeAttendance(e.id)
    setResettingId(null)
    if (status === 200 && data && 'attendanceRecordsDeleted' in data) {
      setOk(`Tarixçə sıfırlandı (${data.attendanceRecordsDeleted} qeyd silindi) — yenidən test edə bilərsiniz.`)
      await refresh()
    } else {
      setError('Sıfırlanmadı')
    }
  }

  async function onReinvite(e: AdminEmployeeRow) {
    setError(null)
    setOk(null)
    setLinkBusyId(e.id)
    const { status, data } = await reinviteEmployee(e.id)
    setLinkBusyId(null)
    if (status === 200 && data && 'activationToken' in data) {
      setLink({ name: e.fullName, result: data })
      await refresh()
    } else if (data && 'error' in data) {
      setError(ERRORS[data.error] ?? 'Link yaradılmadı')
    }
  }

  async function onResetPin(e: AdminEmployeeRow) {
    if (!window.confirm(`"${e.fullName}" üçün PIN sıfırlansın? Yeni müvəqqəti PIN veriləcək — işçi girib öz PIN-ini dəyişməlidir.`)) return
    setError(null)
    setOk(null)
    const { status, data } = await resetPin(e.id)
    if (status === 200 && data && 'tempPin' in data) {
      setPinReset({ name: e.fullName, pin: data.tempPin })
    } else if (data && 'error' in data && data.error === 'NotActivated') {
      setError('Bu işçi hələ aktivləşməyib — «Qeyd. linki» göndərin.')
    } else {
      const code = data && 'error' in data ? (data as { error: string }).error : ''
      setError(ERRORS[code] ?? 'PIN sıfırlanmadı')
    }
  }

  async function onResetReference(e: AdminEmployeeRow) {
    if (!window.confirm(`"${e.fullName}" üçün referans şəkli sıfırlansın? İşçi növbəti dəfə öz telefonu ilə giriş edəndə yeni referans avtomatik yaranacaq.`)) return
    setRefBusy(true)
    setError(null)
    const { status } = await resetReferencePhoto(e.id)
    setRefBusy(false)
    if (status === 200) setOk(`"${e.fullName}" üçün referans sıfırlandı — növbəti girişdə yenilənəcək.`)
    else setError('Referans sıfırlanmadı')
  }

  async function onResetAllReferences() {
    if (!window.confirm('BÜTÜN işçilərin referans şəkli sıfırlansın? Hər kəs növbəti dəfə öz telefonu ilə giriş edəndə referans avtomatik düzgün üzlə yenilənəcək.')) return
    setRefBusy(true)
    setError(null)
    const { status, data } = await resetAllReferencePhotos()
    setRefBusy(false)
    if (status === 200 && data && 'reset' in data)
      setOk(`${data.reset} işçinin referansı sıfırlandı — hərə növbəti girişdə yenilənəcək.`)
    else setError('Referanslar sıfırlanmadı')
  }

  async function openAttendance(e: AdminEmployeeRow) {
    setAttendanceEmployee(e)
    setAttendanceError(null)
    setEditingRecordId(null)
    setShowCreateRecord(false)
    await refreshAttendance(e.id)
  }

  async function refreshAttendance(employeeId: string) {
    setAttendanceLoading(true)
    const { status, data } = await getEmployeeAttendance(employeeId)
    setAttendanceLoading(false)
    if (status === 200 && Array.isArray(data)) {
      setAttendanceRecords(data)
    } else {
      setAttendanceError('Tarixçə yüklənmədi')
    }
  }

  function closeAttendance() {
    setAttendanceEmployee(null)
    setAttendanceRecords([])
    setEditingRecordId(null)
    setShowCreateRecord(false)
  }

  function startEditRecord(r: AttendanceRecord) {
    setEditingRecordId(r.recordId)
    setEditCheckIn(toLocalInputValue(r.checkInAtUtc))
    setEditCheckOut(toLocalInputValue(r.checkOutAtUtc))
    setAttendanceError(null)
  }

  async function saveEditRecord() {
    if (!editingRecordId || !attendanceEmployee) return
    setSavingRecord(true)
    setAttendanceError(null)
    const { status, data } = await adminUpdateRecord(
      editingRecordId,
      fromLocalInputValue(editCheckIn),
      fromLocalInputValue(editCheckOut),
    )
    setSavingRecord(false)
    if (status === 200) {
      setEditingRecordId(null)
      await refreshAttendance(attendanceEmployee.id)
    } else if (data && typeof data === 'object' && 'error' in data) {
      setAttendanceError(ATTENDANCE_ERRORS[(data as { error: string }).error] ?? 'Yadda saxlanmadı')
    } else {
      setAttendanceError('Yadda saxlanmadı')
    }
  }

  async function onClearCheckOut(r: AttendanceRecord) {
    if (!attendanceEmployee) return
    if (!window.confirm('Bu qeydin çıxışı ləğv edilsin? İşçi yenidən "işdədir" olacaq və sonra düzgün çıxış edə biləcək.')) return
    setSavingRecord(true)
    setAttendanceError(null)
    const { status, data } = await adminClearCheckout(r.recordId)
    setSavingRecord(false)
    if (status === 200) {
      await refreshAttendance(attendanceEmployee.id)
    } else if (data && typeof data === 'object' && 'error' in data) {
      setAttendanceError(ATTENDANCE_ERRORS[(data as { error: string }).error] ?? 'Əməliyyat alınmadı')
    } else {
      setAttendanceError('Əməliyyat alınmadı')
    }
  }

  async function submitCreateRecord() {
    if (!attendanceEmployee || !createDate || !createCheckIn) return
    setSavingRecord(true)
    setAttendanceError(null)
    const checkIn = fromLocalInputValue(createCheckIn)!
    const { status, data } = await adminCreateRecord(attendanceEmployee.id, createDate, checkIn, fromLocalInputValue(createCheckOut))
    setSavingRecord(false)
    if (status === 200) {
      setShowCreateRecord(false)
      setCreateDate('')
      setCreateCheckIn('')
      setCreateCheckOut('')
      await refreshAttendance(attendanceEmployee.id)
    } else if (data && typeof data === 'object' && 'error' in data) {
      setAttendanceError(ATTENDANCE_ERRORS[(data as { error: string }).error] ?? 'Yadda saxlanmadı')
    } else {
      setAttendanceError('Yadda saxlanmadı')
    }
  }

  const activationLink = link?.result.activationToken
    ? `${window.location.origin}/activate?token=${link.result.activationToken}`
    : ''


  // The reissued list, held on screen until it is dismissed on purpose. Deliberately NOT cleared by
  // a refresh of the roster underneath it: losing this list to an accidental reload is the whole
  // reason this exists.
  // How the new person gets their first credential: a link to tap, or four digits to be told. Both
  // existed for bulk already; adding one person could only ever produce a link.
  const [activateWithPin, setActivateWithPin] = useState(true)

  const [sharing, setSharing] = useState(false)

  /**
   * Grant or withdraw a capability across the VISIBLE list — whatever the branch filter and search
   * have narrowed it to. Acting on the filtered set is this page's idiom, and it is the shape the
   * work actually has: a whole brigade at a branch, not people picked one at a time.
   *
   * Withdrawal sits beside it deliberately. A permission that can only be switched on is a ratchet,
   * and the day a brigade stops sharing a phone there has to be a way back no harder than the way in.
   */
  /**
   * Put everyone currently on screen onto one shift.
   *
   * Deliberately "the visible ones" rather than a checkbox selection, exactly like the permission
   * strip beside it: the filters above ARE the selection, and it is the branch filter that makes this
   * safe to press — a crew is what a branch filter leaves on screen.
   */
  async function applyShift(targets: EmployeeSelectionRow[]) {
    if (!bulkShift || targets.length === 0) return
    const clearing = bulkShift === 'none'
    const shift = schedules.find((s) => s.id === bulkShift)
    if (!clearing && !shift) return

    if (!window.confirm(
      clearing
        ? `${targets.length} nəfərin növbəsi ləğv olunsun?

` +
          'Onlar öz saatlarına, o da yoxdursa filialın saatına qayıdacaq.'
        : `${targets.length} nəfər «${shift!.name}» növbəsinə keçirilsin?

` +
          `Saat: ${shift!.shiftStart}–${shift!.shiftEnd}. Növbənin saatı sonradan dəyişsə, ` +
          'KEÇMİŞ günlər də yenidən hesablanır — hesabatlar növbədən oxunur.',
    )) return

    setSharing(true)
    const { status, data } = await bulkSchedule(targets.map((t) => t.id), clearing ? null : bulkShift)
    setSharing(false)
    if (status === 200 && data && !('error' in data)) {
      await refresh()
      setBulkShift('')
      setOk(
        `${data.changed} nəfər dəyişdi` +
        (data.skipped > 0 ? ` · ${data.skipped} nəfər buraxıldı (başqa filialın növbəsi)` : ''),
      )
    } else {
      setError('Dəyişmədi')
    }
  }

  async function setPermission(targets: EmployeeSelectionRow[], permission: BulkPermission, allowed: boolean) {
    const has = (t: EmployeeSelectionRow) =>
      (permission === 'ShareDevice' ? t.canShareDevice : t.canFieldCheckIn) === true
    const affected = targets.filter((t) => has(t) !== allowed)
    if (affected.length === 0) return

    const what = permission === 'ShareDevice' ? 'ORTAQ TELEFON' : 'SAHƏ ZİYARƏTİ'
    const why = permission === 'ShareDevice'
      ? 'Onların hesabı briqadanın ortaq telefonunda saxlanıla biləcək. Bu, həmin işçilər üçün ' +
        '«bir telefon, bir işçi» qorumasını ləğv edir — telefonu olmayanlar üçün nəzərdə tutulub.'
      : 'Onlar QR plakatı olmayan obyektlərdə GPS + selfi ilə davamiyyət qeyd edə biləcək.'
    const back = permission === 'ShareDevice'
      ? 'Artıq bağlanmış telefonlar işləməyə davam edir — onları ayırmaq üçün Cihazlar səhifəsindəki ' +
        '«Ortaq telefonlar» bölməsindən istifadə edin.'
      : 'Başlanmış ziyarətlər olduğu kimi qalır.'

    if (!window.confirm(
      allowed
        ? `${affected.length} nəfərə ${what} icazəsi verilsin?

${why}`
        : `${affected.length} nəfərdən ${what} icazəsi alınsın?

${back}`,
    )) return

    setSharing(true)
    const { status } = await bulkPermission(affected.map((t) => t.id), permission, allowed)
    setSharing(false)
    if (status === 200) {
      const changed = new Set(affected.map((t) => t.id))
      setSelected((prev) => new Map([...prev].map(([id, r]) => [
        id,
        changed.has(id)
          ? permission === 'ShareDevice' ? { ...r, canShareDevice: allowed } : { ...r, canFieldCheckIn: allowed }
          : r,
      ])))
      await refresh()
      setOk(allowed ? `${affected.length} nəfərə icazə verildi` : `${affected.length} nəfərdən icazə alındı`)
    } else {
      setError('Dəyişmədi')
    }
  }

  const [pinList, setPinList] = useState<BulkPinResult | null>(null)
  const [issuing, setIssuing] = useState(false)
  const [pinCopied, setPinCopied] = useState(false)

  async function issuePins(targets: EmployeeSelectionRow[]) {
    if (targets.length === 0) return
    const names = targets.length === 1 ? `"${targets[0].fullName}"` : `${targets.length} nəfər`
    if (!window.confirm(
      `${names} üçün YENİ müvəqqəti PIN veriləcək.\n\n` +
      'Diqqət: əvvəl paylanmış PIN-lər işləməyəcək. Köhnə PIN-ləri geri qaytarmaq mümkün deyil — ' +
      'onlar saxlanmır.\n\nDavam edilsin?',
    )) return

    setIssuing(true)
    const { status, data } = await bulkResetPin(targets.map((t) => t.id))
    setIssuing(false)
    if (status === 200 && data && !('error' in data)) {
      setPinList(data as BulkPinResult)
      await refresh()
    } else {
      window.alert('PIN verilmədi')
    }
  }

  /**
   * Who a bulk action applies to — the ticked people, and only those.
   *
   * It used to fall back to «everything on screen» when nothing was ticked, because this page's idiom
   * was «the filters ARE the selection»: ~260 workers own no phone and whole brigades work at
   * poster-less sites, so ticking a box per person is an afternoon nobody finishes. That speed is kept
   * — ⌘A, or «Hamısını seç», puts every person the filter matches in the selection in one press — but
   * the implicitness is not. An action that takes a permission AWAY must never run against a set the
   * admin did not say out loud.
   */
  const bulkTargets = [...selected.values()]
  /** Of those, the ones still holding an admin-issued PIN they have never used. */
  const bulkPendingPin = bulkTargets.filter((r) => r.isActive && r.activated && r.mustChangePin)

  // The pager counts the whole matching set, which only the server knows now.
  const pageCount = Math.max(1, Math.ceil(total / PAGE_SIZE))
  const allOnPageTicked = rows.length > 0 && rows.every((r) => selected.has(r.id))

  function toggleRow(e: AdminEmployeeRow) {
    setSelected((prev) => {
      const next = new Map(prev)
      if (next.has(e.id)) next.delete(e.id); else next.set(e.id, selRow(e))
      return next
    })
  }

  function togglePage() {
    setSelected((prev) => {
      const next = new Map(prev)
      if (allOnPageTicked) rows.forEach((r) => next.delete(r.id))
      else rows.forEach((r) => next.set(r.id, selRow(r)))
      return next
    })
  }

  function toggleEmpCol(key: keyof EmpCols) {
    setEmpCols((c) => {
      const next = { ...c, [key]: !c[key] }
      try { localStorage.setItem(EMP_COLS_KEY, JSON.stringify(next)) } catch { /* not worth a broken list */ }
      return next
    })
  }

  const activeFilterCount =
    (filterLoc ? 1 : 0) + (statusSel ? 1 : 0) + (roleFilter ? 1 : 0) + (search.trim() ? 1 : 0)
  // Tick column + name + actions, plus whichever optional columns are on.
  const empColCount = 3 + Object.values(empCols).filter(Boolean).length

  function resetFilters() {
    setFilterLoc(null)
    setStatusSel('')
    setRoleFilter('')
    setSearch('')
  }

  return (
    <div>
      <div className="att-head">
        <div>
          <h1 className="att-title">İşçilər</h1>
          <div className="att-sub">
            Komandanı, giriş icazələrini və mobil tətbiq aktivliyini vahid mərkəzdən idarə edin
          </div>
        </div>
        <div className="att-head-act">
          {(stats.leftCount > 0 || showLeft) && (
            <button
              className={`btn btn-sm${showLeft ? ' btn-primary' : ''}`}
              onClick={() => { setShowLeft((v) => !v); setPage(1) }}
            >
              <IconUserX />
              {showLeft ? 'Aktiv işçilər' : `İşdən çıxanlar (${stats.leftCount})`}
            </button>
          )}
          <button
            className="btn btn-sm"
            disabled={refBusy}
            onClick={onResetAllReferences}
            title="Bütün işçilərin referans (foto audit) şəklini sıfırla — hərə növbəti girişdə yenilənir"
          >
            <IconRefresh /> Referansları sıfırla
          </button>
          <button className="btn btn-sm btn-primary" onClick={showForm && !editingId ? closeForm : startAdd}>
            <IconUsers /> İşçi əlavə et
          </button>
        </div>
      </div>

      {/* Three numbers, and the second two are the rollout question somebody asks every morning:
          how many of these people are actually on the app yet. */}
      <div className="emp-kpis">
        {([
          {
            key: 'total', tint: 'blue', Icon: IconUsers,
            label: 'Ümumi işçi sayı', value: stats.total,
            note: filterLoc ? locations.find((l) => l.id === filterLoc)?.name ?? '' : 'Bütün filiallar üzrə',
            noteCls: 'emp-note-blue',
          },
          {
            key: 'on', tint: 'leaf', Icon: IconCheck,
            label: 'Aktivləşdirilib', value: stats.activated,
            note: stats.total > 0
              ? `${Math.round((stats.activated / stats.total) * 1000) / 10}% tətbiqə qoşulub` : '—',
            noteCls: 'emp-note-leaf',
          },
          {
            key: 'off', tint: 'amber', Icon: IconX,
            label: 'Aktivləşdirilməyib', value: stats.notStarted,
            note: stats.total > 0
              ? `${Math.round((stats.notStarted / stats.total) * 1000) / 10}% aktivləşdirmə gözləyir` : '—',
            noteCls: 'emp-note-amber',
          },
        ]).map(({ key, tint, Icon, label, value, note, noteCls }) => (
          <div key={key} className="emp-kpi">
            <span className={`emp-kpi-ic att-t-${tint}`}><Icon /></span>
            <div className="emp-kpi-t">
              <div className="emp-kpi-l">{label}</div>
              <div className="emp-kpi-v">
                <span className="emp-kpi-n">{value}</span>
                <span className="emp-kpi-u">işçi</span>
              </div>
              <div className={`emp-kpi-note ${noteCls}`}>{note}</div>
            </div>
          </div>
        ))}
      </div>

      <div className="emp-panels">
        {/* Not a dashboard: every tile here is a FILTER, and pressing it leaves the list showing
            exactly the people the number counted. A figure nobody can act on is a figure nobody
            reads twice. */}
        <div className="emp-panel">
          <div className="emp-panel-h">
            <span className="emp-panel-t"><IconAlert /> Diqqət tələb edir</span>
            {statusSel && <button className="att-reset" onClick={() => setStatusSel('')}>Süzgəci götür</button>}
          </div>
          <div className="emp-attn">
            {([
              { sel: 'notstarted' as StatusSel, n: stats.notStarted, sev: 'high', sevLabel: 'Yüksək', text: 'Tətbiqi heç vaxt açmayıb' },
              { sel: 'nopush' as StatusSel, n: stats.noPush, sev: 'mid', sevLabel: 'Orta', text: 'Bildiriş çatmır' },
              { sel: 'nodevice' as StatusSel, n: stats.noDevice, sev: 'low', sevLabel: 'Yoxlayın', text: 'Cihaz bağlanmayıb' },
            ]).map((a) => (
              <button
                key={a.sel}
                className={`emp-attn-i${statusSel === a.sel ? ' on' : ''}`}
                onClick={() => setStatusSel((v) => (v === a.sel ? '' : a.sel))}
              >
                <span className="emp-attn-top">
                  <span className="emp-attn-n">{a.n}</span>
                  <span className={`emp-sev emp-sev-${a.sev}`}>{a.sevLabel}</span>
                </span>
                <span className="emp-attn-x">{a.text}</span>
              </button>
            ))}
          </div>
        </div>
      </div>

      {/* The reissued PINs. Shown until closed, with a copy button and a print button, because the
          accident this feature exists for was a page refresh between "issued" and "written down". */}
      {pinList && (
        <div className="card card-pad" style={{ marginBottom: 12, borderColor: 'var(--leaf)' }}>
          <div className="card-title" style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <IconKey /> {pinList.issued.length} nəfərə yeni müvəqqəti PIN verildi
          </div>
          <div className="muted" style={{ fontSize: 12, marginBottom: 10 }}>
            Bu siyahı yalnız indi görünür — bağlasanız bir daha açılmır. Kopyalayın və ya çap edin.
            İşçi ilk girişdə öz PIN-ini təyin edəcək.
          </div>

          <div className="tbl-wrap" style={{ maxHeight: 360, overflowY: 'auto' }}>
            <table>
              <thead><tr><th>İşçi</th><th>Telefon</th><th>PIN</th></tr></thead>
              <tbody>
                {pinList.issued.map((r) => (
                  <tr key={r.id}>
                    <td>{r.fullName}</td>
                    <td>{r.phoneNumber ? `0${r.phoneNumber}` : '—'}</td>
                    <td style={{ fontFamily: "'IBM Plex Mono',monospace", fontSize: 16, fontWeight: 700 }}>
                      {r.tempPin}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {pinList.skipped.length > 0 && (
            <div className="fb fb-err" style={{ marginTop: 10 }}>
              <IconX />
              <span>
                {pinList.skipped.length} nəfərə verilmədi:{' '}
                {pinList.skipped.map((sk) => sk.fullName).join(', ')}
              </span>
            </div>
          )}

          <div style={{ display: 'flex', gap: 8, marginTop: 12, flexWrap: 'wrap' }}>
            <button
              className="btn btn-sm btn-primary"
              onClick={() => {
                const text = pinList.issued
                  .map((r) => `${r.fullName}\t${r.phoneNumber ? '0' + r.phoneNumber : ''}\t${r.tempPin}`)
                  .join('\n')
                void navigator.clipboard.writeText(text).then(() => {
                  setPinCopied(true)
                  setTimeout(() => setPinCopied(false), 1500)
                }).catch(() => {})
              }}
            >
              {pinCopied ? '✓ Kopyalandı' : 'Siyahını kopyala'}
            </button>
            <button className="btn btn-sm" onClick={() => window.print()}>Çap et</button>
            <button
              className="btn btn-sm"
              onClick={() => {
                if (window.confirm('Siyahı bağlanacaq və bir daha açılmayacaq. Kopyaladınızmı?')) setPinList(null)
              }}
            >
              Bağla
            </button>
          </div>
        </div>
      )}


      {error && (
        <div className="fb fb-err" style={{ marginBottom: 14 }}>
          <IconX />
          <span>{error}</span>
        </div>
      )}
      {ok && (
        <div className="fb fb-ok" style={{ marginBottom: 14 }}>
          <IconCheck />
          <span>{ok}</span>
        </div>
      )}

      {/* activation link result (after invite / reinvite) */}
      {link && (
        <div className="card card-pad" style={{ marginBottom: 16 }}>
          <div className="fb fb-ok" style={{ marginBottom: 12 }}>
            <IconCheck />
            <span>
              {link.result.tempPin ? (
                <><b>{link.name}</b> əlavə edildi. Müvəqqəti PIN — işçiyə deyin, ilk girişdə özü dəyişəcək:</>
              ) : (
                <><b>{link.name}</b> üçün qeydiyyat linki. İşçiyə göndərin (email/SMS yoxdur — əl ilə paylaşın):</>
              )}
            </span>
          </div>

          {link.result.tempPin ? (
            <div className="link-box" style={{ fontSize: 28, fontWeight: 800, letterSpacing: 6, textAlign: 'center' }}>
              {link.result.tempPin}
            </div>
          ) : (
            <div className="link-box">{activationLink}</div>
          )}

          <div className="muted" style={{ fontSize: 12, marginTop: 8 }}>
            {link.result.tempPin
              ? 'PIN yalnız indi görünür — saxlanmır, sonra yalnız sıfırlamaq olar.'
              : 'Link bir dəfəlikdir; işçi onu açıb öz PIN-ini təyin edir.'}
          </div>

          <div style={{ display: 'flex', gap: 8, marginTop: 10 }}>
            <button
              className="btn btn-primary btn-sm"
              onClick={() => {
                const text = link.result.tempPin ?? activationLink
                void navigator.clipboard?.writeText(text).then(() => {
                  setCopied(true)
                  setTimeout(() => setCopied(false), 1500)
                }).catch(() => window.prompt('Kopyalayın:', text))
              }}
            >
              {copied ? 'Kopyalandı ✓' : 'Kopyala'}
            </button>
            <button className="btn btn-sm" onClick={() => setLink(null)}>
              Bağla
            </button>
          </div>
        </div>
      )}

      {/* temporary PIN result (after reset-pin) */}
      {pinReset && (
        <div className="card card-pad" style={{ marginBottom: 16 }}>
          <div className="fb fb-ok" style={{ marginBottom: 12 }}>
            <IconCheck />
            <span>
              <b>{pinReset.name}</b> üçün yeni müvəqqəti PIN. İşçiyə deyin — girib öz PIN-ini dəyişsin.
            </span>
          </div>
          <div className="link-box" style={{ fontSize: 28, fontWeight: 800, letterSpacing: 6, textAlign: 'center' }}>
            {pinReset.pin}
          </div>
          <div style={{ display: 'flex', gap: 8, marginTop: 10 }}>
            <button
              className="btn btn-primary btn-sm"
              onClick={() => {
                void navigator.clipboard?.writeText(pinReset.pin)
                setCopied(true)
                setTimeout(() => setCopied(false), 1500)
              }}
            >
              {copied ? 'Kopyalandı ✓' : 'Kopyala'}
            </button>
            <button className="btn btn-sm" onClick={() => setPinReset(null)}>
              Bağla
            </button>
          </div>
        </div>
      )}

      {/* add / edit form */}
      {/* Add flow: a single-vs-bulk switch shown only when adding (editing is always the single form).
          Toplu əlavə used to be its own page; it now lives here so onboarding is one button. */}
      {showForm && !editingId && (
        <div className="chip-row" style={{ marginBottom: 10 }}>
          <span className={`chip${addMode === 'single' ? ' active' : ''}`} onClick={() => setAddMode('single')}>
            Tək-tək
          </span>
          <span className={`chip${addMode === 'bulk' ? ' active' : ''}`} onClick={() => setAddMode('bulk')}>
            Toplu əlavə
          </span>
        </div>
      )}

      {showForm && !editingId && addMode === 'bulk' && (
        <div className="card card-pad" style={{ marginBottom: 16, maxWidth: 900 }}>
          <BulkInvitePage />
        </div>
      )}

      {showForm && (editingId || addMode === 'single') && (
        <form onSubmit={onSubmit} className="card card-pad" style={{ marginBottom: 16, maxWidth: 760 }}>
          <div style={{ fontWeight: 700, color: 'var(--c900)', marginBottom: 14 }}>
            {editingId ? 'İşçini redaktə et' : 'Yeni işçi'}
          </div>

          <div className="form-row cols2">
            <div>
              <label className="form-label">Ad</label>
              <input className="inp" required value={form.firstName} onChange={(e) => set('firstName', e.target.value)} />
            </div>
            <div>
              <label className="form-label">Soyad</label>
              <input className="inp" required value={form.lastName} onChange={(e) => set('lastName', e.target.value)} />
            </div>
          </div>
          <div className="form-row cols2">
            <div>
              <label className="form-label">Ata adı</label>
              <input className="inp" value={form.fatherName} onChange={(e) => set('fatherName', e.target.value)} />
            </div>
            <div />
          </div>

          <div className="form-row cols2">
            <div>
              <label className="form-label">Vəzifə</label>
              <PositionSelect value={form.position} onChange={(v) => set('position', v)} />
            </div>
            <div>
              <label className="form-label">Doğum tarixi</label>
              <input
                className="inp"
                type="date"
                min="1940-01-01"
                max="2012-12-31"
                value={form.birthDate}
                onChange={(e) => set('birthDate', e.target.value)}
              />
              {!form.birthDate && form.birthYear && (
                <div className="muted" style={{ fontSize: 11, marginTop: 4 }}>
                  Hazırda yalnız il məlumdur: {form.birthYear}. Tam tarix seçsəniz yenilənəcək.
                </div>
              )}
            </div>
          </div>

          {/* «Sənəd üzrə» — only for the minority whose paperwork names a different company from the
              one they work at. Nothing is computed from these: attendance, the geofence, the shift,
              the tabel and the pay all follow the branch above. They exist so the fact stops living
              in one manager's head and starts appearing on the report the owner reads. */}
          <div className="form-row cols2">
            <div>
              <label className="form-label">Sənəd üzrə şirkət</label>
              {groupCompanies.length > 0 ? (
                <select
                  className="inp"
                  value={form.paperEmployer}
                  onChange={(e) => set('paperEmployer', e.target.value)}
                >
                  {/* The empty option is the normal case and must stay reachable: it is how an admin
                      says "there is no discrepancy" and clears a note entered by mistake. */}
                  <option value="">— eyni şirkət —</option>
                  {groupCompanies.map((c) => (
                    <option key={c} value={c}>{c}</option>
                  ))}
                  {/* A value already stored that is no longer on the list would otherwise be silently
                      swapped for the blank option the moment anybody saved this form. */}
                  {form.paperEmployer && !groupCompanies.includes(form.paperEmployer) && (
                    <option value={form.paperEmployer}>{form.paperEmployer}</option>
                  )}
                </select>
              ) : (
                <input
                  className="inp"
                  value={form.paperEmployer}
                  placeholder="məs. Bakı Abadlıq Xidməti"
                  onChange={(e) => set('paperEmployer', e.target.value)}
                />
              )}
            </div>
            <div>
              <label className="form-label">Sənəd üzrə ərazi</label>
              <input
                className="inp"
                value={form.paperSite}
                placeholder="məs. Nərimanov Ofis"
                disabled={!form.paperEmployer.trim()}
                onChange={(e) => set('paperSite', e.target.value)}
              />
              <div className="muted" style={{ fontSize: 11, marginTop: 4 }}>
                Yalnız sənədi başqa şirkəti göstərən işçilər üçün. Davamiyyət yuxarıdakı filiala görə
                hesablanır.
              </div>
            </div>
          </div>

          <div className="form-row cols2">
            <div>
              <label className="form-label">Telefon nömrəsi</label>
              <input className="inp" type="tel" inputMode="tel" placeholder="0501234567" value={form.phoneNumber} onChange={(e) => set('phoneNumber', e.target.value)} />
            </div>
            <div>
              <label className="form-label">Email (istəyə bağlı)</label>
              <input className="inp" type="email" value={form.email} onChange={(e) => set('email', e.target.value)} />
            </div>
          </div>

          <div className="form-row cols2">
            <div>
              <label className="form-label">Rol</label>
              {/* Same reason as Status: demoting yourself out of Admin locks you out of this panel,
                  and there may be no one else who can put you back. */}
              <select
                className="inp"
                value={form.role}
                disabled={isSelf}
                onChange={(e) => set('role', e.target.value as Role)}
              >
                <option value="Employee">İşçi</option>
                <option value="Manager">Menecer</option>
                <option value="Admin">Admin</option>
              </select>
              {isSelf && (
                <div className="muted" style={{ fontSize: 11, marginTop: 4 }}>Öz rolunuzu dəyişə bilməzsiniz</div>
              )}
            </div>
            <div>
              <label className="form-label">Filial</label>
              <select className="inp" value={form.locationId} onChange={(e) => set('locationId', e.target.value)}>
                {locations.map((l) => (
                  <option key={l.id} value={l.id}>
                    {l.name}
                  </option>
                ))}
              </select>
            </div>
          </div>

          {/* A Manager sees only the branches ticked here — nothing else. Until this existed, nothing
              outside DevController ever wrote them, so every manager in production opened an empty
              panel with no way to tell why. It is deliberately not the same as "Filial" above: that
              is where they clock in; this is what they may look at. */}
          {form.role === 'Manager' && (
            <div style={{ marginTop: 4 }}>
              <label className="form-label">Hansı filiallara baxa bilsin?</label>
              <div
                style={{
                  border: '1px solid var(--c200)', borderRadius: 10, padding: '10px 12px',
                  display: 'flex', flexWrap: 'wrap', gap: '8px 18px',
                }}
              >
                {locations.map((l) => (
                  <label key={l.id} style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 13, cursor: 'pointer' }}>
                    <input
                      type="checkbox"
                      checked={form.managedLocationIds.includes(l.id)}
                      onChange={(e) =>
                        set(
                          'managedLocationIds',
                          e.target.checked
                            ? [...form.managedLocationIds, l.id]
                            : form.managedLocationIds.filter((x) => x !== l.id),
                        )
                      }
                    />
                    {l.name}
                  </label>
                ))}
                {locations.length === 0 && <span className="muted" style={{ fontSize: 12 }}>Filial yoxdur</span>}
              </div>
              <div
                className="muted"
                style={{ fontSize: 11, marginTop: 4, color: form.managedLocationIds.length === 0 ? 'var(--clay)' : undefined }}
              >
                {form.managedLocationIds.length === 0
                  ? 'Heç biri seçilməyib — menecer panelə girə bilər, amma hər səhifə BOŞ olacaq.'
                  : `${form.managedLocationIds.length} filialın davamiyyətini görəcək. Bu, işlədiyi filialdan asılı deyil.`}
              </div>
            </div>
          )}

          {editingId && (
            <div className="form-row cols2">
              <div>
                <label className="form-label">Status</label>
                {/* Locked when you are editing yourself: deactivating your own account closes your
                    login silently, and if you are the only admin nobody left can undo it. The server
                    refuses this too — this just stops you reaching for it. */}
                <select
                  className="inp"
                  value={form.isActive ? '1' : '0'}
                  disabled={isSelf}
                  onChange={(e) => set('isActive', e.target.value === '1')}
                >
                  <option value="1">Aktiv</option>
                  <option value="0">Deaktiv (giriş bağlı)</option>
                </select>
                {isSelf && (
                  <div className="muted" style={{ fontSize: 11, marginTop: 4 }}>Öz hesabınızı deaktiv edə bilməzsiniz</div>
                )}
              </div>
            </div>
          )}

          <div className="form-row cols2">
            <div>
              <label className="form-label">Aylıq maaş (AZN)</label>
              <input
                className="inp"
                type="number"
                min="0"
                step="0.01"
                inputMode="decimal"
                value={form.monthlySalary}
                onChange={(e) => set('monthlySalary', e.target.value)}
                placeholder="məs. 800"
              />
            </div>
            <div />
          </div>
          <p style={{ fontSize: 12, color: 'var(--c500)', marginTop: -6, marginBottom: 4 }}>
            Maaş hesabatı üçün. Boş buraxsanız işçi maaş cədvəlinə düşmür.
          </p>

          {/* Someone who refuses to be photographed will point the camera at the ceiling instead —
              which reads as a verified check-in and quietly teaches everyone else the same trick.
              An exemption granted here keeps the refusal on the record and the audit meaningful. */}
          <label style={{ display: 'flex', alignItems: 'flex-start', gap: 8, margin: '10px 0 4px' }}>
            <input
              type="checkbox"
              checked={form.photoExempt}
              onChange={(e) => set('photoExempt', e.target.checked)}
              style={{ marginTop: 3 }}
            />
            <span>
              <span style={{ fontWeight: 700, fontSize: 13 }}>Giriş şəkli tələb olunmasın</span>
              <span style={{ display: 'block', fontSize: 12, color: 'var(--c500)' }}>
                Bu işçidə skan zamanı kamera açılmır. Yer və cihaz yoxlaması qüvvədə qalır.
              </span>
            </span>
          </label>

          {/* Field/mobile check-in is opt-in: only workers actually sent to poster-less sites get the
              «Səyyar / Sahə ziyarəti» screen + self-report, and only they can be assigned a visit. */}
          <label style={{ display: 'flex', alignItems: 'flex-start', gap: 8, margin: '10px 0 4px' }}>
            <input
              type="checkbox"
              checked={form.canFieldCheckIn}
              onChange={(e) => set('canFieldCheckIn', e.target.checked)}
              style={{ marginTop: 3 }}
            />
            <span>
              <span style={{ fontWeight: 700, fontSize: 13 }}>Sahə girişi icazəsi</span>
              <span style={{ display: 'block', fontSize: 12, color: 'var(--c500)' }}>
                İşçi QR-suz, GPS ilə sahə ziyarəti qeyd edə bilər (poster olmayan obyektlər üçün). Bağlı olsa, bu işçidə funksiya görünmür.
              </span>
            </span>
          </label>

          {/* The same two settings the BRANCH carries, pinned for one person. Separate from the
              checkbox above on purpose: «Sahə ziyarəti» is an errand somewhere else, with its own
              screen and its own board. These two are about this person's ORDINARY day. Default is
              «Filiala görə» — the branch is still where this belongs for everybody else, and the
              nineteen invisible people were missing a fact about the PLACE, not a per-person tick. */}
          <div style={{ margin: '12px 0 4px', padding: '10px 12px', border: '1px solid var(--c100)', borderRadius: 10 }}>
            <div style={{ fontWeight: 700, fontSize: 13, marginBottom: 2 }}>Fərdi giriş rejimi</div>
            <div style={{ fontSize: 12, color: 'var(--c500)', marginBottom: 8 }}>
              Yalnız bu işçi üçün. Toxunmasan filialın ayarı işləyir.
            </div>

            <label style={{ display: 'block', fontSize: 12, marginBottom: 6 }}>
              QR posteri
              <select
                className="input"
                value={form.qrlessOverride}
                onChange={(e) => set('qrlessOverride', e.target.value as '' | 'on' | 'off')}
                style={{ marginTop: 2 }}
              >
                <option value="">Filiala görə</option>
                <option value="on">QR-suz — üz və GPS ilə giriş etsin</option>
                <option value="off">QR posteri skan etsin</option>
              </select>
            </label>

            <label style={{ display: 'block', fontSize: 12 }}>
              GPS divarı
              <select
                className="input"
                value={form.fenceOverride}
                onChange={(e) => set('fenceOverride', e.target.value as '' | 'on' | 'off')}
                style={{ marginTop: 2 }}
              >
                <option value="">Filiala görə</option>
                <option value="on">Radiusdan kənarda girişi rədd et</option>
                <option value="off">Rədd etmə — yalnız ölç (yeri xəritəyə düşür)</option>
              </select>
            </label>

            {form.qrlessOverride === 'on' && form.fenceOverride === 'off' && (
              <div style={{ fontSize: 12, color: '#b91c1c', marginTop: 8 }}>
                <b>Diqqət:</b> bu işçidə nə poster, nə GPS yoxlaması qalır — yeganə lövbər selfi və üz
                tanımadır.
              </div>
            )}
          </div>

          {/* The one-phone-one-employee rule is what stops a colleague clocking in for an absent
              worker, and this hands it away for whoever is on the shared handset. Off by default and
              granted per person, so a brigade phone is a decision the company makes and can see —
              before, any employee could quietly assemble one and nothing showed it. */}
          <label style={{ display: 'flex', alignItems: 'flex-start', gap: 8, margin: '10px 0 4px' }}>
            <input
              type="checkbox"
              checked={form.canShareDevice}
              onChange={(e) => set('canShareDevice', e.target.checked)}
              style={{ marginTop: 3 }}
            />
            <span>
              <span style={{ fontWeight: 700, fontSize: 13 }}>Ortaq telefon icazəsi</span>
              <span style={{ display: 'block', fontSize: 12, color: 'var(--c500)' }}>
                Telefonu olmayan işçilər üçün: hesabı briqadanın ortaq telefonunda saxlanıla bilər.
                Bağlı olsa, bu işçi yalnız öz cihazından skan edə bilər.
              </span>
            </span>
          </label>

          {/* One shift control. A person's hours, work-days and rotation all come from the named shift
              chosen here; shifts are created and edited in the Növbələr panel, not retyped per person.
              The employee's own old per-person hours/rotation are still round-tripped in state (never
              blanked on save) so nothing is lost before they are migrated onto a named shift. */}
          <div style={{ marginBottom: 14 }}>
            <label className="form-label">Növbə</label>
            <select
              className="inp"
              value={form.scheduleId}
              onChange={(e) => {
                if (e.target.value === '__new__') { navigate('/admin/schedules'); return }
                set('scheduleId', e.target.value)
              }}
            >
              <option value="">— növbə yoxdur (filialın saatları) —</option>
              {/* Only the shifts this person's branch actually offers: the company-wide ones, plus the
                  ones pinned to their own branch. With several branches the unfiltered list was every
                  shift in the company on every card, and putting another site's crew shift on somebody
                  was one mis-click that showed up nowhere afterwards except in hours that did not add
                  up. The server refuses it too (ScheduleBelongsToOtherBranch) — this is the door. */}
              {schedules
                .filter((s) => s.locationId === null || s.locationId === form.locationId)
                .map((s) => (
                  <option key={s.id} value={s.id}>
                    {s.name} · {s.shiftStart}–{s.shiftEnd}{s.isOvernight ? ' 🌙' : ''}
                  </option>
                ))}
              <option value="__new__">＋ Yeni növbə yarat…</option>
            </select>
            <p style={{ fontSize: 12, color: 'var(--c500)', marginTop: 6, marginBottom: 0, lineHeight: 1.6 }}>
              İşçinin saatları və iş günləri seçdiyiniz növbədən gəlir. İstədiyiniz növbə siyahıda
              yoxdursa «＋ Yeni növbə yarat» ilə Növbələr panelində yaradın.
            </p>
            {/* This employee still carries old per-person hours / rotation and no shift — nudge to
                move them onto a named shift so the whole team is managed in one place. */}
            {!form.scheduleId && (form.workStart || form.cycle.days) && (
              <div className="fb" style={{ marginTop: 10, background: 'var(--amber-bg, #FFF7ED)', color: '#9a3412' }}>
                <span>
                  Bu işçidə köhnə fərdi qrafik var
                  {form.workStart && form.workEnd ? ` (🕒 ${form.workStart}–${form.workEnd})` : ''}
                  {form.cycle.days ? ' 🔄' : ''}. Yuxarıdan uyğun növbəni seçin —
                  yoxdursa «＋ Yeni növbə yarat» ilə yaradıb təyin edin.
                </span>
              </div>
            )}
          </div>

          {/* How this person gets in the first time. Only on create — an existing employee already
              has a credential, and replacing it is "PIN sıfırla" on their row. The PIN is the default
              because it is what works for the people added one at a time: a link has to reach a phone
              that can open it, and four digits can be read out loud across a yard. */}
          {!editingId && (
            <div style={{ marginBottom: 14 }}>
              <label className="form-label">İlk giriş necə verilsin?</label>
              <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
                <button
                  type="button"
                  className={`btn btn-sm${activateWithPin ? ' btn-primary' : ''}`}
                  onClick={() => setActivateWithPin(true)}
                >
                  Müvəqqəti PIN
                </button>
                <button
                  type="button"
                  className={`btn btn-sm${activateWithPin ? '' : ' btn-primary'}`}
                  onClick={() => setActivateWithPin(false)}
                >
                  Qeydiyyat linki
                </button>
              </div>
              <p className="muted" style={{ fontSize: 12, marginTop: 6, marginBottom: 0 }}>
                {activateWithPin
                  ? 'Hesab dərhal açılır, 4 rəqəmli PIN bir dəfə göstərilir — işçiyə deyirsiniz, o da ilk girişdə özününkünü təyin edir.'
                  : 'İşçiyə link göndərilir; linki açıb öz PIN-ini özü təyin edir.'}
              </p>
            </div>
          )}

          <div style={{ display: 'flex', gap: 8 }}>
            <button type="submit" className="btn btn-primary" disabled={saving || !form.locationId}>
              <IconCheck />
              {saving
                ? 'Yadda saxlanır…'
                : editingId
                  ? 'Yadda saxla'
                  : activateWithPin ? 'Əlavə et və PIN ver' : 'Əlavə et və link yarat'}
            </button>
            <button type="button" className="btn" onClick={closeForm} disabled={saving}>
              Ləğv et
            </button>
          </div>
        </form>
      )}

      {/* attendance view/correction panel */}
      {attendanceEmployee && (
        <div className="card card-pad" style={{ marginBottom: 16 }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 14 }}>
            <div style={{ fontWeight: 700, color: 'var(--c900)' }}>
              {attendanceEmployee.fullName} — davamiyyət qeydləri
            </div>
            <div style={{ display: 'flex', gap: 8 }}>
              <button className="btn btn-sm" disabled={refBusy} onClick={() => onResetReference(attendanceEmployee)}>
                Referansı sıfırla
              </button>
              <button className="btn btn-sm" onClick={closeAttendance}>Bağla</button>
            </div>
          </div>

          {attendanceError && (
            <div className="fb fb-err" style={{ marginBottom: 14 }}>
              <IconX />
              <span>{attendanceError}</span>
            </div>
          )}

          <div style={{ marginBottom: 14 }}>
            {!showCreateRecord ? (
              <button className="btn btn-sm" onClick={() => setShowCreateRecord(true)}>
                <IconCheck /> Yeni qeyd əlavə et
              </button>
            ) : (
              <div className="card card-pad" style={{ background: 'var(--c50, #f6f8f4)' }}>
                <div className="form-row cols2">
                  <div>
                    <label className="form-label">Tarix</label>
                    <input className="inp" type="date" value={createDate} onChange={(ev) => setCreateDate(ev.target.value)} />
                  </div>
                  <div>
                    <label className="form-label">Giriş vaxtı</label>
                    <input className="inp" type="datetime-local" value={createCheckIn} onChange={(ev) => setCreateCheckIn(ev.target.value)} />
                  </div>
                </div>
                <div className="form-row cols2">
                  <div>
                    <label className="form-label">Çıxış vaxtı (istəyə bağlı)</label>
                    <input className="inp" type="datetime-local" value={createCheckOut} onChange={(ev) => setCreateCheckOut(ev.target.value)} />
                  </div>
                </div>
                <div style={{ display: 'flex', gap: 8 }}>
                  <button
                    className="btn btn-primary btn-sm"
                    disabled={savingRecord || !createDate || !createCheckIn}
                    onClick={submitCreateRecord}
                  >
                    {savingRecord ? 'Yadda saxlanır…' : 'Yadda saxla'}
                  </button>
                  <button className="btn btn-sm" onClick={() => setShowCreateRecord(false)} disabled={savingRecord}>
                    Ləğv et
                  </button>
                </div>
              </div>
            )}
          </div>

          {attendanceLoading && <p className="muted">Yüklənir…</p>}

          <div className="tbl-wrap tbl-cards">
            <table>
              <thead>
                <tr>
                  <th>Tarix</th>
                  <th>Status</th>
                  <th>Giriş</th>
                  <th>Çıxış</th>
                  <th style={{ textAlign: 'right' }}>Əməliyyat</th>
                </tr>
              </thead>
              <tbody>
                {attendanceRecords.map((r) => (
                  <tr key={r.recordId}>
                    {editingRecordId === r.recordId ? (
                      <>
                        <td className="mono">{r.attendanceDate}</td>
                        <td><StatusBadge status={r.status} /></td>
                        <td>
                          <input
                            className="inp"
                            type="datetime-local"
                            value={editCheckIn}
                            onChange={(ev) => setEditCheckIn(ev.target.value)}
                            style={{ minWidth: 180 }}
                          />
                        </td>
                        <td>
                          <input
                            className="inp"
                            type="datetime-local"
                            value={editCheckOut}
                            onChange={(ev) => setEditCheckOut(ev.target.value)}
                            style={{ minWidth: 180 }}
                          />
                        </td>
                        <td style={{ textAlign: 'right' }}>
                          <div style={{ display: 'flex', gap: 8, justifyContent: 'flex-end' }}>
                            <button className="btn btn-primary btn-sm" disabled={savingRecord} onClick={saveEditRecord}>
                              {savingRecord ? 'Saxlanır…' : 'Saxla'}
                            </button>
                            <button className="btn btn-sm" disabled={savingRecord} onClick={() => setEditingRecordId(null)}>
                              Ləğv et
                            </button>
                          </div>
                        </td>
                      </>
                    ) : (
                      <>
                        <td className="mono">{r.attendanceDate}</td>
                        <td><StatusBadge status={r.status} /></td>
                        <td className="mono">{r.checkInAtUtc ? fmtFullDateTime(r.checkInAtUtc) : '—'}</td>
                        <td className="mono">{r.checkOutAtUtc ? fmtFullDateTime(r.checkOutAtUtc) : '—'}</td>
                        <td style={{ textAlign: 'right' }}>
                          <div style={{ display: 'flex', gap: 8, justifyContent: 'flex-end' }}>
                            {r.checkOutAtUtc && (
                              <button className="btn btn-sm" disabled={savingRecord} onClick={() => onClearCheckOut(r)}>
                                Çıxışı ləğv et
                              </button>
                            )}
                            <button className="btn btn-sm" onClick={() => startEditRecord(r)}>
                              {r.status === 'Incomplete' ? 'Çıxışı əlavə et' : 'Düzəlt'}
                            </button>
                          </div>
                        </td>
                      </>
                    )}
                  </tr>
                ))}
                {!attendanceLoading && attendanceRecords.length === 0 && (
                  <tr>
                    <td colSpan={5} className="muted" style={{ textAlign: 'center', padding: 20 }}>
                      Qeyd yoxdur
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* employees table */}
      <div className="att-table">
        {/* The contextual bar.
            The bulk panel that used to sit above the table is gone: it stood there whether or not
            anybody was selected, and it had to answer «applies to whom?» in prose. Bound to the
            selection it answers that by existing — and the escape hatch the speed depended on is now
            explicit rather than implicit: ⌘A, or «Hamısını seç», puts the whole filtered list in the
            selection in one press. That matters most for the actions that take something AWAY. */}
        {selected.size === 0
          ? (
            <div className="emp-bar empty">
              <span className="emp-bar-hint">
                <IconAlert />
                Əməliyyat üçün cədvəldən işçiləri seçin
              </span>
              <span className="emp-bar-kbd">
                <span className="att-kbd">⌘A</span> hamısını seç
              </span>
            </div>
          )
          : (
            <div className="emp-bar on">
              <div className="emp-bar-l">
                <button className="emp-bar-x" onClick={() => setSelected(new Map())} title="Seçimi sil" aria-label="Seçimi sil">
                  <IconX />
                </button>
                <span className="emp-bar-n"><b>{selected.size}</b> işçi seçildi</span>
                <span className="emp-bar-sep" />
                {schedules.length > 0 && (
                  <>
                    <select
                      className="emp-bar-sel"
                      value={bulkShift}
                      onChange={(e) => setBulkShift(e.target.value)}
                      aria-label="Növbə seçin"
                    >
                      <option value="">Növbə seçin…</option>
                      {schedules.map((s) => (
                        <option key={s.id} value={s.id}>{s.name} · {s.shiftStart}–{s.shiftEnd}</option>
                      ))}
                    </select>
                    <button
                      className="emp-bar-b primary"
                      disabled={sharing || !bulkShift}
                      onClick={() => void applyShift(bulkTargets)}
                    >
                      <IconCalendar /> Növbə təyin et
                    </button>
                  </>
                )}
                <button
                  className="emp-bar-b"
                  disabled={sharing}
                  onClick={() => void setPermission(bulkTargets, 'ShareDevice', true)}
                >
                  <IconPhone /> Ortaq telefon
                </button>
                <button
                  className="emp-bar-b"
                  disabled={sharing}
                  onClick={() => void setPermission(bulkTargets, 'FieldCheckIn', true)}
                >
                  <IconMapPin /> Sahə ziyarəti
                </button>
                <div className="emp-more-w">
                  <button className={`emp-bar-b${moreOpen ? ' on' : ''}`} onClick={() => setMoreOpen((v) => !v)}>
                    Daha çox <IconChevronDown />
                  </button>
                  {moreOpen && (
                    <>
                      <div className="att-backdrop" onClick={() => setMoreOpen(false)} />
                      <div className="emp-more">
                        <button
                          className="emp-more-i"
                          disabled={issuing || bulkPendingPin.length === 0}
                          onClick={() => { setMoreOpen(false); void issuePins(bulkPendingPin) }}
                        >
                          <IconKey />
                          <span>
                            Müvəqqəti PIN ver
                            <i>{bulkPendingPin.length} nəfər hələ heç vaxt girməyib</i>
                          </span>
                        </button>
                        {/* Taking something away sits apart and is drawn as what it is. The three
                            below each remove an ability somebody is relying on today — a brigade's
                            shared phone, a field worker's only way to clock in, a crew's hours. */}
                        <div className="emp-more-sep">Geri alan əməliyyatlar</div>
                        <button
                          className="emp-more-i danger"
                          disabled={sharing}
                          onClick={() => { setMoreOpen(false); void setPermission(bulkTargets, 'ShareDevice', false) }}
                        >
                          <IconPhone />
                          <span>
                            Ortaq telefon icazəsini geri al
                            <i>Briqadanın telefonunda hesab saxlamaq bağlanır</i>
                          </span>
                        </button>
                        <button
                          className="emp-more-i danger"
                          disabled={sharing}
                          onClick={() => { setMoreOpen(false); void setPermission(bulkTargets, 'FieldCheckIn', false) }}
                        >
                          <IconMapPin />
                          <span>
                            Sahə ziyarəti icazəsini geri al
                            <i>Plakatsız sahədə işləyənin yeganə giriş yolu bağlanır</i>
                          </span>
                        </button>
                        <button
                          className="emp-more-i danger"
                          disabled={sharing}
                          onClick={() => { setMoreOpen(false); setBulkShift('none'); void applyShift(bulkTargets) }}
                        >
                          <IconCalendar />
                          <span>
                            Növbəni ləğv et
                            <i>Öz saatına, o da yoxdursa filialın saatına qayıdır</i>
                          </span>
                        </button>
                      </div>
                    </>
                  )}
                </div>
              </div>
              <div className="emp-bar-r">
                <span className="emp-bar-note">Seçilmiş işçilərə tətbiq olunur</span>
                {selected.size < total && (
                  <button className="emp-bar-all" disabled={selectingAll} onClick={() => void selectAll()}>
                    {selectingAll ? 'Seçilir…' : `Hamısını seç (${total})`}
                  </button>
                )}
              </div>
            </div>
          )}

        <div className="att-tbar">
          <div>
            <span className="att-tbar-t">İşçi siyahısı</span>
            <span className="att-tbar-n">{listing ? '…' : `${total} nəticə`}</span>
          </div>
          <div className="att-tbar-a">
            {selected.size > 0 && (
              <button className="att-reset" onClick={() => setSelected(new Map())}>
                {selected.size} seçilib — seçimi götür
              </button>
            )}
            <button className={`att-tool${empColsOpen ? ' on' : ''}`} onClick={() => setEmpColsOpen((v) => !v)}>
              <IconColumns />
              Sütunlar
            </button>
            {empColsOpen && (
              <>
                <div className="att-backdrop" onClick={() => setEmpColsOpen(false)} />
                <div className="att-pop" style={{ left: 'auto', right: 0, minWidth: 210 }}>
                  {([
                    ['position', 'Vəzifə'],
                    ['location', 'Filial'],
                    ['role', 'Rol'],
                    ['device', 'Cihaz'],
                    ['push', 'Bildiriş'],
                    ['lastActive', 'Son aktivlik'],
                    ['status', 'Qeydiyyat'],
                  ] as [keyof EmpCols, string][]).map(([key, label]) => (
                    <label key={key} className="att-opt">
                      <input type="checkbox" checked={empCols[key]} onChange={() => toggleEmpCol(key)} />
                      <span className="att-opt-t">{label}</span>
                    </label>
                  ))}
                </div>
              </>
            )}
          </div>
        </div>

        <div className="emp-filters">
          <div className="att-f" style={{ flex: '1 1 280px' }}>
            <span className="att-f-lbl">Axtarış</span>
            <span className="att-search">
              <IconSearch />
              <input
                ref={searchRef}
                type="text"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                placeholder="Ad, telefon nömrəsi və ya işçi ID-si üzrə axtarın"
                aria-label="İşçi axtar"
              />
              {search
                ? (
                  <button className="att-clear" onClick={() => setSearch('')} title="Təmizlə" aria-label="Axtarışı təmizlə">
                    <IconX />
                  </button>
                )
                : <span className="att-kbd">⌘K</span>}
            </span>
          </div>
          <div className="att-f">
            <span className="att-f-lbl">Status</span>
            <select
              className="emp-sel"
              value={statusSel}
              onChange={(e) => setStatusSel(e.target.value as StatusSel)}
              aria-label="Status"
            >
              {STATUS_OPTIONS.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
            </select>
          </div>
          <div className="att-f">
            <span className="att-f-lbl">Filial</span>
            <select
              className="emp-sel"
              value={filterLoc ?? ''}
              onChange={(e) => setFilterLoc(e.target.value || null)}
              aria-label="Filial"
            >
              <option value="">Bütün filiallar</option>
              {locations.map((l) => <option key={l.id} value={l.id}>{l.name}</option>)}
            </select>
          </div>
          <div className="att-f">
            <span className="att-f-lbl">Rol</span>
            <select
              className="emp-sel"
              value={roleFilter}
              onChange={(e) => setRoleFilter(e.target.value)}
              aria-label="Rol"
            >
              <option value="">Bütün rollar</option>
              {Object.entries(ROLE_LABEL).map(([v, l]) => <option key={v} value={v}>{l}</option>)}
            </select>
          </div>
          {activeFilterCount > 0 && (
            <button className="att-reset" style={{ alignSelf: 'flex-end', height: 38 }} onClick={resetFilters}>
              Sıfırla
            </button>
          )}
        </div>

        <div className="tbl-wrap tbl-cards">
        <table>
          <thead>
            <tr>
              <th className="emp-tick">
                <input
                  type="checkbox"
                  checked={allOnPageTicked}
                  onChange={togglePage}
                  title="Bu səhifədəkilərin hamısını seç"
                  aria-label="Bu səhifədəkilərin hamısını seç"
                />
              </th>
              <th>İşçi / telefon / ID</th>
              {empCols.position && <th>Vəzifə</th>}
              {empCols.location && <th>Filial</th>}
              {empCols.role && <th>Rol</th>}
              {empCols.device && <th>Cihaz</th>}
              {empCols.push && <th>Bildiriş</th>}
              {empCols.lastActive && <th>Son aktivlik</th>}
              {empCols.status && <th>Qeydiyyat</th>}
              <th style={{ textAlign: 'right' }}>Əməliyyat</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((e) => (
              <tr key={e.id} className={selected.has(e.id) ? 'emp-on' : undefined} style={{ opacity: e.isActive ? 1 : 0.55 }}>
                <td className="emp-tick" data-label="">
                  <input
                    type="checkbox"
                    checked={selected.has(e.id)}
                    onChange={() => toggleRow(e)}
                    aria-label={`${e.fullName} — seç`}
                  />
                </td>
                <td data-label="İşçi">
                  <span className="att-emp">
                    <span className="att-av" aria-hidden="true">{initials(e.fullName)}</span>
                    <span className="att-emp-t">
                  <div style={{ fontWeight: 700 }}>
                    <Link to={`/admin/employees/${e.id}`} style={{ color: 'var(--c900)', textDecoration: 'none' }}>
                      {e.fullName}{e.fatherName ? ` ${e.fatherName}` : ''}
                    </Link>
                    {!e.isActive && (
                      <span className="tag" style={{ marginLeft: 8, background: 'rgba(154,52,18,0.12)', color: '#9a3412' }}>
                        Deaktiv
                      </span>
                    )}
                  </div>
                  {/* Phone, birth date and id on ONE line, as the design has it. They were three
                      stacked lines, which made every row three times the height of its own content —
                      on nine hundred people that is what turns a list into a scroll. */}
                  <div className="emp-meta">
                    {e.phoneNumber
                      ? <>0{e.phoneNumber}</>
                      : <span className="emp-nophone">nömrə yoxdur</span>}
                    {(e.birthDate || e.birthYear) &&
                      // Father name rides with the full name above; the meta keeps only birth date.
                      ` · ${e.birthDate ? e.birthDate.split('-').reverse().join('.') : e.birthYear}`}
                    {' · '}<span className="mono">ID {e.id.slice(0, 8)}</span>
                  </div>
                    </span>
                  </span>
                </td>
                {empCols.position && <td data-label="Vəzifə">{e.position || '—'}</td>}
                {empCols.location && <td data-label="Filial">
                  {e.locationName ?? '—'}
                  {/* Shown right under the branch, because the whole point is the CONTRAST between
                      where this person works and where their paperwork says they belong. On its own
                      line elsewhere it would read as a second branch. */}
                  {e.paperEmployer && (
                    <div style={{ fontSize: 11, color: '#b45309', marginTop: 2 }}>
                      sənəd: {e.paperEmployer}{e.paperSite ? ` / ${e.paperSite}` : ''}
                    </div>
                  )}
                  {/* The employee's own shift when set — so it's visible which schedule (day/night)
                      they're on at a location that runs several. */}
                  {/* The shift decides hours and days, so it replaces the raw times in the list. */}
                  {e.scheduleName && (
                    <div style={{ fontSize: 11, color: 'var(--c400)', marginTop: 2 }}>
                      🗓️ {e.scheduleName}
                    </div>
                  )}
                  {!e.scheduleName && e.workStart && e.workEnd && (
                    <div style={{ fontSize: 11, color: 'var(--c400)', marginTop: 2 }}>
                      🕒 {e.workStart}–{e.workEnd}{e.workEnd < e.workStart ? ' 🌙' : ''}
                    </div>
                  )}
                  {/* A rotation changes which DAYS count, not just the hours — and it silently
                      decides whether a blank day is rest or an unpaid absence, so it belongs in the
                      list rather than only inside the edit form. */}
                  {!e.scheduleName && e.workCycleDays && (
                    <div style={{ fontSize: 11, color: 'var(--c400)', marginTop: 2 }}>
                      🔄 {cycleLabel(e.workCycleDays, e.workCycleOnDays ?? 1)}
                    </div>
                  )}
                </td>}
                {empCols.role && <td data-label="Rol">
                  {ROLE_LABEL[e.role] ?? e.role}
                  {/* A manager with no branches is not a lesser manager — they see nothing at all.
                      That is invisible from the admin's side unless the list says so. */}
                  {e.role === 'Manager' && (
                    e.managedLocationIds?.length > 0 ? (
                      <div style={{ fontSize: 11, color: 'var(--c400)', marginTop: 2 }}>
                        👁 {e.managedLocationNames.join(', ')}
                      </div>
                    ) : (
                      <div style={{ fontSize: 11, color: 'var(--clay)', fontWeight: 600, marginTop: 2 }}>
                        filial seçilməyib — boş panel
                      </div>
                    )
                  )}
                </td>}
                {empCols.device && <td data-label="Cihaz">{deviceBadge(e.hasDevice, e.deviceLabel)}</td>}
                {empCols.push && <td data-label="Bildiriş">
                  {/* Whether an announcement/reminder actually reaches this person's phone. */}
                  {e.pushEnabled
                    ? pill('Açıq', '#2e7d32', 'rgba(124,179,66,0.15)')
                    : pill('Bağlı', '#9a3412', 'rgba(154,52,18,0.12)')}
                </td>}
                {empCols.lastActive && <td data-label="Son aktivlik">{lastActiveBadge(e.lastActiveAtUtc)}</td>}
                {empCols.status && <td data-label="Qeydiyyat">{statusBadge(e.activated)}</td>}
                <td data-label="Əməliyyat">
                  {/* One button, then a ⋯ menu. Six buttons a row wrapped onto two lines, pushed the
                      columns people actually read out of view, and left a red "Sil" one mis-tap from
                      every other action — see RowActions. */}
                  <div style={{ display: 'flex', justifyContent: 'flex-end' }}>
                    <RowActions
                      primary={{ label: 'Redaktə', onClick: () => void startEdit(e.id), disabled: formBusy }}
                      actions={[
                        {
                          label: 'Qeyd. linki',
                          icon: <IconSend />,
                          hidden: e.activated,
                          disabled: linkBusyId === e.id,
                          onClick: () => onReinvite(e),
                          title: 'Qeydiyyat linkini (yenidən) yarat',
                        },
                        {
                          label: 'Davamiyyət',
                          icon: <IconCalendar />,
                          hidden: !e.activated,
                          onClick: () => openAttendance(e),
                          title: 'Giriş/çıxış qeydlərinə bax, düzəlt və ya əlavə et',
                        },
                        {
                          label: 'PIN sıfırla',
                          icon: <IconPhone />,
                          // Never one's own — the server refuses it (it would lock the admin out).
                          hidden: !e.activated || e.id === myId,
                          onClick: () => onResetPin(e),
                          title: 'İşçi PIN-ini unudubsa — müvəqqəti PIN ver',
                        },
                        {
                          label: 'Davamiyyəti sıfırla',
                          icon: <IconRefresh />,
                          hidden: !e.activated,
                          danger: true,
                          disabled: resettingId === e.id,
                          onClick: () => onResetAttendance(e),
                          title: 'Giriş/çıxış tarixçəsini sil — hesab qalır',
                        },
                        {
                          label: 'Sil',
                          icon: <IconTrash />,
                          danger: true,
                          disabled: deletingId === e.id,
                          onClick: () => onDelete(e),
                        },
                      ]}
                    />
                  </div>
                </td>
              </tr>
            ))}
            {rows.length === 0 && (
              <tr>
                <td colSpan={empColCount} className="muted" style={{ textAlign: 'center', padding: 28 }}>
                  {listing
                    ? 'Yüklənir…'
                    : activeFilterCount > 0 || showLeft
                      ? 'Bu axtarış/filial üzrə işçi yoxdur'
                      : 'Hələ işçi yoxdur — “İşçi əlavə et” ilə başlayın'}
                </td>
              </tr>
            )}
          </tbody>
        </table>
        </div>

        {/* Paged at twenty-five, unlike the attendance board, which groups by branch instead. Nine
            hundred names in one run is a scroll, and the question this page answers — «find this
            person», «who has not started» — is answered by the filters above, not by scrolling. */}
        <div className="att-foot">
          <span>
            {total === 0
              ? '0 nəticə'
              : <>
                  <b>{(page - 1) * PAGE_SIZE + 1}–{Math.min(page * PAGE_SIZE, total)}</b>
                  {' / '}{total} nəticə · səhifədə {PAGE_SIZE}
                </>}
          </span>
          {pageCount > 1 && (
            <div className="emp-pager">
              <button
                className="att-step"
                disabled={page <= 1 || listing}
                onClick={() => setPage((p) => Math.max(1, p - 1))}
                aria-label="Əvvəlki səhifə"
              >
                <IconChevronLeft />
              </button>
              <span className="emp-pager-n">{page} / {pageCount}</span>
              <button
                className="att-step"
                disabled={page >= pageCount || listing}
                onClick={() => setPage((p) => Math.min(pageCount, p + 1))}
                aria-label="Növbəti səhifə"
              >
                <IconChevronRight />
              </button>
            </div>
          )}
        </div>
      </div>
    </div>
  )
}

function pill(text: string, color: string, bg: string) {
  return (
    <span
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        gap: 4,
        padding: '2px 9px',
        borderRadius: 999,
        fontSize: 11,
        fontWeight: 700,
        color,
        background: bg,
      }}
    >
      {text}
    </span>
  )
}

function deviceBadge(hasDevice: boolean, deviceLabel: string | null) {
  return hasDevice ? (
    <span style={{ display: 'inline-flex', alignItems: 'center', gap: 4, fontSize: 11, fontWeight: 700, color: '#2e7d32' }}>
      <IconPhone /> {deviceLabel ?? 'Naməlum cihaz'}
    </span>
  ) : (
    pill('Yoxdur', '#9a3412', 'rgba(154,52,18,0.12)')
  )
}

function statusBadge(activated: boolean) {
  return activated
    ? pill('Tamamlandı', '#2e7d32', 'rgba(124,179,66,0.15)')
    : pill('Gözləyir', '#9a6a00', 'rgba(227,150,62,0.16)')
}

// "Son aktivlik" — when the employee last opened the app. Colour by recency so a glance down the
// column shows who's dropped off: green today, amber this week, muted older, clay if never.
function lastActiveBadge(lastActiveAtUtc: string | null) {
  if (!lastActiveAtUtc) return pill('Heç vaxt açmayıb', '#9a3412', 'rgba(154,52,18,0.12)')
  const d = new Date(lastActiveAtUtc)
  const ageMs = Date.now() - d.getTime()
  const day = 24 * 60 * 60 * 1000
  const label = ageMs < day
    ? d.toLocaleTimeString('az-AZ', { hour: '2-digit', minute: '2-digit', timeZone: COMPANY_TZ })
    : d.toLocaleDateString('az-AZ', { day: '2-digit', month: '2-digit', timeZone: COMPANY_TZ }) +
      ' ' + d.toLocaleTimeString('az-AZ', { hour: '2-digit', minute: '2-digit', timeZone: COMPANY_TZ })
  if (ageMs < day) return pill(label, '#2e7d32', 'rgba(124,179,66,0.15)')
  if (ageMs < 7 * day) return pill(label, '#9a6a00', 'rgba(227,150,62,0.16)')
  return <span style={{ fontSize: 11, color: 'var(--c400)', fontFamily: "'IBM Plex Mono',monospace" }}>{label}</span>
}
