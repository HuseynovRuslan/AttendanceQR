import { useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import {
  bulkPermissionAsManager,
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
  photoExempt: false, canFieldCheckIn: false, isActive: true,
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
  // A branch can run to two hundred people; scrolling to find one is not a way to work. Matches the
  // same fields the admin roster searches, so the two screens behave alike.
  const [search, setSearch] = useState('')

  /** Grant or withdraw a capability across this manager's whole list. The server narrows it to their
   *  branches' plain staff, so a name they may not act on is skipped rather than failing the call —
   *  refusing all of it would teach them to stop using the button. */
  async function grant(targets: ManagerEmployee[], permission: 'ShareDevice' | 'FieldCheckIn', allowed: boolean) {
    const has = (r: ManagerEmployee) =>
      (permission === 'ShareDevice' ? r.canShareDevice : r.canFieldCheckIn) === true
    const affected = targets.filter((r) => has(r) !== allowed)
    if (affected.length === 0) return

    const what = permission === 'ShareDevice' ? 'ortaq telefon' : 'sahə ziyarəti'
    if (!window.confirm(
      allowed
        ? `${affected.length} nəfərə «${what}» icazəsi verilsin?`
        : `${affected.length} nəfərdən «${what}» icazəsi alınsın?`,
    )) return

    setBusy(true)
    const { status } = await bulkPermissionAsManager(affected.map((r) => r.id), permission, allowed)
    setBusy(false)
    if (status === 200) await load()
    else setErr('Dəyişmədi')
  }

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
      // Phone-only employees have a null email now — and legacy placeholder addresses are hidden.
      email: e.email && !e.email.endsWith('@baki.local') ? e.email : null,
      phoneNumber: e.phoneNumber, fatherName: e.fatherName, position: e.position,
      locationId: e.locationId, birthDate: e.birthDate, birthYear: e.birthYear,
      workStart: e.workStart, workEnd: e.workEnd, photoExempt: e.photoExempt,
      canFieldCheckIn: e.canFieldCheckIn, isActive: e.isActive,
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

  const q = search.trim().toLowerCase()
  const visible = q
    ? rows.filter((r) =>
        `${r.fullName} ${r.phoneNumber ?? ''} ${r.position ?? ''} ${r.locationName ?? ''}`
          .toLowerCase()
          .includes(q))
    : rows
  // Colleagues are visible and read-only; every bulk action stops at the rows a manager may change.
  const grantable = visible.filter((r) => r.manageable !== false)

  return (
    <div>
      {pin && (
        <div className="fb fb-ok" style={{ marginBottom: 16, display: 'block' }}>
          <div style={{ fontWeight: 700 }}>{pin.name} — müvəqqəti PIN: <span className="mono" style={{ fontSize: 16 }}>{pin.pin}</span></div>
          <div style={{ fontSize: 12, marginTop: 4 }}>Bu PIN-i işçiyə verin. İlk girişdə öz PIN-ini təyin edəcək. Bu pəncərə bağlananda PIN yenidən görünməyəcək.</div>
          <button className="btn btn-sm" style={{ marginTop: 8 }} onClick={() => setPin(null)}>Bağla</button>
        </div>
      )}

      {/* The two opt-in capabilities across this manager's own staff. The manager is who knows which
          of their brigade owns no phone and which of their sites has no poster; making them ask an
          admin for every name is how a permission ends up either never granted or granted to
          everyone. The server narrows it to their branches' plain staff regardless. */}
      {/* The bulk strip counts and targets only rows this manager may act on. The list now shows
          colleagues too, and the server skips them — but a count that included them would report
          "3 of 12" over a set of 9, and the button would say it changed more people than it did. */}
      {!editing && grantable.length > 0 && (
        <div className="card" style={{ padding: '12px 16px', marginBottom: 16, display: 'flex', flexDirection: 'column', gap: 10 }}>
          {([
            {
              key: 'ShareDevice' as const,
              icon: '📱',
              title: 'Ortaq telefon icazəsi',
              hint: 'Telefonu olmayanlar üçün: hesabı briqadanın telefonunda saxlanıla bilər.',
              count: grantable.filter((r) => r.canShareDevice === true).length,
            },
            {
              key: 'FieldCheckIn' as const,
              icon: '📍',
              title: 'Sahə ziyarəti icazəsi',
              hint: 'QR plakatı olmayan obyektlər üçün: GPS + selfi ilə davamiyyət.',
              count: grantable.filter((r) => r.canFieldCheckIn === true).length,
            },
          ]).map((p) => (
            <div key={p.key} style={{ display: 'flex', alignItems: 'center', gap: 14, flexWrap: 'wrap' }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: 10, minWidth: 0 }}>
                <span style={{ fontSize: 22 }}>{p.icon}</span>
                <div style={{ minWidth: 0 }}>
                  <div style={{ fontWeight: 700 }}>{p.title} — {grantable.length} nəfərdən {p.count}-də var</div>
                  <div className="muted" style={{ fontSize: 12 }}>{p.hint}</div>
                </div>
              </div>
              <div style={{ marginLeft: 'auto', display: 'flex', gap: 8 }}>
                <button className="btn btn-sm" disabled={busy} onClick={() => void grant(grantable, p.key, true)}>
                  Hamısına ver
                </button>
                <button className="btn btn-sm" disabled={busy} onClick={() => void grant(grantable, p.key, false)}>
                  Geri al
                </button>
              </div>
            </div>
          ))}
        </div>
      )}

      {!editing && rows.length > 0 && (
        <div style={{ display: 'flex', gap: 8, alignItems: 'center', flexWrap: 'wrap', marginBottom: 12 }}>
          <input
            className="inp"
            type="text"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Ad, nömrə, vəzifə üzrə axtar…"
            style={{ width: 'auto', minWidth: 240, padding: '8px 12px' }}
          />
          {search && <button className="btn btn-sm" onClick={() => setSearch('')}>Təmizlə</button>}
          <span className="muted" style={{ fontSize: 13 }}>
            {q ? `${visible.length} / ${rows.length}` : `${rows.length} işçi`}
          </span>
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
            {visible.map((e) => (
              <div className="mgr-item" key={e.id}>
                <div className="mgr-main">
                  {/* The way into the person's card. It was reachable by typing the URL and no other
                      way — the admin roster has linked its names since it existed, and this one never
                      did, so from a manager's side the profile simply did not appear to exist. */}
                  <div className="mgr-name">
                    <Link to={`/admin/employees/${e.id}`} className="emp-link">
                      {e.fullName}{e.fatherName ? <span className="father"> {e.fatherName}</span> : ''}
                    </Link>
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
                    {/* A colleague's row is visible and read-only. The server refuses the write
                        either way — this is so the button is not there to be pressed. */}
                    {e.manageable === false
                      ? <span className="tag">həmkar</span>
                      : <button className="btn btn-sm" onClick={() => startEdit(e)}>Redaktə</button>}
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
