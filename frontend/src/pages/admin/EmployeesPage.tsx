import { useEffect, useState, type FormEvent } from 'react'
import {
  deleteEmployee,
  getAdminLocations,
  getEmployees,
  invite,
  reinviteEmployee,
  resetEmployeeAttendance,
  updateEmployee,
  type AdminEmployee,
  type AdminLocation,
  type InviteResult,
} from '../../api/admin'
import type { Role } from '../../lib/jwt'
import { IconCheck, IconPhone, IconRefresh, IconSend, IconTrash, IconUsers, IconX } from '../../components/icons'

const ROLE_LABEL: Record<Role, string> = { Employee: 'İşçi', Manager: 'Menecer', Admin: 'Admin' }

const ERRORS: Record<string, string> = {
  EmailAlreadyExists: 'Bu email artıq mövcuddur',
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
  locationId: string
  role: Role
  isActive: boolean
}

const EMPTY: FormState = {
  fullName: '',
  fatherName: '',
  position: '',
  birthYear: '',
  email: '',
  locationId: '',
  role: 'Employee',
  isActive: true,
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
  const [link, setLink] = useState<{ name: string; result: InviteResult } | null>(null)
  const [copied, setCopied] = useState(false)

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
      locationId: e.locationId,
      role: e.role,
      isActive: e.isActive,
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
    setSaving(true)
    const payload = {
      fullName: form.fullName.trim(),
      email: form.email.trim(),
      locationId: form.locationId,
      role: form.role,
      fatherName: form.fatherName.trim() || null,
      position: form.position.trim() || null,
      birthYear: form.birthYear ? Number(form.birthYear) : null,
    }
    const res = editingId
      ? await updateEmployee(editingId, { ...payload, isActive: form.isActive })
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
        <button className="btn btn-primary" onClick={showForm && !editingId ? closeForm : startAdd}>
          <IconUsers /> İşçi əlavə et
        </button>
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
              <label className="form-label">Email</label>
              <input className="inp" type="email" required value={form.email} onChange={(e) => set('email', e.target.value)} />
            </div>
            <div>
              <label className="form-label">Rol</label>
              <select className="inp" value={form.role} onChange={(e) => set('role', e.target.value as Role)}>
                <option value="Employee">İşçi</option>
                <option value="Manager">Menecer</option>
                <option value="Admin">Admin</option>
              </select>
            </div>
          </div>

          <div className="form-row cols2">
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
            {editingId && (
              <div>
                <label className="form-label">Status</label>
                <select className="inp" value={form.isActive ? '1' : '0'} onChange={(e) => set('isActive', e.target.value === '1')}>
                  <option value="1">Aktiv</option>
                  <option value="0">Deaktiv (giriş bağlı)</option>
                </select>
              </div>
            )}
          </div>

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
                    {[e.fatherName ? `${e.fatherName} oğlu/qızı` : null, e.birthYear || null].filter(Boolean).join(' · ') || '—'}
                  </div>
                </td>
                <td>{e.position || '—'}</td>
                <td>{e.locationName ?? '—'}</td>
                <td>{ROLE_LABEL[e.role] ?? e.role}</td>
                <td>{deviceBadge(e.hasDevice)}</td>
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
                      <button
                        className="btn btn-sm"
                        disabled={resettingId === e.id}
                        onClick={() => onResetAttendance(e)}
                        title="Giriş/çıxış tarixçəsini sil — hesab qalır, yenidən test edin"
                      >
                        <IconRefresh /> Sıfırla
                      </button>
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

function deviceBadge(hasDevice: boolean) {
  return hasDevice ? (
    <span style={{ display: 'inline-flex', alignItems: 'center', gap: 4, fontSize: 11, fontWeight: 700, color: '#2e7d32' }}>
      <IconPhone /> Bağlı
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
