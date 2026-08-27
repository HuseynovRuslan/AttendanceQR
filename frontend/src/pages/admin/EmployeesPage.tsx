import { useEffect, useState, type FormEvent } from 'react'
import { RowActions } from '../../components/RowActions'
import { BulkInvitePage } from './BulkInvitePage'
import { PositionSelect } from '../../components/PositionSelect'
import { NO_CYCLE, type WorkCycleValue } from '../../components/WorkCyclePicker'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { COMPANY_TZ, fmtFullDateTime, fromCompanyInputValue, toCompanyInputValue } from '../../lib/format'
import {
  bulkResetPin,
  type BulkPinResult,
  deleteEmployee,
  getAdminLocations,
  getEmployees,
  getSchedules,
  invite,
  reinviteEmployee,
  resetAllReferencePhotos,
  resetEmployeeAttendance,
  resetPin,
  resetReferencePhoto,
  updateEmployee,
  type AdminEmployee,
  type AdminLocation,
  type InviteResult,
  type Schedule,
} from '../../api/admin'
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
import { IconCalendar, IconCheck, IconKey, IconPhone, IconRefresh, IconSend, IconTrash, IconUsers, IconX } from '../../components/icons'

const ATTENDANCE_ERRORS: Record<string, string> = {
  NothingToUpdate: 'Heç nə dəyişmədi',
  RecordNotFound: 'Qeyd tapılmadı',
  LocationNotFound: 'Lokasiya tapılmadı',
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
  LocationNotFound: 'Lokasiya tapılmadı',
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
  managedLocationIds: [],
  cycle: NO_CYCLE,
  scheduleId: '',
}

/** The form edits Ad + Soyad separately. Prefer the stored parts; for a row not yet backfilled, fall
 *  back to splitting FullName (last token = surname) so the two fields aren't empty on first edit. */
function splitName(first: string | null | undefined, last: string | null | undefined, full: string): { first: string; last: string } {
  if (first || last) return { first: first ?? '', last: last ?? '' }
  const toks = (full ?? '').trim().split(/\s+/).filter(Boolean)
  if (toks.length <= 1) return { first: toks[0] ?? '', last: '' }
  return { first: toks.slice(0, -1).join(' '), last: toks[toks.length - 1] }
}

