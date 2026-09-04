import { useEffect, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import {
  getEmployees,
  getSummary,
  getEmployeeDays,
  getDeviceBindings,
  revokeDeviceBinding,
  resetPin,
  reinviteEmployee,
  updateEmployee,
  type AdminEmployee,
  type EmployeeReportRow,
  type EmployeeDay,
  type DeviceBinding,
  type InviteResult,
} from '../../api/admin'
import { getEmployeeAttendance, getPhotoUrl, adminUpdateRecord, adminCreateRecord, adminClearCheckout, adminDeleteRecord } from '../../api/attendance'
import type { AttendanceRecord, PhotoUrlResponse } from '../../api/attendance'
import { PhotoCompareModal } from '../../components/PhotoCompareModal'
import { RecordBadge, leaveVisual } from '../../components/StatusBadge'
import { initials } from '../../lib/att'
import { fmtDate, fmtDuration, fmtTime, fromCompanyInputValue, toCompanyInputValue } from '../../lib/format'
import { IconCamera, IconCheck, IconLaptop, IconPhone, IconX } from '../../components/icons'
import { useAuth } from '../../auth/AuthContext'
import { getManagerEmployee, resetManagerEmployeePin, updateManagerEmployee } from '../../api/manager'
import { getEquipmentByEmployee, type EquipmentRecord } from '../../api/equipment'

const ROLE_LABEL: Record<string, string> = { Admin: 'Admin', Manager: 'Filial meneceri', Employee: 'İşçi' }

/** One employee's full profile: identity + this-month summary + recent attendance + photos + devices,
 * with the key actions (edit, PIN reset, activate/deactivate, invite link) in one place. All data comes
 * from existing admin endpoints — no new backend. Reached at /admin/employees/:id. */
export function EmployeeProfilePage() {
  // Role decides which API this screen talks to, never what it hides. A manager's data comes from
  // /api/manager/*, which cannot carry salary or role at all.
  const { role } = useAuth()
  const isManager = role === 'Manager'
  /**
   * Whether this caller may ACT on the person whose card is open.
   *
   * Opening a card and changing it are two different boundaries. A manager may open anyone at their
   * branches — a peer, the admin who clocks in there — because a name on a board should never be dead
   * text. They may change only their own plain staff. Without this the card offered PIN reset and
   * deactivate on an admin, and the refusal arrived as a 403 the person reads as a broken button.
   */
  const [manageable, setManageable] = useState(true)
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()

  const [emp, setEmp] = useState<AdminEmployee | null>(null)
  const [records, setRecords] = useState<AttendanceRecord[]>([])
  const [summary, setSummary] = useState<EmployeeReportRow | null>(null)
  // Day-by-day rows behind the "Bu ay" tiles, and which tile is currently expanded.
  const [monthDays, setMonthDays] = useState<EmployeeDay[]>([])
  const [openMetric, setOpenMetric] = useState<string | null>(null)
  const [devices, setDevices] = useState<DeviceBinding[]>([])
  const [equipment, setEquipment] = useState<EquipmentRecord[]>([])
  const [loading, setLoading] = useState(true)
  const [notFound, setNotFound] = useState(false)
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState<string | null>(null)
  const [pin, setPin] = useState<string | null>(null)
  const [invite, setInvite] = useState<InviteResult | null>(null)
  // Attendance correction, in place — so a wrong day is fixed from the profile, not by hopping back
  // to the employees list. Same admin endpoints the list used.
  // Per-record selfie viewer — "Şəklə bax" on a row opens that day's check-in photo (vs the
  // reference) in the same modal the today board and photo audit use.
  const [photoModal, setPhotoModal] = useState<{ title: string; photo: PhotoUrlResponse } | null>(null)
  const [photoBusyId, setPhotoBusyId] = useState<string | null>(null)
  const [editRecId, setEditRecId] = useState<string | null>(null)
  const [editIn, setEditIn] = useState('')
  const [editOut, setEditOut] = useState('')
  const [showCreate, setShowCreate] = useState(false)
  const [crDate, setCrDate] = useState('')
  const [crIn, setCrIn] = useState('')
  const [crOut, setCrOut] = useState('')
  const [recBusy, setRecBusy] = useState(false)
  const [recErr, setRecErr] = useState<string | null>(null)

  useEffect(() => {
    void load()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [id])

  async function load() {
    if (!id) return
    setLoading(true)
    setErr(null)
    const now = new Date()
    const monthStart = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-01`
    const today = now.toISOString().slice(0, 10)

    // A manager loads ONE person from their own endpoint. The admin roster is not merely bigger — it
    // is unscoped and carries monthlySalary, so it must never be the thing a manager's screen fetches
    // and filters client-side.
    const [empRes, attRes, devRes, sumRes, daysRes, equipRes] = await Promise.all([
      isManager ? getManagerEmployee(id) : getEmployees(),
      getEmployeeAttendance(id),
      getDeviceBindings(),
      getSummary(monthStart, today),
      getEmployeeDays(id, monthStart, today),
      // The equipment register is an Admin screen and its endpoint is [Authorize(Roles = "Admin")],
      // so asking as a manager would be a 403 on every profile they open — for a section whose only
      // control links to a page they cannot reach.
      isManager ? Promise.resolve(null) : getEquipmentByEmployee(id),
    ])
    if (daysRes.status === 200 && Array.isArray(daysRes.data)) setMonthDays(daysRes.data)

    const found = empRes.status !== 200 || !empRes.data || 'error' in empRes.data
      ? null
      : Array.isArray(empRes.data)
        ? empRes.data.find((e) => e.id === id) ?? null
        // The manager row is the same shape minus salary and role — which is the point, so the cast
        // goes through unknown rather than pretending the two types overlap. The fields this screen
        // reads (identity, hours, shift, flags) are present on both; salary it never displays, and
        // role only decorates a badge.
        : (empRes.data as unknown as AdminEmployee)
    setEmp(found)
    // The manager endpoint says so outright; an admin may act on anyone.
    setManageable(!isManager || (found != null && (found as unknown as { manageable?: boolean }).manageable === true))
    if (!found) {
      setNotFound(true)
      setLoading(false)
      return
    }
    const recs = attRes.status === 200 && Array.isArray(attRes.data) ? attRes.data : []
    setRecords(recs)
    if (devRes.status === 200 && Array.isArray(devRes.data)) setDevices(devRes.data.filter((d) => d.employeeId === id))
    if (equipRes?.status === 200 && Array.isArray(equipRes.data)) setEquipment(equipRes.data)
    if (sumRes.status === 200 && sumRes.data && 'rows' in sumRes.data)
      setSummary(sumRes.data.rows.find((r) => r.employeeId === id) ?? null)

    // No photo prefetch: nothing on this page shows a selfie until someone asks for a specific
    // flagged day, so the presigned URL is fetched then (viewRecordPhoto), not on every open.
    setLoading(false)
  }

  async function onResetPin() {
    if (!emp) return
    setBusy(true)
    setErr(null)
    // The manager twin re-checks the branch and the target's role; the admin one 403s for them
    // outright, so an unbranched call made the button look broken rather than refused.
    const r = isManager ? await resetManagerEmployeePin(emp.id) : await resetPin(emp.id)
    setBusy(false)
    if (r.status === 200 && r.data && 'tempPin' in r.data) setPin(r.data.tempPin)
    else setErr('PIN sıfırlanmadı')
  }

  async function onReinvite() {
    if (!emp) return
    setBusy(true)
    setErr(null)
    const r = await reinviteEmployee(emp.id)
    setBusy(false)
    if (r.status === 200 && r.data && 'activationUrl' in r.data) setInvite(r.data)
    else setErr('Link yaradılmadı')
  }

  async function onToggleActive() {
    if (!emp) return
    if (!window.confirm(emp.isActive ? `"${emp.fullName}" deaktiv edilsin? Girişi bağlanacaq.` : `"${emp.fullName}" yenidən aktiv edilsin?`))
      return
    setBusy(true)
    setErr(null)
    // THE hazard on this screen. EmployeeUpdateRequest null-defaults every field, so this re-sends
    // the whole employee to change one flag — including monthlySalary. A manager's `emp` never
    // carries salary (their endpoint does not send it), so routing their save to the admin PUT would
    // wipe the salary of every person they toggled, silently, one at a time. The manager endpoint
    // has no salary field at all, which is why the branch is here and not a hidden input.
    const r = isManager
      ? await updateManagerEmployee(emp.id, {
          fullName: emp.fullName,
          firstName: emp.firstName,
          lastName: emp.lastName,
          fatherName: emp.fatherName,
          position: emp.position,
          phoneNumber: emp.phoneNumber,
          email: emp.email || null,
          locationId: emp.locationId,
          birthDate: emp.birthDate ?? null,
          birthYear: emp.birthYear,
          workStart: emp.workStart ?? null,
          workEnd: emp.workEnd ?? null,
          photoExempt: emp.photoExempt === true,
          canFieldCheckIn: emp.canFieldCheckIn === true,
          scheduleId: emp.scheduleId ?? null,
          workCycleDays: emp.workCycleDays ?? null,
          workCycleOnDays: emp.workCycleOnDays ?? null,
          workCycleAnchor: emp.workCycleAnchor ?? null,
          isActive: !emp.isActive,
        })
      : await updateEmployee(emp.id, {
      fullName: emp.fullName,
      // Re-sent like the rest — an omitted part would blank it and the backend would recompose the
      // name from the remaining fields.
      firstName: emp.firstName,
      lastName: emp.lastName,
      email: emp.email || null,
      phoneNumber: emp.phoneNumber,
      locationId: emp.locationId,
      role: emp.role,
      fatherName: emp.fatherName,
      position: emp.position,
      birthYear: emp.birthYear,
      // Re-send these too, or this partial update would blank them (the request defaults them to null).
      birthDate: emp.birthDate ?? null,
      monthlySalary: emp.monthlySalary ?? null,
      photoExempt: emp.photoExempt === true,
      canFieldCheckIn: emp.canFieldCheckIn === true,
      canShareDevice: emp.canShareDevice === true,
      workStart: emp.workStart ?? null,
      workEnd: emp.workEnd ?? null,
      // Re-sent for the same reason as the rest: an omitted field is nulled, so toggling active
      // would silently take this employee off their shift.
      scheduleId: emp.scheduleId ?? null,
      workCycleDays: emp.workCycleDays ?? null,
      workCycleOnDays: emp.workCycleOnDays ?? null,
      workCycleAnchor: emp.workCycleAnchor ?? null,
      isActive: !emp.isActive,
        })
    setBusy(false)
    if (r.status === 200) void load()
    else setErr('Status dəyişmədi')
  }

  async function onRevokeDevice(devId: string) {
    if (!window.confirm('Bu cihaz ləğv edilsin? Növbəti skan işləməyəcək.')) return
    setBusy(true)
    await revokeDeviceBinding(devId)
    setBusy(false)
    void load()
  }

  // --- attendance correction (record edit / create / clear checkout) ----------
  const toInput = (iso: string | null) => {
    // Company timezone in both directions — see toCompanyInputValue.
    return iso ? toCompanyInputValue(iso) : ''
  }
  const fromInput = (local: string) => (local ? fromCompanyInputValue(local) : undefined)

  function startEditRecord(r: AttendanceRecord) {
    setEditRecId(r.recordId)
    setEditIn(toInput(r.checkInAtUtc))
    setEditOut(toInput(r.checkOutAtUtc))
    setRecErr(null)
  }

  async function saveEditRecord() {
    if (!editRecId) return
    setRecBusy(true); setRecErr(null)
    const { status } = await adminUpdateRecord(editRecId, fromInput(editIn), fromInput(editOut))
    setRecBusy(false)
    if (status === 200) { setEditRecId(null); void load() }
    else setRecErr('Qeyd dəyişmədi')
  }

  async function clearCheckout(recordId: string) {
    if (!window.confirm('Çıxış qeydi silinsin? İşçi yenidən çıxış edə biləcək.')) return
    setRecBusy(true); setRecErr(null)
    const { status } = await adminClearCheckout(recordId)
    setRecBusy(false)
    if (status === 200) void load()
    else setRecErr('Çıxış silinmədi')
  }

  /**
   * Removes a whole day, not just its times.
   *
   * Only for a row that records nothing real — the classic one is a night worker's morning double
   * scan, which opens a day they were asleep for and closes it seven minutes later. That row then
   * refuses their next scan ("gün artıq bağlanıb"), and editing cannot clear it: any times on that
   * date keep the refusal. The confirm spells out that it cannot be undone, because it cannot.
   */
  async function deleteRecord(r: AttendanceRecord) {
    const when = `${fmtDate(r.attendanceDate)} ${fmtTime(r.checkInAtUtc)}${r.checkOutAtUtc ? `–${fmtTime(r.checkOutAtUtc)}` : ''}`
    if (!window.confirm(
      `${when} qeydi tamamilə silinsin?

` +
      'Geri qaytarmaq mümkün deyil. Giriş şəkli də silinir. ' +
      'Yalnız heç bir işi əks etdirməyən səhv qeydlər üçün istifadə edin — ' +
      'saatı düzəltmək lazımdırsa «Redaktə» edin.')) return
    setRecBusy(true); setRecErr(null)
    const { status } = await adminDeleteRecord(r.recordId)
    setRecBusy(false)
    if (status === 200) void load()
    else setRecErr('Qeyd silinmədi')
  }

  async function viewRecordPhoto(r: AttendanceRecord) {
    setPhotoBusyId(r.recordId)
    setRecErr(null)
    // URLs are short-lived — fetch fresh each open, never cache.
    const { status, data } = await getPhotoUrl(r.recordId)
    setPhotoBusyId(null)
    if (status === 200 && data && 'hasPhoto' in data) {
      if (!data.checkInPhotoUrl) { setRecErr('Bu gün üçün şəkil yoxdur'); return }
      setPhotoModal({ title: `${emp?.fullName ?? ''} · ${fmtDate(r.attendanceDate)}`, photo: data })
    } else {
      setRecErr('Şəkil açılmadı')
    }
  }

  async function createRecord() {
    if (!emp || !crDate || !crIn) { setRecErr('Tarix və giriş vaxtı seçin'); return }
    const checkIn = fromInput(`${crDate}T${crIn}`)
    const checkOut = crOut ? fromInput(`${crDate}T${crOut}`) : undefined
    if (!checkIn) { setRecErr('Giriş vaxtı düzgün deyil'); return }
    setRecBusy(true); setRecErr(null)
    const { status, data } = await adminCreateRecord(emp.id, crDate, checkIn, checkOut)
    setRecBusy(false)
    if (status === 200) {
      setShowCreate(false); setCrDate(''); setCrIn(''); setCrOut('')
      void load()
    } else {
      const code = data && typeof data === 'object' && 'error' in data ? (data as { error: string }).error : ''
      setRecErr(code === 'RecordAlreadyExists' ? 'Bu gün üçün artıq qeyd var' : 'Qeyd yaradılmadı')
    }
  }

  if (loading) return <p className="muted" style={{ padding: 24 }}>Yüklənir…</p>
  if (notFound || !emp)
    return (
      <div style={{ padding: 24 }}>
        <p className="muted">İşçi tapılmadı.</p>
        <Link to="/admin/employees" className="btn" style={{ marginTop: 12 }}>← İşçilər</Link>
      </div>
    )

  const recent = [...records].sort((a, b) => (a.attendanceDate < b.attendanceDate ? 1 : -1)).slice(0, 12)

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
      <Link to="/admin/employees" className="btn btn-sm" style={{ alignSelf: 'flex-start' }}>← İşçilər</Link>

      {/* Header */}
      <div className="card card-pad">
        <div style={{ display: 'flex', gap: 16, alignItems: 'center', flexWrap: 'wrap' }}>
          <div style={{ width: 56, height: 56, borderRadius: '50%', background: 'var(--leaf-bg)', color: 'var(--leaf-d)', display: 'flex', alignItems: 'center', justifyContent: 'center', fontWeight: 800, fontSize: 20, flexShrink: 0 }}>
            {initials(emp.fullName)}
          </div>
          <div style={{ minWidth: 0, flex: 1 }}>
            <div style={{ fontFamily: 'Manrope,sans-serif', fontWeight: 800, fontSize: 20, color: 'var(--c900)' }}>
              {emp.fullName}{emp.fatherName ? ` ${emp.fatherName}` : ''}
            </div>
            <div className="muted" style={{ fontSize: 13, marginTop: 2 }}>
              {[emp.position, emp.locationName].filter(Boolean).join(' · ') || '—'}
            </div>
            <div className="muted" style={{ fontSize: 11, fontFamily: "'IBM Plex Mono',monospace", marginTop: 2 }}>
              ID: {emp.id.slice(0, 8)}
            </div>
          </div>
          <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
            <span className="tag">{ROLE_LABEL[emp.role] ?? emp.role}</span>
            <span className={`badge ${emp.isActive ? 'b-present' : 'b-absent'}`}>{emp.isActive ? 'Aktiv' : 'Deaktiv'}</span>
            {!emp.activated && <span className="badge b-late">Aktivləşməyib</span>}
            <span className={`badge ${emp.hasDevice ? 'b-device' : 'b-nodevice'}`}>{emp.hasDevice ? 'Cihaz bağlı' : 'Cihaz yox'}</span>
          </div>
        </div>

        {/* Actions. Every one of these is refused server-side for a card this caller may not act on —
            a manager opening a peer's or their branch admin's card. Showing them anyway would turn a
            deliberate boundary into what looks like a broken button. */}
        <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', marginTop: 16 }}>
          {!manageable && (
            <span className="muted" style={{ fontSize: 13, alignSelf: 'center' }}>
              Bu hesab sizin idarənizdə deyil — yalnız baxa bilərsiniz.
            </span>
          )}
          {/* A manager edits from their OWN roster; the admin one is not theirs to open, and
              sending them there landed on a page that renders its 403 as "no employees". */}
          {manageable && (
            <button
              className="btn btn-primary btn-sm"
              onClick={() => navigate(`/admin/${isManager ? 'my-employees' : 'employees'}?edit=${emp.id}`)}
            >
              Redaktə et
            </button>
          )}
          {manageable && (
            <button className="btn btn-sm" disabled={busy || !emp.activated} onClick={() => void onResetPin()}>PIN sıfırla</button>
          )}
          {/* Re-invite mints an activation link and has no manager endpoint; offering it would
              only ever 403. A manager reissues a temporary PIN instead, which they can do. */}
          {!emp.activated && !isManager && (
            <button className="btn btn-sm" disabled={busy} onClick={() => void onReinvite()}>Dəvət linki</button>
          )}
          {manageable && (
            <button className={`btn btn-sm ${emp.isActive ? 'btn-danger' : ''}`} disabled={busy} onClick={() => void onToggleActive()}>
              {emp.isActive ? 'Deaktiv et' : 'Aktiv et'}
            </button>
          )}
        </div>

        {err && <div className="fb fb-err" style={{ marginTop: 12 }}><IconX /><span>{err}</span></div>}
        {pin && (
          <div className="fb fb-ok" style={{ marginTop: 12 }}>
            <IconCheck /><span>Yeni müvəqqəti PIN: <b style={{ fontFamily: 'IBM Plex Mono,monospace' }}>{pin}</b> — işçiyə verin, ilk girişdə dəyişəcək.</span>
          </div>
        )}
        {invite && (
          <div className="fb fb-info" style={{ marginTop: 12 }}>
            <span>Aktivləşdirmə linki: <span className="link-box" style={{ display: 'inline-block', marginTop: 6 }}>{invite.activationUrl}</span></span>
          </div>
        )}
      </div>

      {/* This month */}
      <div className="card card-pad">
        <div className="card-title">Bu ay</div>
        {summary ? (
          <>
            <div className="stat-grid" style={{ marginBottom: 0 }}>
              <Stat label="İş günü" value={summary.workDays} metric="workDays" open={openMetric} onOpen={setOpenMetric} />
              <Stat label="Saat" value={summary.totalWorkedHours.toFixed(1)} metric="hours" open={openMetric} onOpen={setOpenMetric} />
              <Stat label="Gecikmə" value={summary.lateCount} metric="late" open={openMetric} onOpen={setOpenMetric} />
              <Stat label="Qayıb" value={summary.absentDays} metric="absent" open={openMetric} onOpen={setOpenMetric} />
              <Stat label="Natamam" value={summary.incompleteDays} metric="incomplete" open={openMetric} onOpen={setOpenMetric} />
              {/* «Məzuniyyət» alone now — sick has its own tile. The combined tile disagreed with the
                  list that opens beneath it, which names each day by its real type. */}
              <Stat label="Məzuniyyət" value={summary.vacationDays ?? summary.leaveDays} metric="leave" open={openMetric} onOpen={setOpenMetric} />
              {(summary.sickDays ?? 0) > 0 && (
                <Stat label="Xəstəlik" value={summary.sickDays ?? 0} metric="sick" open={openMetric} onOpen={setOpenMetric} />
              )}
              {(summary.unpaidDays ?? 0) > 0 && (
                <Stat label="Ödənişsiz" value={summary.unpaidDays ?? 0} metric="unpaid" open={openMetric} onOpen={setOpenMetric} />
              )}
              <Stat label="İcazə" value={summary.permissionDays} metric="permission" open={openMetric} onOpen={setOpenMetric} />
              {/* Apart from leave: on Ezamiyyət the person WAS working, away from a poster.
                  Folded into the leave figure, a driver on a month-long route read as a month
                  of annual holiday on their own card. Hidden at zero — most people never have one. */}
              {summary.tripDays > 0 && (
                <Stat label="Ezamiyyət" value={summary.tripDays} metric="trip" open={openMetric} onOpen={setOpenMetric} />
              )}
            </div>
            {openMetric && <MetricBreakdown metric={openMetric} days={monthDays} onClose={() => setOpenMetric(null)} />}
          </>
        ) : (
          <p className="muted" style={{ fontSize: 13 }}>Bu ay üçün məlumat yoxdur.</p>
        )}
      </div>

      {/* Personal */}
      <div className="card card-pad">
        <div className="card-title">Şəxsi məlumat</div>
        <div className="form-row cols2" style={{ marginBottom: 0 }}>
          <Field label="Ata adı" value={emp.fatherName} />
          <Field label="Doğum tarixi" value={emp.birthDate ? emp.birthDate.split('-').reverse().join('.') : emp.birthYear ? String(emp.birthYear) : null} />
          <Field label="Telefon" value={emp.phoneNumber} />
          <Field label="Email" value={emp.email} />
          <Field label="İş saatı" value={emp.workStart && emp.workEnd ? `${emp.workStart} – ${emp.workEnd}` : 'Filialın saatı'} />
          <Field label="Qeydiyyat" value={emp.createdAtUtc ? fmtDate(emp.createdAtUtc.slice(0, 10)) : null} />
        </div>
      </div>

      {/* No standing photo card here on purpose: a selfie is opened only where there is a REASON to
          look — a face-flagged check-in (the "Şəklə bax" button below) or the Foto audit screen.
          A profile that greets whoever opens it with the person's face turns an anti-fraud record
          into casual browsing of staff photos. */}

      {/* Recent attendance — with in-place correction, so a wrong day is fixed here, not from the list. */}
      <div className="card card-pad">
        <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 10 }}>
          <div className="card-title" style={{ margin: 0 }}>Son davamiyyət</div>
          {/* Creating a day from nothing stayed Admin-only when the controller opened to managers —
              correcting a record that exists is a different power from inventing one. Offering the
              button anyway would only ever 403. A manager corrects the rows below instead. */}
          {!isManager && (
            <button className="btn btn-sm" style={{ marginLeft: 'auto' }} onClick={() => { setShowCreate((v) => !v); setRecErr(null) }}>
              + Qeyd əlavə et
            </button>
          )}
        </div>

        {recErr && <div className="fb fb-err" style={{ marginBottom: 10 }}><IconX /><span>{recErr}</span></div>}

        {showCreate && (
          <div className="card card-pad" style={{ marginBottom: 12, background: 'var(--c50)' }}>
            <div className="form-row cols2">
              <div><label className="form-label">Tarix</label><input className="inp" type="date" value={crDate} onChange={(e) => setCrDate(e.target.value)} /></div>
              <div><label className="form-label">Giriş</label><input className="inp" type="time" value={crIn} onChange={(e) => setCrIn(e.target.value)} /></div>
            </div>
            <div style={{ maxWidth: 200, marginBottom: 12 }}>
              <label className="form-label">Çıxış (istəyə bağlı)</label>
              <input className="inp" type="time" value={crOut} onChange={(e) => setCrOut(e.target.value)} />
            </div>
            <div style={{ display: 'flex', gap: 8 }}>
              <button className="btn btn-primary btn-sm" disabled={recBusy} onClick={() => void createRecord()}>{recBusy ? 'Yaradılır…' : 'Yarat'}</button>
              <button className="btn btn-sm" onClick={() => setShowCreate(false)}>Ləğv et</button>
            </div>
          </div>
        )}

        {recent.length === 0 ? (
          <p className="muted" style={{ fontSize: 13 }}>Qeyd yoxdur.</p>
        ) : (
          <div className="tbl-wrap tbl-cards">
            <table>
              <thead>
                <tr><th>Tarix</th><th>Status</th><th>Giriş</th><th>Çıxış</th><th>Müddət</th><th></th></tr>
              </thead>
              <tbody>
                {recent.map((r) => (
                  // The edit row is matched on the record id — and a «səyyar» day HAS no record, so
                  // its id is empty and every such row matched at once: opening the editor on one
                  // opened it on all of them, showing the same times in each, and saving PUT to an
                  // empty guid and answered «Qeyd dəyişmədi». The isFieldDay guard is what makes the
                  // comparison meaningful; the key below stops React reusing rows for the same reason.
                  !r.isFieldDay && editRecId === r.recordId ? (
                    <tr key={r.recordId}>
                      <td data-label="Tarix">{fmtDate(r.attendanceDate)}</td>
                      <td colSpan={3} data-label="Düzəliş">
                        <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', alignItems: 'center' }}>
                          <input className="inp" style={{ width: 200 }} type="datetime-local" value={editIn} onChange={(e) => setEditIn(e.target.value)} />
                          <input className="inp" style={{ width: 200 }} type="datetime-local" value={editOut} onChange={(e) => setEditOut(e.target.value)} />
                        </div>
                      </td>
                      <td></td>
                      <td>
                        <div style={{ display: 'flex', gap: 6, justifyContent: 'flex-end' }}>
                          <button className="btn btn-primary btn-sm" disabled={recBusy} onClick={() => void saveEditRecord()}>Yadda saxla</button>
                          <button className="btn btn-sm" onClick={() => setEditRecId(null)}>Ləğv</button>
                        </div>
                      </td>
                    </tr>
                  ) : (
                    <tr key={r.isFieldDay ? `f:${r.attendanceDate}` : r.recordId}>
                      <td data-label="Tarix">
                        {fmtDate(r.attendanceDate)}
                        {/* No poster scan at all — the times come from a field visit, and there is no
                            AttendanceRecord here to edit, close or delete. Said on the row, with
                            where to go instead, because the buttons being absent explains nothing. */}
                        {r.isFieldDay && (
                          <div style={{ fontSize: 11, marginTop: 2, color: 'var(--leaf-d)' }}>
                            📍 Səyyar iş günü — «Sahə ziyarətləri»ndən redaktə olunur
                          </div>
                        )}
                        {r.manualByName && (
                          <div style={{ fontSize: 11, marginTop: 2, color: 'var(--amber)' }}>Əl ilə daxil edilib · {r.manualByName}</div>
                        )}
                        {r.closedByFieldVisit && (
                          <div style={{ fontSize: 11, marginTop: 2, color: 'var(--c600)' }}>📍 Ərazi çıxışı ilə bağlandı</div>
                        )}
                      </td>
                      <td data-label="Status"><RecordBadge r={r} /></td>
                      <td className="mono" data-label="Giriş">{fmtTime(r.checkInAtUtc)}</td>
                      <td className="mono" data-label="Çıxış">{fmtTime(r.checkOutAtUtc)}</td>
                      <td className="mono" data-label="Müddət">{r.checkInAtUtc && r.checkOutAtUtc ? fmtDuration(r.checkInAtUtc, r.checkOutAtUtc) : '—'}</td>
                      <td>
                        <div style={{ display: 'flex', gap: 6, justifyContent: 'flex-end' }}>
                          {/* Menecer də baxa bilir (2026-09-04) — amma yalnız idarə etdiyi adamların:
                              server `LocationScopeRules` ilə əhatəni yoxlayır. Səyyar gündə düymə
                              yoxdur, çünki şəkil qeydə bağlıdır və belə gündə qeyd yoxdur. */}
                          {r.checkInAtUtc && !r.isFieldDay && (
                            <button className="btn btn-sm" disabled={photoBusyId === r.recordId} onClick={() => void viewRecordPhoto(r)}>
                              <IconCamera /> {photoBusyId === r.recordId ? '…' : 'Şəklə bax'}
                            </button>
                          )}
                          {/* Every control here addresses an AttendanceRecord by id. A field day has
                              none — the id is empty — so these would 404 rather than do nothing
                              visible, which is exactly what happened: a staff member typed the right
                              times into the wrong screen and got «Qeyd dəyişmədi» four times. */}
                          {!r.isFieldDay && (
                            <>
                              <button className="btn btn-sm" onClick={() => startEditRecord(r)}>Redaktə</button>
                              {r.checkOutAtUtc && <button className="btn btn-sm" onClick={() => void clearCheckout(r.recordId)}>Çıxışı sil</button>}
                              {/* Last, and worded as the whole day: next to "Çıxışı sil" it must not
                                  read as another way of clearing a time. */}
                              <button className="btn btn-sm btn-danger" disabled={recBusy}
                                      onClick={() => void deleteRecord(r)}>Günü sil</button>
                            </>
                          )}
                        </div>
                      </td>
                    </tr>
                  )
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {/* This person's line in the IT equipment register. Read-only here — the register is edited
          in one place, where the whole list is in view. Shows nothing unless the register's name was
          linked to this staff record.
          Admin only, like the register itself: a manager would get an empty card whose one button
          leads somewhere they cannot open. */}
      {!isManager && (
      <div className="card card-pad">
        <div className="card-title" style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
          <span>Kompüter avadanlığı</span>
          <Link to="/admin/equipment" className="btn btn-sm"><IconLaptop /> Kompüter inventarizasiyası</Link>
        </div>
        {equipment.length === 0 ? (
          <p className="muted" style={{ fontSize: 13 }}>
            İnventarizasiya siyahısında bu işçiyə bağlı sətir yoxdur.
          </p>
        ) : (
          <div className="tbl-wrap">
            <table>
              <thead>
                <tr><th>Avadanlıq</th><th>Sistem bloku</th><th>Monitor</th><th>Digər avadanlıq</th></tr>
              </thead>
              <tbody>
                {equipment.map((r) => (
                  <tr key={r.id}>
                    <td style={{ whiteSpace: 'pre-line', minWidth: 200 }}>{r.equipment ?? '—'}</td>
                    <td style={{ whiteSpace: 'pre-line' }} className="muted">{r.systemUnit ?? '—'}</td>
                    <td style={{ whiteSpace: 'pre-line' }} className="muted">{r.monitor ?? '—'}</td>
                    <td style={{ whiteSpace: 'pre-line' }} className="muted">{r.otherEquipment ?? '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
      )}

      {/* Devices */}
      <div className="card card-pad">
        <div className="card-title">Cihazlar</div>
        {devices.length === 0 ? (
          <p className="muted" style={{ fontSize: 13 }}>Bağlı cihaz yoxdur.</p>
        ) : (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
            {devices.map((d) => (
              <div key={d.id} style={{ display: 'flex', alignItems: 'center', gap: 10, justifyContent: 'space-between', flexWrap: 'wrap' }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: 8, minWidth: 0 }}>
                  <IconPhone />
                  <div style={{ minWidth: 0 }}>
                    <div style={{ fontWeight: 600, fontSize: 13 }}>{d.deviceLabel ?? 'Cihaz'}</div>
                    <div className="muted" style={{ fontSize: 11 }}>{d.boundVia} · {fmtDate(d.boundAtUtc.slice(0, 10))}</div>
                  </div>
                </div>
                {manageable && (
                  <button className="btn btn-danger btn-sm" disabled={busy} onClick={() => void onRevokeDevice(d.id)}>Ləğv et</button>
                )}
              </div>
            ))}
          </div>
        )}
      </div>

      {photoModal && (
        <PhotoCompareModal
          title={photoModal.title}
          referenceUrl={photoModal.photo.referencePhotoUrl}
          checkInUrl={photoModal.photo.checkInPhotoUrl}
          checkInTakenAtUtc={photoModal.photo.checkInPhotoTakenAtUtc}
          faceMatchStatus={photoModal.photo.faceMatchStatus}
          faceMatchScore={photoModal.photo.faceMatchScore}
          onClose={() => setPhotoModal(null)}
        />
      )}
    </div>
  )
}

function Stat({ label, value, metric, open, onOpen }: {
  label: string; value: string | number; metric: string; open: string | null; onOpen: (m: string | null) => void
}) {
  const active = open === metric
  return (
    <div
      className="stat-card"
      onClick={() => onOpen(active ? null : metric)}
      style={{ cursor: 'pointer', boxShadow: active ? '0 0 0 2px var(--c400)' : undefined }}
    >
      <div className="stat-lbl">{label}</div>
      <div className="stat-val">{value}</div>
    </div>
  )
}

const METRIC_LABEL: Record<string, string> = {
  workDays: 'İş günləri', hours: 'İş saatları', late: 'Gecikmələr',
  absent: 'Qayıb günləri', incomplete: 'Natamam günlər',
  leave: 'Məzuniyyət', sick: 'Xəstəlik', unpaid: 'Ödənişsiz məzuniyyət',
  permission: 'İcazə', trip: 'Ezamiyyət',
}

/**
 * The days behind a tile.
 *
 * Two defects lived here. «Ezamiyyət» had a tile and NO case, so pressing it opened an empty list
 * under a non-zero number — the screen contradicted itself. And «leave» matched every OnLeave day
 * plus every Permission day, so the tile's figure and the list beneath it were counting different
 * things the moment a person had both.
 *
 * Each metric now filters on the leave TYPE, the same field the badge reads, so a tile and its list
 * can no longer disagree.
 */
function daysForMetric(days: EmployeeDay[], metric: string): EmployeeDay[] {
  const ofType = (t: string) => days.filter((d) => d.status === 'OnLeave' && d.leaveType === t)
  switch (metric) {
    case 'workDays': return days.filter((d) => ['OnTime', 'Late', 'Incomplete'].includes(d.status))
    case 'hours': return days.filter((d) => d.workedMinutes > 0)
    case 'late': return days.filter((d) => d.status === 'Late')
    case 'absent': return days.filter((d) => d.status === 'Absent')
    case 'incomplete': return days.filter((d) => d.status === 'Incomplete')
    case 'leave': return ofType('Vacation')
    case 'sick': return ofType('Sick')
    case 'unpaid': return ofType('Unpaid')
    case 'permission': return days.filter((d) => d.status === 'Permission')
    case 'trip': return ofType('BusinessTrip')
    default: return []
  }
}
function dayDetail(d: EmployeeDay, metric: string): string {
  const ci = d.checkInAtUtc ? fmtTime(d.checkInAtUtc) : '—'
  const co = d.checkOutAtUtc ? fmtTime(d.checkOutAtUtc) : '—'
  if (metric === 'late') return `Giriş ${ci} · ${d.lateMinutes} dəq gecikmə`
  if (metric === 'absent') return 'Giriş yoxdur'
  if (metric === 'incomplete') return `Giriş ${ci} · çıxış yoxdur`
  if (metric === 'hours') return `${ci}–${co} · ${(d.workedMinutes / 60).toFixed(1)} saat`
  if (['leave', 'sick', 'unpaid', 'permission', 'trip'].includes(metric))
    return leaveVisual(d.leaveType)?.label ?? (d.status === 'Permission' ? 'İcazə' : 'Məzuniyyət')
  return `${ci}–${co}`
}
/** The days behind one tile — click "Gecikmə" and see exactly which days, with times. */
function MetricBreakdown({ metric, days, onClose }: { metric: string; days: EmployeeDay[]; onClose: () => void }) {
  const list = daysForMetric(days, metric)
  return (
    <div style={{ marginTop: 12, borderTop: '1px solid var(--c100)', paddingTop: 12 }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 8 }}>
        <div style={{ fontWeight: 700, color: 'var(--c900)' }}>{METRIC_LABEL[metric]} — {list.length}</div>
        <button className="btn btn-sm" onClick={onClose}>Bağla</button>
      </div>
      {list.length === 0 ? (
        <p className="muted" style={{ fontSize: 13, margin: 0 }}>Bu ay üçün gün yoxdur.</p>
      ) : (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
          {list.map((d) => (
            <div key={d.date} style={{ display: 'flex', alignItems: 'center', gap: 10, fontSize: 13, padding: '7px 11px', background: 'var(--c50)', borderRadius: 8 }}>
              <span style={{ fontWeight: 700, color: 'var(--c900)', minWidth: 96 }}>{fmtDate(d.date)}</span>
              <span className="muted">{dayDetail(d, metric)}</span>
            </div>
          ))}
        </div>
      )}
    </div>
  )
}

function Field({ label, value }: { label: string; value: string | null }) {
  return (
    <div>
      <div className="form-label">{label}</div>
      <div style={{ fontSize: 14, color: value ? 'var(--c900)' : 'var(--c400)' }}>{value || '—'}</div>
    </div>
  )
}
