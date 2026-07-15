import { useEffect, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import {
  getEmployees,
  getSummary,
  getDeviceBindings,
  revokeDeviceBinding,
  resetPin,
  reinviteEmployee,
  updateEmployee,
  type AdminEmployee,
  type EmployeeReportRow,
  type DeviceBinding,
  type InviteResult,
} from '../../api/admin'
import { getEmployeeAttendance, getPhotoUrl } from '../../api/attendance'
import type { AttendanceRecord } from '../../api/attendance'
import { RecordBadge } from '../../components/StatusBadge'
import { fmtDate, fmtDuration, fmtTime, initials } from '../../lib/att'
import { IconCamera, IconCheck, IconPhone, IconX } from '../../components/icons'

const ROLE_LABEL: Record<string, string> = { Admin: 'Admin', Manager: 'Ərazi meneceri', Employee: 'İşçi' }

/** One employee's full profile: identity + this-month summary + recent attendance + photos + devices,
 * with the key actions (edit, PIN reset, activate/deactivate, invite link) in one place. All data comes
 * from existing admin endpoints — no new backend. Reached at /admin/employees/:id. */
export function EmployeeProfilePage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()

  const [emp, setEmp] = useState<AdminEmployee | null>(null)
  const [records, setRecords] = useState<AttendanceRecord[]>([])
  const [summary, setSummary] = useState<EmployeeReportRow | null>(null)
  const [devices, setDevices] = useState<DeviceBinding[]>([])
  const [photos, setPhotos] = useState<{ reference: string | null; checkIn: string | null }>({ reference: null, checkIn: null })
  const [loading, setLoading] = useState(true)
  const [notFound, setNotFound] = useState(false)
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState<string | null>(null)
  const [pin, setPin] = useState<string | null>(null)
  const [invite, setInvite] = useState<InviteResult | null>(null)

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

    const [empRes, attRes, devRes, sumRes] = await Promise.all([
      getEmployees(),
      getEmployeeAttendance(id),
      getDeviceBindings(),
      getSummary(monthStart, today),
    ])

    const found = empRes.status === 200 && Array.isArray(empRes.data) ? empRes.data.find((e) => e.id === id) ?? null : null
    setEmp(found)
    if (!found) {
      setNotFound(true)
      setLoading(false)
      return
    }
    const recs = attRes.status === 200 && Array.isArray(attRes.data) ? attRes.data : []
    setRecords(recs)
    if (devRes.status === 200 && Array.isArray(devRes.data)) setDevices(devRes.data.filter((d) => d.employeeId === id))
    if (sumRes.status === 200 && sumRes.data && 'rows' in sumRes.data)
      setSummary(sumRes.data.rows.find((r) => r.employeeId === id) ?? null)

    // Reference + latest check-in selfie: getPhotoUrl on the newest record returns both (or nothing).
    if (recs[0]) {
      const p = await getPhotoUrl(recs[0].recordId)
      if (p.status === 200 && p.data && 'referencePhotoUrl' in p.data)
        setPhotos({ reference: p.data.referencePhotoUrl, checkIn: p.data.checkInPhotoUrl })
    }
    setLoading(false)
  }

  async function onResetPin() {
    if (!emp) return
    setBusy(true)
    setErr(null)
    const r = await resetPin(emp.id)
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
    const r = await updateEmployee(emp.id, {
      fullName: emp.fullName,
      email: emp.email || null,
      phoneNumber: emp.phoneNumber,
      locationId: emp.locationId,
      role: emp.role,
      fatherName: emp.fatherName,
      position: emp.position,
      birthYear: emp.birthYear,
      workStart: emp.workStart ?? null,
      workEnd: emp.workEnd ?? null,
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
            <div style={{ fontFamily: 'Sora,sans-serif', fontWeight: 800, fontSize: 20, color: 'var(--c900)' }}>{emp.fullName}</div>
            <div className="muted" style={{ fontSize: 13, marginTop: 2 }}>
              {[emp.position, emp.locationName].filter(Boolean).join(' · ') || '—'}
            </div>
          </div>
          <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
            <span className="tag">{ROLE_LABEL[emp.role] ?? emp.role}</span>
            <span className={`badge ${emp.isActive ? 'b-present' : 'b-absent'}`}>{emp.isActive ? 'Aktiv' : 'Deaktiv'}</span>
            {!emp.activated && <span className="badge b-late">Aktivləşməyib</span>}
            <span className={`badge ${emp.hasDevice ? 'b-device' : 'b-nodevice'}`}>{emp.hasDevice ? 'Cihaz bağlı' : 'Cihaz yox'}</span>
          </div>
        </div>

        {/* Actions */}
        <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', marginTop: 16 }}>
          <button className="btn btn-primary btn-sm" onClick={() => navigate(`/admin/employees?edit=${emp.id}`)}>Redaktə et</button>
          <button className="btn btn-sm" disabled={busy || !emp.activated} onClick={() => void onResetPin()}>PIN sıfırla</button>
          {!emp.activated && <button className="btn btn-sm" disabled={busy} onClick={() => void onReinvite()}>Dəvət linki</button>}
          <button className={`btn btn-sm ${emp.isActive ? 'btn-danger' : ''}`} disabled={busy} onClick={() => void onToggleActive()}>
            {emp.isActive ? 'Deaktiv et' : 'Aktiv et'}
          </button>
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
          <div className="stat-grid" style={{ marginBottom: 0 }}>
            <Stat label="İş günü" value={summary.workDays} />
            <Stat label="Saat" value={summary.totalWorkedHours.toFixed(1)} />
            <Stat label="Gecikmə" value={summary.lateCount} />
            <Stat label="Qayıb" value={summary.absentDays} />
            <Stat label="Natamam" value={summary.incompleteDays} />
            <Stat label="Məzuniyyət/İcazə" value={summary.leaveDays + summary.permissionDays} />
          </div>
        ) : (
          <p className="muted" style={{ fontSize: 13 }}>Bu ay üçün məlumat yoxdur.</p>
        )}
      </div>

      {/* Personal */}
      <div className="card card-pad">
        <div className="card-title">Şəxsi məlumat</div>
        <div className="form-row cols2" style={{ marginBottom: 0 }}>
          <Field label="Ata adı" value={emp.fatherName} />
          <Field label="Təvəllüd" value={emp.birthYear ? String(emp.birthYear) : null} />
          <Field label="Telefon" value={emp.phoneNumber} />
          <Field label="Email" value={emp.email} />
          <Field label="İş saatı" value={emp.workStart && emp.workEnd ? `${emp.workStart} – ${emp.workEnd}` : 'Ərazinin saatı'} />
          <Field label="Qeydiyyat" value={emp.createdAtUtc ? fmtDate(emp.createdAtUtc.slice(0, 10)) : null} />
        </div>
      </div>

      {/* Photos */}
      {(photos.reference || photos.checkIn) && (
        <div className="card card-pad">
          <div className="card-title" style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <span>Fotolar</span>
            <Link to="/admin/photo-audit" className="btn btn-sm"><IconCamera /> Foto audit</Link>
          </div>
          <div style={{ display: 'flex', gap: 16, flexWrap: 'wrap' }}>
            {photos.reference && <Photo label="Referans" url={photos.reference} />}
            {photos.checkIn && <Photo label="Son giriş" url={photos.checkIn} />}
          </div>
        </div>
      )}

      {/* Recent attendance */}
      <div className="card card-pad">
        <div className="card-title">Son davamiyyət</div>
        {recent.length === 0 ? (
          <p className="muted" style={{ fontSize: 13 }}>Qeyd yoxdur.</p>
        ) : (
          <div className="tbl-wrap">
            <table>
              <thead>
                <tr><th>Tarix</th><th>Status</th><th>Giriş</th><th>Çıxış</th><th>Müddət</th></tr>
              </thead>
              <tbody>
                {recent.map((r) => (
                  <tr key={r.recordId}>
                    <td>{fmtDate(r.attendanceDate)}</td>
                    <td><RecordBadge r={r} /></td>
                    <td className="mono">{fmtTime(r.checkInAtUtc)}</td>
                    <td className="mono">{fmtTime(r.checkOutAtUtc)}</td>
                    <td className="mono">{r.checkInAtUtc && r.checkOutAtUtc ? fmtDuration(r.checkInAtUtc, r.checkOutAtUtc) : '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

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
                <button className="btn btn-danger btn-sm" disabled={busy} onClick={() => void onRevokeDevice(d.id)}>Ləğv et</button>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  )
}

function Stat({ label, value }: { label: string; value: string | number }) {
  return (
    <div className="stat-card">
      <div className="stat-lbl">{label}</div>
      <div className="stat-val">{value}</div>
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

function Photo({ label, url }: { label: string; url: string }) {
  return (
    <div style={{ textAlign: 'center' }}>
      <img src={url} alt={label} style={{ width: 120, height: 120, objectFit: 'cover', borderRadius: 'var(--r)', border: '1px solid var(--c200)' }} />
      <div className="muted" style={{ fontSize: 12, marginTop: 4 }}>{label}</div>
    </div>
  )
}