export function EmployeesPage() {
  const [rows, setRows] = useState<AdminEmployee[]>([])
  const [locations, setLocations] = useState<AdminLocation[]>([])
  const [schedules, setSchedules] = useState<Schedule[]>([])
  const navigate = useNavigate()
  const [filterLoc, setFilterLoc] = useState<string | null>(null)
  // "Bildirişsiz" — show only the people a reminder/announcement can NOT reach, so a manager can go
  // help them switch it on. A workforce that won't self-serve is what keeps reach stuck, and a name
  // list per branch is what actually converts.
  const [onlyNoPush, setOnlyNoPush] = useState(false)
  const [onlyNotStarted, setOnlyNotStarted] = useState(false)
  const [search, setSearch] = useState('')
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
  const [deletingId, setDeletingId] = useState<string | null>(null)
  const [linkBusyId, setLinkBusyId] = useState<string | null>(null)
  const [resettingId, setResettingId] = useState<string | null>(null)
  const [refBusy, setRefBusy] = useState(false)
  const [link, setLink] = useState<{ name: string; result: InviteResult } | null>(null)
  const [copied, setCopied] = useState(false)
  const [pinReset, setPinReset] = useState<{ name: string; pin: string } | null>(null)

  // Attendance-correction panel (view + fix one employee's raw records).
  const [attendanceEmployee, setAttendanceEmployee] = useState<AdminEmployee | null>(null)
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

  async function refresh() {
    const [emp, locs, scheds] = await Promise.all([getEmployees(), getAdminLocations(), getSchedules()])
    if (emp.status === 200 && Array.isArray(emp.data)) setRows(emp.data)
    if (locs.status === 200 && Array.isArray(locs.data)) setLocations(locs.data)
    if (scheds.status === 200 && Array.isArray(scheds.data)) setSchedules(scheds.data)
  }

  useEffect(() => {
    void refresh()
  }, [])

  // Opened from an employee's profile ("Redaktə et" → /admin/employees?edit=<id>): jump straight into
  // that employee's edit form once the list has loaded, then drop the query param.
  const [searchParams, setSearchParams] = useSearchParams()
  useEffect(() => {
    const eid = searchParams.get('edit')
    if (!eid || rows.length === 0) return
    const target = rows.find((r) => r.id === eid)
    if (target) {
      startEdit(target)
      setSearchParams({}, { replace: true })
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [rows, searchParams])

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

  function startEdit(e: AdminEmployee) {
    setEditingId(e.id)
    const parts = splitName(e.firstName, e.lastName, e.fullName)
    setForm({
      firstName: parts.first,
      lastName: parts.last,
      fatherName: e.fatherName ?? '',
      position: e.position ?? '',
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
      birthYear: form.birthYear ? Number(form.birthYear) : null,
      birthDate: form.birthDate || null,
      monthlySalary: form.monthlySalary.trim() ? Number(form.monthlySalary) : null,
      photoExempt: form.photoExempt,
      canFieldCheckIn: form.canFieldCheckIn,
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

  async function onDelete(e: AdminEmployee) {
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
          await refresh()
        } else {
          setError('Silinmədi')
        }
        return
      }
    }

    setDeletingId(null)
    if (status === 200) {
      await refresh()
    } else if (data && typeof data === 'object' && 'error' in data) {
      setError(ERRORS[(data as { error: string }).error] ?? 'Silinmədi')
    } else {
      setError('Silinmədi')
    }
  }

  async function onResetAttendance(e: AdminEmployee) {
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

  async function onReinvite(e: AdminEmployee) {
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

  async function onResetPin(e: AdminEmployee) {
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

  async function onResetReference(e: AdminEmployee) {
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

  async function openAttendance(e: AdminEmployee) {
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

  const [pinList, setPinList] = useState<BulkPinResult | null>(null)
  const [issuing, setIssuing] = useState(false)
  const [pinCopied, setPinCopied] = useState(false)

  async function issuePins(targets: AdminEmployee[]) {
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

  const q = search.trim().toLowerCase()
  const visible = rows.filter((r) => {
    if (filterLoc && r.locationId !== filterLoc) return false
    if (onlyNoPush && r.pushEnabled) return false
    // "Not started" = has never signed in and chosen their own PIN. Two different states mean the
    // same thing to whoever is chasing them: an invite link nobody opened (never activated), and a
    // temporary PIN nobody used (activated at creation, still on it).
    if (onlyNotStarted && r.activated && !r.mustChangePin) return false
    if (q && !`${r.fullName} ${r.phoneNumber ?? ''} ${r.position ?? ''} ${r.id}`.toLowerCase().includes(q)) return false
    return true
  })

  // Reach = the share of employees who can actually be reached, over the branch currently in view.
  // Only active, activated staff count — a deactivated or not-yet-onboarded person needs no reminder,
  // and counting them would understate how well the reachable ones are covered.
  // Onboarding: who is still holding the PIN an admin generated for them, in the branch on screen.
  // These are the people whose PIN list was printed once and, if it was lost, cannot be printed again
  // — only replaced. The count is what makes that offer visible at the moment it is needed.
  const pendingPin = visible.filter((r) => r.isActive && r.activated && r.mustChangePin)

  // Onboarding progress for the branch in view: who is actually using the app, and who has not
  // started. During a rollout this is the number the owner asks for every day, and it was only
  // visible as a side effect of the PIN-reissue strip.
  const onboardPool = rows.filter((r) => r.isActive && (!filterLoc || r.locationId === filterLoc))
  const started = onboardPool.filter((r) => r.activated && !r.mustChangePin).length
  const notStarted = onboardPool.length - started

  const reachPool = rows.filter((r) => r.isActive && r.activated && (!filterLoc || r.locationId === filterLoc))
  const reachOn = reachPool.filter((r) => r.pushEnabled).length
  const reachPct = reachPool.length > 0 ? Math.round((reachOn / reachPool.length) * 100) : 0
  const noPushCount = reachPool.length - reachOn

  return (
    <div>
      {/* toolbar: area filter + add button */}
      <div className="flex items-center justify-between mb-2" style={{ gap: 12, flexWrap: 'wrap' }}>
        <div className="chip-row" style={{ marginBottom: 0 }}>
          <span className={`chip${!filterLoc ? ' active' : ''}`} onClick={() => setFilterLoc(null)}>
            Hamısı
          </span>
          {locations.map((l) => (
            <span
              key={l.id}
              className={`chip${filterLoc === l.id ? ' active' : ''}`}
              onClick={() => setFilterLoc(l.id)}
            >
              {l.name}
            </span>
          ))}
        </div>
        <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', alignItems: 'center' }}>
          <input
            className="inp"
            type="text"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Ad, nömrə, vəzifə üzrə axtar…"
            style={{ width: 'auto', minWidth: 210, padding: '8px 12px' }}
          />
          {search && <button className="btn btn-sm" onClick={() => setSearch('')}>Təmizlə</button>}
          <button className="btn" disabled={refBusy} onClick={onResetAllReferences} title="Bütün işçilərin referans (foto audit) şəklini sıfırla — hərə növbəti girişdə yenilənir">
            <IconRefresh /> Referansları sıfırla
          </button>
          <button className="btn btn-primary" onClick={showForm && !editingId ? closeForm : startAdd}>
            <IconUsers /> İşçi əlavə et
          </button>
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

      {/* Onboarding progress. Always on screen, not only while there is something to fix: during a
          rollout "how many have started" is the question of the week, and it was previously readable
          only as a side effect of the PIN-reissue offer below. */}
      {onboardPool.length > 0 && (
        <div
          className="card"
          style={{ display: 'flex', alignItems: 'center', gap: 14, flexWrap: 'wrap', padding: '10px 16px', marginBottom: 12 }}
        >
          <div style={{ fontWeight: 700 }}>
            {onboardPool.length} işçi
            {filterLoc && <span className="muted" style={{ fontWeight: 500 }}> · bu filialda</span>}
          </div>
          <div style={{ display: 'flex', gap: 14, alignItems: 'center', flexWrap: 'wrap' }}>
            <span style={{ color: 'var(--leaf-d)', fontWeight: 700 }}>
              {started} <span style={{ fontWeight: 500 }}>hesabını aktivləşdirib</span>
            </span>
            <span style={{ color: notStarted > 0 ? 'var(--clay)' : 'var(--c400)', fontWeight: 700 }}>
              {notStarted} <span style={{ fontWeight: 500 }}>hələ aktivləşdirməyib</span>
            </span>
          </div>
          <div style={{ flex: 1, minWidth: 120, height: 8, borderRadius: 999, background: 'rgba(0,0,0,0.07)', overflow: 'hidden' }}>
            <div
              style={{
                width: `${onboardPool.length ? Math.round((started / onboardPool.length) * 100) : 0}%`,
                height: '100%',
                borderRadius: 999,
                background: 'var(--leaf)',
                transition: 'width .4s ease',
              }}
            />
          </div>
          {notStarted > 0 && (
            <button
              className={`btn btn-sm${onlyNotStarted ? ' btn-primary' : ''}`}
              onClick={() => setOnlyNotStarted((v) => !v)}
            >
              {onlyNotStarted ? 'Hamısını göstər' : 'Kimlər olduğunu göstər'}
            </button>
          )}
        </div>
      )}

      {/* Onboarding strip: the people in this branch who have never signed in and are still holding
          the PIN somebody printed for them. It is the moment the list is needed, and the only moment
          it can be produced — so the offer lives here rather than in a menu nobody opens. */}
      {pendingPin.length > 0 && !pinList && (
        <div
          className="card"
          style={{ display: 'flex', alignItems: 'center', gap: 14, flexWrap: 'wrap', padding: '12px 16px', marginBottom: 12 }}
        >
          <div style={{ display: 'flex', alignItems: 'center', gap: 10, minWidth: 0 }}>
            <span style={{ fontSize: 22 }}>🔑</span>
            <div style={{ minWidth: 0 }}>
              <div style={{ fontWeight: 700 }}>{pendingPin.length} nəfər hələ heç vaxt girməyib</div>
              <div className="muted" style={{ fontSize: 12 }}>
                Müvəqqəti PIN-dədirlər. PIN-i itirmisinizsə, yenisini verib siyahını götürə bilərsiniz.
              </div>
            </div>
          </div>
          <div style={{ marginLeft: 'auto' }}>
            <button className="btn btn-sm" disabled={issuing} onClick={() => void issuePins(pendingPin)}>
              {issuing ? 'Verilir…' : 'Yeni PIN siyahısı ver'}
            </button>
          </div>
        </div>
      )}

      {/* Reach strip: how many of the (active, onboarded) staff a reminder/announcement actually
          reaches, for the branch in view — plus a one-tap way to list exactly who is missing so a
          manager can help them turn it on. Hidden until the roster is loaded. */}
      {reachPool.length > 0 && (
        <div
          className="card"
          style={{ display: 'flex', alignItems: 'center', gap: 14, flexWrap: 'wrap', padding: '12px 16px', marginBottom: 12 }}
        >
          <div style={{ display: 'flex', alignItems: 'center', gap: 10, minWidth: 0 }}>
            <span style={{ fontSize: 22 }}>🔔</span>
            <div style={{ minWidth: 0 }}>
              <div style={{ fontWeight: 700 }}>
                Bildiriş {reachOn}/{reachPool.length} işçiyə çatır{' '}
                <span className="muted" style={{ fontWeight: 600 }}>({reachPct}%)</span>
              </div>
              <div className="muted" style={{ fontSize: 12 }}>
                {noPushCount === 0
                  ? 'Hamıya çatır — bütün işçilər bildirişi açıb.'
                  : `${noPushCount} işçiyə növbə xatırlatması və elan çatmır.`}
              </div>
            </div>
          </div>
          {/* Track fill mirrors the percentage. */}
          <div style={{ flex: 1, minWidth: 120, height: 8, borderRadius: 999, background: 'rgba(0,0,0,0.07)', overflow: 'hidden' }}>
            <div
              style={{
                width: `${reachPct}%`,
                height: '100%',
                borderRadius: 999,
                background: reachPct >= 80 ? '#2e7d32' : reachPct >= 50 ? '#c98a00' : '#c0392b',
                transition: 'width .4s ease',
              }}
            />
          </div>
          {noPushCount > 0 && (
            <button
              className={`btn btn-sm${onlyNoPush ? ' btn-primary' : ''}`}
              onClick={() => setOnlyNoPush((v) => !v)}
            >
              {onlyNoPush ? 'Hamısını göstər' : `Bildirişsiz (${noPushCount})`}
            </button>
          )}
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
                Bu işçidə skan zamanı kamera açılmır. Lokasiya və cihaz yoxlaması qüvvədə qalır.
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
      <div className="tbl-wrap tbl-cards">
        <table>
          <thead>
            <tr>
              <th>Ad, soyad, ata adı</th>
              <th>Vəzifə</th>
              <th>Filial</th>
              <th>Rol</th>
              <th>Cihaz</th>
              <th>Bildiriş</th>
              <th>Son aktivlik</th>
              <th>Qeydiyyat</th>
              <th style={{ textAlign: 'right' }}>Əməliyyat</th>
            </tr>
          </thead>
          <tbody>
            {visible.map((e) => (
              <tr key={e.id} style={{ opacity: e.isActive ? 1 : 0.55 }}>
                <td data-label="İşçi">
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
                  <div style={{ fontSize: 12, color: 'var(--c400)' }}>
                    {e.phoneNumber ? (
                      <>📞 0{e.phoneNumber}</>
                    ) : (
                      <span style={{ color: '#b45309', fontWeight: 600 }}>nömrə yoxdur</span>
                    )}
                    {(e.birthDate || e.birthYear) &&
                      // Father name now rides with the full name above; the meta keeps only birth date.
                      ` · ${e.birthDate ? e.birthDate.split('-').reverse().join('.') : e.birthYear}`}
                  </div>
                  <div style={{ fontSize: 11, color: 'var(--c400)', fontFamily: "'IBM Plex Mono',monospace", marginTop: 2 }}>
                    ID: {e.id.slice(0, 8)}
                  </div>
                </td>
                <td data-label="Vəzifə">{e.position || '—'}</td>
                <td data-label="Filial">
                  {e.locationName ?? '—'}
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
                </td>
                <td data-label="Rol">
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
                </td>
                <td>{deviceBadge(e.hasDevice, e.deviceLabel)}</td>
                <td data-label="Bildiriş">
                  {/* Whether an announcement/reminder actually reaches this person's phone. */}
                  {e.pushEnabled
                    ? pill('Açıq', '#2e7d32', 'rgba(124,179,66,0.15)')
                    : pill('Bağlı', '#9a3412', 'rgba(154,52,18,0.12)')}
                </td>
                <td>{lastActiveBadge(e.lastActiveAtUtc)}</td>
                <td>{statusBadge(e.activated)}</td>
                <td>
                  {/* One button, then a ⋯ menu. Six buttons a row wrapped onto two lines, pushed the
                      columns people actually read out of view, and left a red "Sil" one mis-tap from
                      every other action — see RowActions. */}
                  <div style={{ display: 'flex', justifyContent: 'flex-end' }}>
                    <RowActions
                      primary={{ label: 'Redaktə', onClick: () => startEdit(e) }}
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
                          hidden: !e.activated,
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
            {visible.length === 0 && (
              <tr>
                <td colSpan={7} className="muted" style={{ textAlign: 'center', padding: 28 }}>
                  {rows.length === 0 ? 'Hələ işçi yoxdur — “İşçi əlavə et” ilə başlayın' : 'Bu axtarış/filial üzrə işçi yoxdur'}
                </td>
              </tr>
            )}
          </tbody>
        </table>
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
