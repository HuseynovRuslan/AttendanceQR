import { useEffect, useState } from 'react'
import {
  createManagerEmployee,
  getManagerEmployees,
  getManagerLocations,
  getManagerPositions,
  resetManagerEmployeePin,
  updateManagerEmployee,
  type ManagerEmployee,
  type ManagerEmployeeInput,
  type ManagerLocation,
} from '../../api/manager'
import { IconX } from '../../components/icons'
import { WorkCyclePicker } from '../../components/WorkCyclePicker'
import './manager.css'

const EMPTY: ManagerEmployeeInput = {
  fullName: '', email: null, phoneNumber: null, fatherName: null, position: null,
  locationId: '', birthDate: null, birthYear: null, workStart: null, workEnd: null,
  workCycleDays: null, workCycleOnDays: null, workCycleAnchor: null,
  photoExempt: false, isActive: true,
}

const ERRORS: Record<string, string> = {
  NameRequired: 'Ad tələb olunur',
  NeedEmailOrPhone: 'Telefon və ya e-poçt lazımdır',
  EmailAlreadyExists: 'Bu e-poçt artıq mövcuddur',
  PhoneAlreadyExists: 'Bu telefon artıq mövcuddur',
  LocationNotManaged: 'Bu filial sizə aid deyil',
  WorkCycleDaysInvalid: 'Növbə dövrü 2–28 gün aralığında olmalıdır',
  WorkCycleOnDaysInvalid: 'İş günlərinin sayı dövrədən az olmalıdır',
  WorkCycleAnchorRequired: 'Növbə üçün işlədiyi bir gün seçilməlidir',
}

/**
 * A manager's own branches' staff — the screen that stops the manager pestering an admin to "add
 * this one, remove that one". Everything here is scoped server-side to their locations; there is no
 * salary and no role field, because a manager sets neither.
 */
