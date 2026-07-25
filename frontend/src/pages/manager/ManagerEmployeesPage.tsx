import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
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
import { getManagerSchedules, type ManagerSchedule } from '../../api/manager'
import './manager.css'

const EMPTY: ManagerEmployeeInput = {
  fullName: '', firstName: '', lastName: '', email: null, phoneNumber: null, fatherName: null, position: null,
  locationId: '', birthDate: null, birthYear: null, workStart: null, workEnd: null,
  scheduleId: null, workCycleDays: null, workCycleOnDays: null, workCycleAnchor: null,
  photoExempt: false, isActive: true,
}

/** Prefer stored parts; fall back to splitting FullName (last token = surname) for un-backfilled rows. */
function splitName(first: string | null | undefined, last: string | null | undefined, full: string): { first: string; last: string } {
  if (first || last) return { first: first ?? '', last: last ?? '' }
  const toks = (full ?? '').trim().split(/\s+/).filter(Boolean)
  if (toks.length <= 1) return { first: toks[0] ?? '', last: '' }
  return { first: toks.slice(0, -1).join(' '), last: toks[toks.length - 1] }
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
  const navigate = useNavigate()
  const [positions, setPositions] = useState<string[]>([])
  const [schedules, setSchedules] = useState<ManagerSchedule[]>([])
  const [loading, setLoading] = useState(true)
  const [editing, setEditing] = useState<string | null>(null) // id, or 'new', or null
  const [form, setForm] = useState<ManagerEmployeeInput>(EMPTY)
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState<string | null>(null)
  const [pin, setPin] = useState<{ name: string; pin: string } | null>(null)

  async function load() {
    setLoading(true)
    const [e, l, p, sc] = await Promise.all([
      getManagerEmployees(), getManagerLocations(), getManagerPositions(), getManagerSchedules(),
    ])
    if (e.status === 200 && Array.isArray(e.data)) setRows(e.data)
    if (l.status === 200 && Array.isArray(l.data)) setLocations(l.data)
    if (p.status === 200 && Array.isArray(p.data)) setPositions(p.data.map((x) => x.name))
    if (sc.status === 200 && Array.isArray(sc.data)) setSchedules(sc.data)
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
    const nm = splitName(e.firstName, e.lastName, e.fullName)
    setForm({
      fullName: e.fullName, firstName: nm.first, lastName: nm.last,
      email: e.email.endsWith('@baki.local') ? null : e.email,
      phoneNumber: e.phoneNumber, fatherName: e.fatherName, position: e.position,
      locationId: e.locationId, birthDate: e.birthDate, birthYear: e.birthYear,
      workStart: e.workStart, workEnd: e.workEnd, photoExempt: e.photoExempt, isActive: e.isActive,
      scheduleId: e.scheduleId,
      workCycleDays: e.workCycleDays, workCycleOnDays: e.workCycleOnDays, workCycleAnchor: e.workCycleAnchor,
    })
    setEditing(e.id)
    setErr(null)
    setPin(null)
  }

  async function save() {
    setBusy(true)
    setErr(null)
    // FullName stays canonical (backend also composes it) — build it from the two name fields.
    const payload = { ...form, fullName: `${(form.firstName ?? '').trim()} ${(form.lastName ?? '').trim()}`.trim() }
    const res = editing === 'new'
      ? await createManagerEmployee(payload)
      : await updateManagerEmployee(editing!, payload)
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
              <label className="form-label">Ad *</label>
              <input className="inp" value={form.firstName ?? ''} onChange={(e) => set('firstName', e.target.value)} />
            </div>
            <div>
              <label className="form-label">Soyad *</label>
              <input className="inp" value={form.lastName ?? ''} onChange={(e) => set('lastName', e.target.value)} />
            </div>
          </div>
          <div className="form-row cols2">
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
          {/* One shift control — hours, work-days and rotation all come from the named shift chosen
              here (created in the Növbələr panel), not retyped per person. Any old per-person hours
              still ride along in `form` and are saved back untouched, so nothing is lost. */}
          <div style={{ marginBottom: 14 }}>
            <label className="form-label">Növbə</label>
            <select
              className="inp"
              value={form.scheduleId ?? ''}
              onChange={(e) => {
                if (e.target.value === '__new__') { navigate('/admin/schedules'); return }
                set('scheduleId', e.target.value || null)
              }}
            >
              <option value="">— növbə yoxdur (filialın saatları) —</option>
              {schedules.map((sc) => (
                <option key={sc.id} value={sc.id}>
                  {sc.name} · {sc.shiftStart}–{sc.shiftEnd}{sc.isOvernight ? ' 🌙' : ''}
                </option>
              ))}
              <option value="__new__">＋ Yeni növbə yarat…</option>
            </select>
            <p style={{ fontSize: 12, color: 'var(--c500)', marginTop: 6, marginBottom: 0, lineHeight: 1.6 }}>
              İşçinin saatları seçdiyiniz növbədən gəlir. İstədiyiniz növbə yoxdursa «＋ Yeni növbə
              yarat» ilə Növbələr panelində yaradın.
            </p>
            {!form.scheduleId && (form.workStart || form.workCycleDays) && (
              <div className="fb" style={{ marginTop: 10, background: '#FFF7ED', color: '#9a3412' }}>
                <span>
                  Bu işçidə köhnə fərdi qrafik var
                  {form.workStart && form.workEnd ? ` (🕒 ${form.workStart}–${form.workEnd})` : ''}
                  {form.workCycleDays ? ' 🔄' : ''}. Uyğun növbəni seçin — yoxdursa yaradıb təyin edin.
                </span>
              </div>
            )}
          </div>

          <div className="form-row cols2">
            <div>
              <label className="form-label">Doğum tarixi</label>
              <input className="inp" type="date" value={form.birthDate ?? ''} onChange={(e) => set('birthDate', e.target.value || null)} />
            </div>
            <div />
          </div>

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
