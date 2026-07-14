import { useEffect, useState, type FormEvent } from 'react'
import {
  deleteEmployee,
  getAdminLocations,
  getEmployees,
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
} from '../../api/admin'
import {
  adminClearCheckout,
  adminCreateRecord,
  adminUpdateRecord,
  getEmployeeAttendance,
  type AttendanceRecord,
} from '../../api/attendance'
import type { Role } from '../../lib/jwt'
import { StatusBadge } from '../../components/StatusBadge'
import { IconCalendar, IconCheck, IconPhone, IconRefresh, IconSend, IconTrash, IconUsers, IconX } from '../../components/icons'

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
  if (!iso) return ''
  const d = new Date(iso)
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`
}

function fromLocalInputValue(local: string): string | undefined {
  if (!local) return undefined
  return new Date(local).toISOString()
}

const ROLE_LABEL: Record<Role, string> = { Employee: 'İşçi', Manager: 'Menecer', Admin: 'Admin' }

const ERRORS: Record<string, string> = {
  EmailAlreadyExists: 'Bu email artıq mövcuddur',
  PhoneAlreadyExists: 'Bu telefon nömrəsi artıq mövcuddur',
  NeedEmailOrPhone: 'Telefon nömrəsi və ya email lazımdır',
  LocationNotFound: 'Lokasiya tapılmadı',
  EmployeeHasHistory: 'Bu işçinin davamiyyət tarixçəsi var — silmək olmaz, əvəzinə deaktiv edin',
  CannotDeleteSelf: 'Öz hesabınızı silə bilməzsiniz',
  AlreadyActivated: 'İşçi artıq qeydiyyatdan keçib',
  EmployeeNotFound: 'İşçi tapılmadı',
}

type FormState = {
  fullName: string
  fatherName: string
  position: string
  birthYear: string
  email: string
  phoneNumber: string
  locationId: string
  role: Role
  isActive: boolean
  workStart: string
  workEnd: string
}

const EMPTY: FormState = {
  fullName: '',
  fatherName: '',
  position: '',
  birthYear: '',
  email: '',
  phoneNumber: '',
  locationId: '',
  role: 'Employee',
  isActive: true,
  workStart: '',
  workEnd: '',
}

export function EmployeesPage() {
  const [rows, setRows] = useState<AdminEmployee[]>([])
  const [locations, setLocations] = useState<AdminLocation[]>([])
  const [filterLoc, setFilterLoc] = useState<string | null>(null)
  const [showForm, setShowForm] = useState(false)
  const [editingId, setEditingId] = useState<string | null>(null)
  const [form, setForm] = useState<FormState>(EMPTY)
  const [error, setError] = useState<string | null>(null)
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
    const [emp, locs] = await Promise.all([getEmployees(), getAdminLocations()])
    if (emp.status === 200 && Array.isArray(emp.data)) setRows(emp.data)
    if (locs.status === 200 && Array.isArray(locs.data)) setLocations(locs.data)
  }

  useEffect(() => {
    void refresh()
  }, [])

  function set<K extends keyof FormState>(key: K, value: FormState[K]) {
    setForm((f) => ({ ...f, [key]: value }))
  }

  function startAdd() {
    setEditingId(null)
    setForm({ ...EMPTY, locationId: locations[0]?.id ?? '' })
    setError(null)
    setOk(null)
    setLink(null)
    setShowForm(true)
  }

  function startEdit(e: AdminEmployee) {
    setEditingId(e.id)
    setForm({
      fullName: e.fullName,
      fatherName: e.fatherName ?? '',
      position: e.position ?? '',
      birthYear: e.birthYear != null ? String(e.birthYear) : '',
      email: e.email,
      phoneNumber: e.phoneNumber ?? '',
      locationId: e.locationId,
      role: e.role,
      isActive: e.isActive,
      workStart: e.workStart ?? '',
      workEnd: e.workEnd ?? '',
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
    const payload = {
      fullName: form.fullName.trim(),
      email: form.email.trim() || null,
      phoneNumber: form.phoneNumber.trim() || null,
      locationId: form.locationId,
      role: form.role,
      fatherName: form.fatherName.trim() || null,
      position: form.position.trim() || null,
      birthYear: form.birthYear ? Number(form.birthYear) : null,
    }
    const res = editingId
      ? await updateEmployee(editingId, {
          ...payload,
          isActive: form.isActive,
          workStart: form.workStart || null,
          workEnd: form.workEnd || null,
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
      setError('PIN sıfırlanmadı')
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

  const activationLink = link ? `${window.location.origin}/activate?token=${link.result.activationToken}` : ''

  async function copyLink() {
    try {
      await navigator.clipboard.writeText(activationLink)
      setCopied(true)
      setTimeout(() => setCopied(false), 2000)
    } catch {
      window.prompt('Qeydiyyat linki (kopyalayın):', activationLink)
    }
  }

  const visible = filterLoc ? rows.filter((r) => r.locationId === filterLoc) : rows

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
        <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
          <button className="btn" disabled={refBusy} onClick={onResetAllReferences} title="Bütün işçilərin referans (foto audit) şəklini sıfırla — hərə növbəti girişdə yenilənir">
            <IconRefresh /> Referansları sıfırla
          </button>
          <button className="btn btn-primary" onClick={showForm && !editingId ? closeForm : startAdd}>
            <IconUsers /> İşçi əlavə et
          </button>
        </div>
      </div>

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
              <b>{link.name}</b> üçün qeydiyyat linki. İşçiyə göndərin (email/SMS yoxdur — əl ilə paylaşın):
            </span>
          </div>
          <div className="link-box">{activationLink}</div>
          <div style={{ display: 'flex', gap: 8, marginTop: 10 }}>
            <button className="btn btn-primary btn-sm" onClick={copyLink}>
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
      {showForm && (
        <form onSubmit={onSubmit} className="card card-pad" style={{ marginBottom: 16, maxWidth: 760 }}>
          <div style={{ fontWeight: 700, color: 'var(--c900)', marginBottom: 14 }}>
            {editingId ? 'İşçini redaktə et' : 'Yeni işçi'}
          </div>

          <div className="form-row cols2">
            <div>
              <label className="form-label">Ad Soyad</label>
              <input className="inp" required value={form.fullName} onChange={(e) => set('fullName', e.target.value)} />
            </div>
            <div>
              <label className="form-label">Ata adı</label>
              <input className="inp" value={form.fatherName} onChange={(e) => set('fatherName', e.target.value)} />
            </div>
          </div>

          <div className="form-row cols2">
            <div>
              <label className="form-label">Vəzifə</label>
              <input className="inp" value={form.position} onChange={(e) => set('position', e.target.value)} placeholder="məs. Bağban" />
            </div>
            <div>
              <label className="form-label">Təvəllüd ili</label>
              <input className="inp" type="number" min="1940" max="2010" value={form.birthYear} onChange={(e) => set('birthYear', e.target.value)} placeholder="1990" />
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
              <select className="inp" value={form.role} onChange={(e) => set('role', e.target.value as Role)}>
                <option value="Employee">İşçi</option>
                <option value="Manager">Menecer</option>
                <option value="Admin">Admin</option>
              </select>
            </div>
            <div>
              <label className="form-label">Ərazi</label>
              <select className="inp" value={form.locationId} onChange={(e) => set('locationId', e.target.value)}>
                {locations.map((l) => (
                  <option key={l.id} value={l.id}>
                    {l.name}
                  </option>
                ))}
              </select>
            </div>
          </div>

          {editingId && (
            <div className="form-row cols2">
              <div>
                <label className="form-label">Status</label>
                <select className="inp" value={form.isActive ? '1' : '0'} onChange={(e) => set('isActive', e.target.value === '1')}>
                  <option value="1">Aktiv</option>
                  <option value="0">Deaktiv (giriş bağlı)</option>
                </select>
              </div>
            </div>
          )}

          {editingId && (
            <>
              <div className="form-row cols2">
                <div>
                  <label className="form-label">İş başlanğıcı</label>
                  <input className="inp" type="time" value={form.workStart} onChange={(e) => set('workStart', e.target.value)} />
                </div>
                <div>
                  <label className="form-label">İş sonu</label>
                  <input className="inp" type="time" value={form.workEnd} onChange={(e) => set('workEnd', e.target.value)} />
                </div>
              </div>
              <p style={{ fontSize: 12, color: 'var(--c500)', marginTop: -6, marginBottom: 4 }}>
                Boş buraxsanız ərazinin iş saatları tətbiq olunur — gec gəlmə / tez çıxma bu saatlara görə hesablanır.
              </p>
            </>
          )}

          <div style={{ display: 'flex', gap: 8 }}>
            <button type="submit" className="btn btn-primary" disabled={saving || !form.locationId}>
              <IconCheck />
              {saving ? 'Yadda saxlanır…' : editingId ? 'Yadda saxla' : 'Əlavə et və link yarat'}
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

          <div className="tbl-wrap">
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
                        <td className="mono">{r.checkInAtUtc ? new Date(r.checkInAtUtc).toLocaleString('az-AZ') : '—'}</td>
                        <td className="mono">{r.checkOutAtUtc ? new Date(r.checkOutAtUtc).toLocaleString('az-AZ') : '—'}</td>
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
      <div className="tbl-wrap">
        <table>
          <thead>
            <tr>
              <th>Ad, soyad, ata adı</th>
              <th>Vəzifə</th>
              <th>Ərazi</th>
              <th>Rol</th>
              <th>Cihaz</th>
              <th>Qeydiyyat</th>
              <th style={{ textAlign: 'right' }}>Əməliyyat</th>
            </tr>
          </thead>
          <tbody>
            {visible.map((e) => (
              <tr key={e.id} style={{ opacity: e.isActive ? 1 : 0.55 }}>
                <td>
                  <div style={{ fontWeight: 700, color: 'var(--c900)' }}>
                    {e.fullName}
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
                    {(e.fatherName || e.birthYear) &&
                      ` · ${[e.fatherName ? `${e.fatherName} oğlu/qızı` : null, e.birthYear || null].filter(Boolean).join(' · ')}`}
                  </div>
                </td>
                <td>{e.position || '—'}</td>
                <td>{e.locationName ?? '—'}</td>
                <td>{ROLE_LABEL[e.role] ?? e.role}</td>
                <td>{deviceBadge(e.hasDevice, e.deviceLabel)}</td>
                <td>{statusBadge(e.activated)}</td>
                <td>
                  <div style={{ display: 'flex', gap: 8, justifyContent: 'flex-end', flexWrap: 'wrap' }}>
                    {!e.activated && (
                      <button
                        className="btn btn-sm"
                        disabled={linkBusyId === e.id}
                        onClick={() => onReinvite(e)}
                        title="Qeydiyyat linkini (yenidən) yarat"
                      >
                        <IconSend /> Qeyd. linki
                      </button>
                    )}
                    {e.activated && (
                      <>
                        <button
                          className="btn btn-sm"
                          onClick={() => openAttendance(e)}
                          title="Giriş/çıxış qeydlərinə bax, düzəlt və ya əlavə et"
                        >
                          <IconCalendar /> Davamiyyət
                        </button>
                        <button
                          className="btn btn-sm"
                          disabled={resettingId === e.id}
                          onClick={() => onResetAttendance(e)}
                          title="Giriş/çıxış tarixçəsini sil — hesab qalır, yenidən test edin"
                        >
                          <IconRefresh /> Sıfırla
                        </button>
                        <button
                          className="btn btn-sm"
                          onClick={() => onResetPin(e)}
                          title="İşçi PIN-ini unudubsa — müvəqqəti PIN ver"
                        >
                          <IconPhone /> PIN sıfırla
                        </button>
                      </>
                    )}
                    <button className="btn btn-sm" onClick={() => startEdit(e)}>Redaktə</button>
                    <button className="btn btn-danger btn-sm" disabled={deletingId === e.id} onClick={() => onDelete(e)}>
                      <IconTrash /> Sil
                    </button>
                  </div>
                </td>
              </tr>
            ))}
            {visible.length === 0 && (
              <tr>
                <td colSpan={7} className="muted" style={{ textAlign: 'center', padding: 28 }}>
                  {rows.length === 0 ? 'Hələ işçi yoxdur — “İşçi əlavə et” ilə başlayın' : 'Bu ərazidə işçi yoxdur'}
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