export function ManagerEmployeesPage() {
  const [rows, setRows] = useState<ManagerEmployee[]>([])
  const [locations, setLocations] = useState<ManagerLocation[]>([])
  const [positions, setPositions] = useState<string[]>([])
  const [loading, setLoading] = useState(true)
  const [editing, setEditing] = useState<string | null>(null) // id, or 'new', or null
  const [form, setForm] = useState<ManagerEmployeeInput>(EMPTY)
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState<string | null>(null)
  const [pin, setPin] = useState<{ name: string; pin: string } | null>(null)

  async function load() {
    setLoading(true)
    const [e, l, p] = await Promise.all([getManagerEmployees(), getManagerLocations(), getManagerPositions()])
    if (e.status === 200 && Array.isArray(e.data)) setRows(e.data)
    if (l.status === 200 && Array.isArray(l.data)) setLocations(l.data)
    if (p.status === 200 && Array.isArray(p.data)) setPositions(p.data.map((x) => x.name))
    setLoading(false)
  }

  useEffect(() => { void load() }, [])

  function set<K extends keyof ManagerEmployeeInput>(k: K, v: ManagerEmployeeInput[K]) {
    setForm((f) => ({ ...f, [k]: v }))
  }

  function startAdd() {
    setForm({ ...EMPTY, locationId: locations[0]?.id ?? '' })
    setEditing('new')
    setErr(null)
    setPin(null)
  }

  function startEdit(e: ManagerEmployee) {
    setForm({
      fullName: e.fullName, email: e.email.endsWith('@baki.local') ? null : e.email,
      phoneNumber: e.phoneNumber, fatherName: e.fatherName, position: e.position,
      locationId: e.locationId, birthDate: e.birthDate, birthYear: e.birthYear,
      workStart: e.workStart, workEnd: e.workEnd, photoExempt: e.photoExempt, isActive: e.isActive,
      workCycleDays: e.workCycleDays, workCycleOnDays: e.workCycleOnDays, workCycleAnchor: e.workCycleAnchor,
    })
    setEditing(e.id)
    setErr(null)
    setPin(null)
  }

  async function save() {
    setBusy(true)
    setErr(null)
    const res = editing === 'new'
      ? await createManagerEmployee(form)
      : await updateManagerEmployee(editing!, form)
    setBusy(false)
    if (res.status === 200 && res.data && 'id' in res.data) {
      if ('tempPin' in res.data) setPin({ name: form.fullName, pin: res.data.tempPin as string })
      setEditing(null)
      void load()
    } else {
      const code = res.data && 'error' in res.data ? res.data.error : ''
      setErr(ERRORS[code] ?? 'Yadda saxlanılmadı')
    }
  }

  async function resetPin(id: string, name: string) {
    if (!window.confirm(`${name} üçün yeni müvəqqəti PIN yaradılsın?

Köhnə PIN dərhal işləməyəcək — yenisini işçiyə verməlisiniz.`)) return
    const { status, data } = await resetManagerEmployeePin(id)
    if (status === 200 && data && 'tempPin' in data) { setPin({ name, pin: data.tempPin as string }); setEditing(null) }
  }

  return (
    <div>
      {pin && (
        <div className="fb fb-ok" style={{ marginBottom: 16, display: 'block' }}>
          <div style={{ fontWeight: 700 }}>{pin.name} — müvəqqəti PIN: <span className="mono" style={{ fontSize: 16 }}>{pin.pin}</span></div>
          <div style={{ fontSize: 12, marginTop: 4 }}>Bu PIN-i işçiyə verin. İlk girişdə öz PIN-ini təyin edəcək. Bu pəncərə bağlananda PIN yenidən görünməyəcək.</div>
          <button className="btn btn-sm" style={{ marginTop: 8 }} onClick={() => setPin(null)}>Bağla</button>
        </div>
      )}

      {!editing && (
        <div style={{ marginBottom: 16 }}>
          <button className="btn btn-primary" onClick={startAdd} disabled={locations.length === 0}>+ Yeni işçi</button>
          {locations.length === 0 && !loading && (
            <span className="muted" style={{ marginLeft: 12, fontSize: 13 }}>
              Sizə hələ filial təyin edilməyib — admin ilə əlaqə saxlayın.
            </span>
          )}
        </div>
      )}

      {editing && (
        <div className="card card-pad" style={{ marginBottom: 16 }}>
          <div className="card-title">{editing === 'new' ? 'Yeni işçi' : 'İşçini redaktə et'}</div>
          <div className="form-row cols2">
            <div>
              <label className="form-label">Ad Soyad *</label>
              <input className="inp" value={form.fullName} onChange={(e) => set('fullName', e.target.value)} />
            </div>
            <div>
              <label className="form-label">Ata adı</label>
              <input className="inp" value={form.fatherName ?? ''} onChange={(e) => set('fatherName', e.target.value || null)} />
            </div>
          </div>
          <div className="form-row cols2">
            <div>
              <label className="form-label">Telefon</label>
              <input className="inp" value={form.phoneNumber ?? ''} onChange={(e) => set('phoneNumber', e.target.value || null)} placeholder="+994…" />
            </div>
            <div>
              <label className="form-label">E-poçt (istəyə bağlı)</label>
              <input className="inp" value={form.email ?? ''} onChange={(e) => set('email', e.target.value || null)} />
            </div>
          </div>
          <div className="form-row cols2">
            <div>
              <label className="form-label">Vəzifə</label>
              <input className="inp" list="mgr-positions" value={form.position ?? ''} onChange={(e) => set('position', e.target.value || null)} placeholder="məs. Bağban" />
              <datalist id="mgr-positions">{positions.map((p) => <option key={p} value={p} />)}</datalist>
            </div>
            <div>
              <label className="form-label">Filial *</label>
              <select className="inp" value={form.locationId} onChange={(e) => set('locationId', e.target.value)}>
                {locations.map((l) => <option key={l.id} value={l.id}>{l.name}</option>)}
              </select>
            </div>
          </div>
          <div className="form-row cols2">
            <div>
              <label className="form-label">Doğum tarixi</label>
              <input className="inp" type="date" value={form.birthDate ?? ''} onChange={(e) => set('birthDate', e.target.value || null)} />
            </div>
            <div>
              <label className="form-label">İş saatları (istəyə bağlı)</label>
              <div style={{ display: 'flex', gap: 8 }}>
                <input className="inp" type="time" value={form.workStart ?? ''} onChange={(e) => set('workStart', e.target.value || null)} />
                <input className="inp" type="time" value={form.workEnd ?? ''} onChange={(e) => set('workEnd', e.target.value || null)} />
              </div>
            </div>
          </div>
          <WorkCyclePicker
            value={{
              days: form.workCycleDays,
              onDays: form.workCycleOnDays ?? 1,
              anchor: form.workCycleAnchor ?? '',
            }}
            onChange={(c) =>
              setForm((f) => ({
                ...f,
                workCycleDays: c.days,
                workCycleOnDays: c.days ? c.onDays : null,
                workCycleAnchor: c.days ? c.anchor || null : null,
              }))
            }
          />

          <label style={{ display: 'flex', gap: 8, marginTop: 8 }}>
            <input type="checkbox" checked={!form.isActive} onChange={(e) => set('isActive', !e.target.checked)} />
            <span style={{ fontSize: 13 }}>Deaktiv (girişi bağlı)</span>
          </label>

          {err && <div className="fb fb-err" style={{ marginTop: 10 }}><IconX /><span>{err}</span></div>}

          <div style={{ display: 'flex', gap: 8, marginTop: 14 }}>
            <button className="btn btn-primary" disabled={busy} onClick={() => void save()}>
              {busy ? 'Yadda saxlanılır…' : editing === 'new' ? 'Əlavə et' : 'Yadda saxla'}
            </button>
            <button className="btn" onClick={() => setEditing(null)}>Ləğv et</button>
          </div>

          {/* Reset-PIN lives inside the edit screen, not on every list row — it is destructive (the
              employee's current PIN stops working), so it must be a deliberate step, not a tap next
              to "Redaktə" that anyone could hit by accident. */}
          {editing !== 'new' && (
            <div style={{ marginTop: 16, paddingTop: 14, borderTop: '1px solid var(--c100)' }}>
              <button className="btn btn-sm" onClick={() => void resetPin(editing!, form.fullName)}>
                PIN-i sıfırla
              </button>
              <div className="muted" style={{ fontSize: 12, marginTop: 6 }}>
                İşçi PIN-ini unudubsa, yeni müvəqqəti PIN yaradın. Köhnəsi işləməyəcək.
              </div>
            </div>
          )}
        </div>
      )}

      {!editing && (
        <>
          {loading && <div className="card card-pad muted">Yüklənir…</div>}
          {!loading && rows.length === 0 && <div className="card card-pad muted" style={{ textAlign: 'center' }}>İşçi yoxdur.</div>}
          <div className="mgr-list">
            {rows.map((e) => (
              <div className="mgr-item" key={e.id}>
                <div className="mgr-main">
                  <div className="mgr-name">
                    {e.fullName}{e.fatherName ? <span className="father"> {e.fatherName}</span> : ''}
                  </div>
                  <div className="mgr-meta">
                    {e.position || '—'}<span className="dot">·</span>{e.locationName}
                  </div>
                </div>
                <div className="mgr-side">
                  {e.isActive
                    ? <span className="pill pill-ok">Aktiv</span>
                    : <span className="pill">Deaktiv</span>}
                  <div className="mgr-actions">
                    <button className="btn btn-sm" onClick={() => startEdit(e)}>Redaktə</button>
                  </div>
                </div>
              </div>
            ))}
          </div>
        </>
      )}
    </div>
  )
}
