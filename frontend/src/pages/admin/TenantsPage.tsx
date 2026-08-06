import { useEffect, useState, type FormEvent } from 'react'
import {
  createTenant,
  getSuperTenants,
  setTenantActive,
  getSuperDashboard,
  getSuperAudit,
  searchSuperUsers,
  resetSuperUserPin,
  reactivateSuperUser,
  revokeSuperUserSessions,
  impersonateTenant,
  getSuperFeatures,
  setTenantPlan,
  type CreateTenantResult,
  type SuperTenant,
  type SuperDashboard,
  type SuperAuditEntry,
  type SuperUser,
  type ImpersonateResult,
  type SuperFeature,
} from '../../api/admin'
import { startImpersonation } from '../../api/client'
import { fmtDate } from '../../lib/format'
import { parseMoney, moneyInputFilter, formatMoney } from '../../lib/money'
import { useCan } from '../operator/OperatorContext'
import { IconCheck, IconUsers, IconX } from '../../components/icons'

const ERRORS: Record<string, string> = {
  NotSuperAdmin: 'İcazəniz yoxdur',
  SlugInvalid: 'Ünvan yalnız kiçik hərf, rəqəm və tire ola bilər (2–20 simvol)',
  SlugReserved: 'Bu ünvan sistem üçün ayrılıb — başqasını seçin',
  SlugTaken: 'Bu ünvan artıq istifadə olunur',
  AdminPhoneInvalid: 'Admin nömrəsi yanlışdır',
  AdminPinInvalid: 'PIN 4 rəqəm olmalıdır',
  TenantNotFound: 'Şirkət tapılmadı',
  CannotDisableOwnTenant: 'Öz şirkətinizi söndürə bilməzsiniz — panelə girişiniz bağlanardı',
  TenantInactive: 'Şirkət söndürülüb — əvvəl aktiv edin',
  NoAdmin: 'Bu şirkətdə aktiv admin yoxdur',
  CannotImpersonateSelf: 'Öz hesabınıza daxil ola bilməzsiniz',
  CannotImpersonateOperator: 'Bu şirkətin adminı özü operatordur — operator kimi daxil olmaq olmaz',
}

const EMPTY = { slug: '', displayName: '', adminName: '', adminPhone: '', adminPin: '', locationName: '' }

// Stable action codes → readable Azerbaijani labels for the audit trail.
const AUDIT_LABELS: Record<string, string> = {
  TenantCreated: 'Şirkət yaradıldı',
  TenantEnabled: 'Şirkət açıldı',
  TenantDisabled: 'Şirkət söndürüldü',
  TenantBrandingChanged: 'Brendinq dəyişdi',
}

type Tab = 'overview' | 'tenants' | 'users' | 'audit'
const TABS: { key: Tab; label: string }[] = [
  { key: 'overview', label: 'İcmal' },
  { key: 'tenants', label: 'Şirkətlər' },
  { key: 'users', label: 'İstifadəçilər' },
  { key: 'audit', label: 'Audit' },
]

function fmtDateTime(iso: string) {
  const d = new Date(iso)
  return d.toLocaleString('az-AZ', {
    day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit',
  })
}

export function TenantsPage() {
  const [tab, setTab] = useState<Tab>('overview')

  return (
    <div>
      <div style={{ marginBottom: 12 }}>
        <h1 style={{ fontSize: 18, fontWeight: 800, color: 'var(--c900)' }}>Platform idarəetməsi</h1>
        <div className="muted" style={{ fontSize: 13 }}>Bütün şirkətlər üzrə nəzarət, idarəetmə və audit.</div>
      </div>

      <div style={{ display: 'flex', gap: 2, marginBottom: 16, borderBottom: '1px solid rgba(0,0,0,0.08)' }}>
        {TABS.map((t) => (
          <button
            key={t.key}
            onClick={() => setTab(t.key)}
            style={{
              border: 'none', background: 'none', cursor: 'pointer', padding: '8px 14px', fontSize: 14,
              fontWeight: tab === t.key ? 800 : 600,
              color: tab === t.key ? 'var(--c900)' : 'var(--c400)',
              borderBottom: tab === t.key ? '2px solid var(--leaf)' : '2px solid transparent',
              marginBottom: -1,
            }}
          >
            {t.label}
          </button>
        ))}
      </div>

      {tab === 'overview' && <SuperOverview />}
      {tab === 'tenants' && <TenantsTab />}
      {tab === 'users' && <SuperUsers />}
      {tab === 'audit' && <SuperAudit />}
    </div>
  )
}

// ── İstifadəçilər: find anyone on the platform and help them back in ─────────
export function SuperUsers() {
  const canUsers = useCan('ManageUsers')
  const [q, setQ] = useState('')
  const [rows, setRows] = useState<SuperUser[]>([])
  const [loading, setLoading] = useState(false)
  const [searched, setSearched] = useState(false)
  const [busyId, setBusyId] = useState<string | null>(null)
  const [pin, setPin] = useState<{ id: string; tempPin: string } | null>(null)
  const [msg, setMsg] = useState<string | null>(null)

  async function search(e?: FormEvent) {
    e?.preventDefault()
    if (q.trim().length < 2) return
    setLoading(true); setSearched(true); setPin(null); setMsg(null)
    const { status, data } = await searchSuperUsers(q.trim())
    setLoading(false)
    setRows(status === 200 && Array.isArray(data) ? data : [])
  }

  async function act(u: SuperUser, kind: 'pin' | 'reactivate' | 'revoke') {
    const ask = { pin: 'PIN sıfırlansın?', reactivate: 'Hesab aktiv edilsin?', revoke: 'Bütün sessiyalar bağlansın?' }
    if (!window.confirm(`"${u.fullName}" — ${ask[kind]}`)) return
    setBusyId(u.id); setPin(null); setMsg(null)
    const res =
      kind === 'pin' ? await resetSuperUserPin(u.id) :
      kind === 'reactivate' ? await reactivateSuperUser(u.id) :
      await revokeSuperUserSessions(u.id)
    setBusyId(null)
    if (res.status === 200 && res.data && !('error' in res.data)) {
      if (kind === 'pin' && 'tempPin' in res.data) setPin({ id: u.id, tempPin: (res.data as { tempPin: string }).tempPin })
      else setMsg(kind === 'reactivate' ? 'Hesab aktiv edildi ✓' : 'Sessiyalar bağlandı ✓')
      await search()
    } else {
      setMsg('Alınmadı')
    }
  }

  return (
    <div>
      <form onSubmit={search} style={{ display: 'flex', gap: 8, marginBottom: 12 }}>
        <input className="inp" value={q} onChange={(e) => setQ(e.target.value)} placeholder="Ad, telefon və ya email…" style={{ maxWidth: 360 }} />
        <button className="btn btn-primary" disabled={q.trim().length < 2}>Axtar</button>
      </form>

      {msg && (
        <div className="card card-pad" style={{ marginBottom: 12, borderColor: 'var(--leaf)', display: 'flex', gap: 8, alignItems: 'center' }}>
          <IconCheck /><span style={{ fontSize: 13 }}>{msg}</span>
        </div>
      )}
      {pin && (
        <div className="card card-pad" style={{ marginBottom: 12, borderColor: 'var(--leaf)' }}>
          <div className="card-title" style={{ display: 'flex', alignItems: 'center', gap: 8 }}><IconCheck /> Yeni müvəqqəti PIN</div>
          <div style={{ fontSize: 13 }}>PIN: <b style={{ fontFamily: "'IBM Plex Mono',monospace", fontSize: 18 }}>{pin.tempPin}</b></div>
          <div className="muted" style={{ fontSize: 12, marginTop: 6 }}>
            Yalnız indi görünür. İşçiyə çatdırın — ilk girişdə öz PIN-ini təyin edəcək. Köhnə sessiyalar bağlandı.
          </div>
        </div>
      )}

      <div className="card">
        <table className="tbl">
          <thead>
            <tr><th>İşçi</th><th>Şirkət</th><th>Telefon</th><th>Rol</th><th>Status</th><th /></tr>
          </thead>
          <tbody>
            {loading && <tr><td colSpan={6} className="muted" style={{ padding: 18 }}>Axtarılır…</td></tr>}
            {!loading && !searched && (
              <tr><td colSpan={6} className="muted" style={{ padding: 18 }}>Ad, telefon və ya email yazın (ən azı 2 simvol) və axtarın.</td></tr>
            )}
            {!loading && searched && rows.length === 0 && (
              <tr><td colSpan={6} className="muted" style={{ padding: 18 }}>Tapılmadı</td></tr>
            )}
            {rows.map((u) => (
              <tr key={u.id} style={{ opacity: u.isActive ? 1 : 0.55 }}>
                <td>
                  <div style={{ fontWeight: 700 }}>{u.fullName}</div>
                  {u.email && <div style={{ fontSize: 11, color: 'var(--c400)' }}>{u.email}</div>}
                </td>
                <td style={{ fontSize: 13 }}>{u.tenantName ?? u.tenantSlug ?? '—'}</td>
                <td style={{ fontSize: 13 }}>{u.phone ? `0${u.phone}` : '—'}</td>
                <td style={{ fontSize: 13 }}>{u.role}</td>
                <td>
                  {u.isActive
                    ? <span className="tag" style={{ background: 'var(--leaf-bg)', color: 'var(--leaf-d)' }}>Aktiv</span>
                    : <span className="tag" style={{ background: 'rgba(154,52,18,0.12)', color: '#9a3412' }}>Söndürülüb</span>}
                </td>
                <td style={{ textAlign: 'right', whiteSpace: 'nowrap' }}>
                  {canUsers ? (
                    <>
                      <button className="btn btn-sm" disabled={busyId === u.id} onClick={() => void act(u, 'pin')}>PIN sıfırla</button>{' '}
                      {!u.isActive && (
                        <button className="btn btn-sm" disabled={busyId === u.id} onClick={() => void act(u, 'reactivate')}>Aktiv et</button>
                      )}{' '}
                      <button className="btn btn-sm" disabled={busyId === u.id} onClick={() => void act(u, 'revoke')}>Sessiyaları bağla</button>
                    </>
                  ) : (
                    <span className="muted" style={{ fontSize: 12 }}>—</span>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  )
}

// ── İcmal: platform-wide numbers + the operator's to-do list ──────────────────
export function SuperOverview() {
  const [d, setD] = useState<SuperDashboard | null>(null)
  const [loading, setLoading] = useState(true)
  const [err, setErr] = useState<string | null>(null)

  useEffect(() => {
    void (async () => {
      const { status, data } = await getSuperDashboard()
      setLoading(false)
      if (status === 200 && data && !('error' in data)) setD(data as SuperDashboard)
      else if (status === 403) setErr('İcazəniz yoxdur')
      else setErr('Yüklənmədi')
    })()
  }, [])

  if (loading) return <div className="muted" style={{ padding: 18 }}>Yüklənir…</div>
  if (err || !d) return <div className="fb fb-err"><IconX /><span>{err ?? 'Xəta'}</span></div>

  const tiles = [
    { v: d.totalTenants, l: 'Şirkət' },
    { v: d.activeTenants, l: 'Aktiv şirkət' },
    { v: d.totalEmployees, l: 'İşçi (aktiv)' },
    { v: d.checkInsToday, l: 'Bu gün giriş' },
    { v: d.checkInsThisMonth, l: 'Bu ay giriş' },
  ]

  return (
    <div>
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(150px, 1fr))', gap: 12, marginBottom: 16 }}>
        {tiles.map((t) => (
          <div key={t.l} className="card card-pad">
            <div style={{ fontSize: 28, fontWeight: 800, color: 'var(--c900)', fontVariantNumeric: 'tabular-nums' }}>{t.v}</div>
            <div className="muted" style={{ fontSize: 12 }}>{t.l}</div>
          </div>
        ))}
      </div>

      <div className="card card-pad">
        <div className="card-title">Diqqət tələb edir</div>
        {d.attention.length === 0 ? (
          <div className="muted" style={{ fontSize: 13 }}>Hər şey qaydasındadır ✓</div>
        ) : (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
            {d.attention.map((a) => (
              <div key={a.id} style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 8 }}>
                <div>
                  <div style={{ fontWeight: 700 }}>{a.displayName}</div>
                  <div style={{ fontSize: 11, color: 'var(--c400)' }}>{a.slug}.qrlog.az</div>
                </div>
                <span className="tag" style={{ background: 'rgba(154,52,18,0.12)', color: '#9a3412', whiteSpace: 'nowrap' }}>{a.reason}</span>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  )
}

// ── Audit: the platform action trail ────────────────────────────────────────
export function SuperAudit() {
  const [rows, setRows] = useState<SuperAuditEntry[]>([])
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    void (async () => {
      const { status, data } = await getSuperAudit(200)
      setLoading(false)
      if (status === 200 && Array.isArray(data)) setRows(data)
    })()
  }, [])

  return (
    <div className="card">
      <table className="tbl">
        <thead>
          <tr>
            <th>Vaxt</th>
            <th>Kim</th>
            <th>Əməliyyat</th>
            <th>Şirkət</th>
            <th>Detal</th>
          </tr>
        </thead>
        <tbody>
          {loading && <tr><td colSpan={5} className="muted" style={{ padding: 18 }}>Yüklənir…</td></tr>}
          {!loading && rows.length === 0 && <tr><td colSpan={5} className="muted" style={{ padding: 18 }}>Hələ qeyd yoxdur</td></tr>}
          {rows.map((a) => (
            <tr key={a.id}>
              <td style={{ fontSize: 12, whiteSpace: 'nowrap' }}>{fmtDateTime(a.createdAtUtc)}</td>
              <td style={{ fontSize: 13, fontWeight: 600 }}>{a.actorName || '—'}</td>
              <td><span className="tag">{AUDIT_LABELS[a.action] ?? a.action}</span></td>
              <td style={{ fontSize: 13 }}>{a.targetTenantSlug ?? '—'}</td>
              <td style={{ fontSize: 12, color: 'var(--c400)' }}>{a.details ?? ''}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

// ── Şirkətlər: create + list + enable/disable (the original panel) ──────────
export function TenantsTab() {
  const canManage = useCan('ManageTenants')
  const canImpersonate = useCan('Impersonate')
  const [rows, setRows] = useState<SuperTenant[]>([])
  const [loading, setLoading] = useState(true)
  const [showForm, setShowForm] = useState(false)
  const [form, setForm] = useState({ ...EMPTY })
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [created, setCreated] = useState<CreateTenantResult | null>(null)
  const [busyId, setBusyId] = useState<string | null>(null)
  const [copied, setCopied] = useState(false)
  const [features, setFeatures] = useState<SuperFeature[]>([])
  const [planEdit, setPlanEdit] = useState<SuperTenant | null>(null)
  const [planForm, setPlanForm] = useState({ plan: '', maxEmployees: '', maxLocations: '', priceOverride: '', disabled: [] as string[] })
  const [savingPlan, setSavingPlan] = useState(false)

  async function refresh() {
    const { status, data } = await getSuperTenants()
    setLoading(false)
    if (status === 200 && Array.isArray(data)) setRows(data)
    else if (status === 403) setError('İcazəniz yoxdur')
  }

  useEffect(() => {
    void refresh()
    void (async () => {
      const { status, data } = await getSuperFeatures()
      if (status === 200 && Array.isArray(data)) setFeatures(data)
    })()
  }, [])

  function openPlan(t: SuperTenant) {
    setError(null)
    setPlanEdit(t)
    setPlanForm({
      plan: t.plan ?? '',
      maxEmployees: t.maxEmployees != null ? String(t.maxEmployees) : '',
      maxLocations: t.maxLocations != null ? String(t.maxLocations) : '',
      priceOverride: t.monthlyPriceOverride != null ? String(t.monthlyPriceOverride) : '',
      disabled: [...t.disabledFeatures],
    })
  }

  function toggleDisabled(key: string) {
    setPlanForm((f) => ({
      ...f,
      disabled: f.disabled.includes(key) ? f.disabled.filter((k) => k !== key) : [...f.disabled, key],
    }))
  }

  async function savePlan() {
    if (!planEdit) return
    // Validate the override BEFORE sending: an empty field clears it (intentional), but a non-empty
    // malformed value must surface as an error — never silently become NaN → null and wipe the price.
    const raw = planForm.priceOverride.trim()
    let override: number | null = null
    if (raw) {
      const n = parseMoney(raw)
      if (n === null) { setError('Fərdi qiymət yanlışdır (məs. 1250 və ya 1250.50)'); return }
      override = n
    }
    setSavingPlan(true)
    const { status } = await setTenantPlan(planEdit.id, {
      plan: planForm.plan || null,
      maxEmployees: planForm.maxEmployees ? Number(planForm.maxEmployees) : null,
      maxLocations: planForm.maxLocations ? Number(planForm.maxLocations) : null,
      monthlyPriceOverride: override,
      disabledFeatures: planForm.disabled,
    })
    setSavingPlan(false)
    if (status === 200) {
      setPlanEdit(null)
      await refresh()
    } else {
      setError('Plan yadda saxlanmadı')
    }
  }

  function set<K extends keyof typeof form>(k: K, v: string) {
    setForm((f) => ({ ...f, [k]: v }))
  }

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    setSaving(true)
    const { status, data } = await createTenant({
      slug: form.slug.trim(),
      displayName: form.displayName.trim() || undefined,
      adminName: form.adminName.trim() || undefined,
      adminPhone: form.adminPhone.trim() || undefined,
      adminPin: form.adminPin.trim() || undefined,
      locationName: form.locationName.trim() || undefined,
    })
    setSaving(false)
    if (status === 200 && data && !('error' in data)) {
      setCreated(data)
      setForm({ ...EMPTY })
      setShowForm(false)
      await refresh()
    } else {
      const code = data && typeof data === 'object' && 'error' in data ? (data as { error: string }).error : ''
      setError(ERRORS[code] ?? 'Yaradılmadı')
    }
  }

  async function toggle(t: SuperTenant) {
    if (t.isActive && !window.confirm(`"${t.displayName}" söndürülsün? ${t.host} açılmayacaq, məlumat qalır.`)) return
    setError(null)
    setBusyId(t.id)
    const { status, data } = await setTenantActive(t.id, !t.isActive)
    setBusyId(null)
    if (status === 200) await refresh()
    else {
      const code = data && typeof data === 'object' && 'error' in data ? (data as { error: string }).error : ''
      setError(ERRORS[code] ?? 'Dəyişmədi')
    }
  }

  async function impersonate(t: SuperTenant) {
    if (!t.isActive) { setError(ERRORS.TenantInactive); return }
    if (!window.confirm(`"${t.displayName}" adminı kimi daxil olub dəstək göstərəsiniz?\n60 dəqiqəlik sessiya — hər addım audit olunur.`)) return
    setError(null)
    setBusyId(t.id)
    const { status, data } = await impersonateTenant(t.id)
    setBusyId(null)
    if (status === 200 && data && !('error' in data)) {
      const r = data as ImpersonateResult
      startImpersonation(r.token, { tenantName: r.tenantName, adminName: r.adminName })
      window.location.href = '/admin'
    } else {
      const code = data && typeof data === 'object' && 'error' in data ? (data as { error: string }).error : ''
      setError(ERRORS[code] ?? 'Alınmadı')
    }
  }

  async function copyHandover() {
    if (!created) return
    const text =
      `Ünvan: https://${created.host}\n` +
      `Telefon: 0${created.adminPhone}\n` +
      `Müvəqqəti PIN: ${created.tempPin}\n` +
      `(ilk girişdə öz PIN-inizi təyin edəcəksiniz)`
    try {
      await navigator.clipboard.writeText(text)
      setCopied(true)
      setTimeout(() => setCopied(false), 1500)
    } catch {
      /* clipboard unavailable */
    }
  }

  return (
    <div>
      <div style={{ display: 'flex', justifyContent: 'flex-end', marginBottom: 12 }}>
        {canManage && !showForm && (
          <button className="btn btn-primary" onClick={() => { setShowForm(true); setCreated(null) }}>
            ＋ Yeni şirkət
          </button>
        )}
      </div>

      {error && (
        <div className="fb fb-err" style={{ marginBottom: 12 }}>
          <IconX />
          <span>{error}</span>
        </div>
      )}

      {created && (
        <div className="card card-pad" style={{ marginBottom: 16, borderColor: 'var(--leaf)' }}>
          <div className="card-title" style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <IconCheck /> «{created.slug}» yaradıldı
          </div>
          <div style={{ fontSize: 13, lineHeight: 1.9 }}>
            Ünvan: <b>https://{created.host}</b>
            <br />
            Admin telefonu: <b>0{created.adminPhone}</b>
            <br />
            Müvəqqəti PIN: <b style={{ fontFamily: "'IBM Plex Mono',monospace", fontSize: 16 }}>{created.tempPin}</b>
          </div>
          <div className="muted" style={{ fontSize: 12, marginTop: 8 }}>
            PIN yalnız indi görünür — saxlanmır, sonra yalnız sıfırlamaq olar. Admin ilk girişdə öz PIN-ini
            təyin edəcək. Sertifikat ilk açılışda alınır, bir neçə saniyə çəkə bilər.
          </div>
          <div style={{ display: 'flex', gap: 8, marginTop: 12 }}>
            <button className="btn btn-sm btn-primary" onClick={copyHandover}>
              {copied ? '✓ Kopyalandı' : 'Məlumatları kopyala'}
            </button>
            <a className="btn btn-sm" href={`https://${created.host}`} target="_blank" rel="noreferrer">
              Ünvanı aç
            </a>
            <button className="btn btn-sm" onClick={() => setCreated(null)}>Bağla</button>
          </div>
        </div>
      )}

      {showForm && (
        <form className="card card-pad" style={{ marginBottom: 16 }} onSubmit={onSubmit}>
          <div className="card-title">Yeni şirkət</div>

          <div className="form-row cols2">
            <div>
              <label className="form-label">Ünvan (subdomain)</label>
              <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
                <input
                  className="inp"
                  value={form.slug}
                  onChange={(e) => set('slug', e.target.value.toLowerCase())}
                  placeholder="məs. yenisirket"
                  required
                />
                <span className="muted" style={{ fontSize: 13, whiteSpace: 'nowrap' }}>.qrlog.az</span>
              </div>
            </div>
            <div>
              <label className="form-label">Şirkətin adı</label>
              <input className="inp" value={form.displayName} onChange={(e) => set('displayName', e.target.value)} placeholder="məs. Yeni Şirkət MMC" />
            </div>
          </div>

          <div className="form-row cols2">
            <div>
              <label className="form-label">Admin adı</label>
              <input className="inp" value={form.adminName} onChange={(e) => set('adminName', e.target.value)} placeholder="Admin" />
            </div>
            <div>
              <label className="form-label">Admin telefonu</label>
              <input className="inp" value={form.adminPhone} onChange={(e) => set('adminPhone', e.target.value)} placeholder="0501234567" required />
            </div>
          </div>

          <div className="form-row cols2">
            <div>
              <label className="form-label">Müvəqqəti PIN</label>
              <input className="inp" value={form.adminPin} onChange={(e) => set('adminPin', e.target.value)} placeholder="boş = avtomatik" maxLength={4} />
              <div className="muted" style={{ fontSize: 11, marginTop: 4 }}>Boş buraxsanız təsadüfi PIN yaranır.</div>
            </div>
            <div>
              <label className="form-label">İlk filialın adı</label>
              <input className="inp" value={form.locationName} onChange={(e) => set('locationName', e.target.value)} placeholder="Baş ofis" />
              <div className="muted" style={{ fontSize: 11, marginTop: 4 }}>Koordinatı admin özü təyin edəcək.</div>
            </div>
          </div>

          <div style={{ display: 'flex', gap: 10, marginTop: 14 }}>
            <button className="btn btn-primary" disabled={saving || !form.slug.trim() || !form.adminPhone.trim()}>
              {saving ? 'Yaradılır…' : 'Şirkəti yarat'}
            </button>
            <button type="button" className="btn" onClick={() => { setShowForm(false); setError(null) }}>Ləğv et</button>
          </div>
        </form>
      )}

      {planEdit && (
        <div className="card card-pad" style={{ marginBottom: 16, borderColor: 'var(--leaf)' }}>
          <div className="card-title">«{planEdit.displayName}» — plan və funksiyalar</div>
          <div className="form-row cols2">
            <div>
              <label className="form-label">Plan</label>
              <select className="inp" value={planForm.plan} onChange={(e) => setPlanForm((f) => ({ ...f, plan: e.target.value }))}>
                <option value="">— (təyin olunmayıb)</option>
                <option value="Start">Start</option>
                <option value="Biznes">Biznes</option>
                <option value="Korporativ">Korporativ</option>
                <option value="Enterprise">Enterprise</option>
              </select>
            </div>
            <div style={{ display: 'flex', gap: 8 }}>
              <div style={{ flex: 1 }}>
                <label className="form-label">Maks. işçi</label>
                <input className="inp" value={planForm.maxEmployees} onChange={(e) => setPlanForm((f) => ({ ...f, maxEmployees: e.target.value.replace(/\D/g, '') }))} placeholder="limitsiz" />
              </div>
              <div style={{ flex: 1 }}>
                <label className="form-label">Maks. filial</label>
                <input className="inp" value={planForm.maxLocations} onChange={(e) => setPlanForm((f) => ({ ...f, maxLocations: e.target.value.replace(/\D/g, '') }))} placeholder="limitsiz" />
              </div>
            </div>
            <div style={{ marginTop: 10 }}>
              <label className="form-label">Fərdi aylıq qiymət (₼)</label>
              <input
                className="inp"
                value={planForm.priceOverride}
                onChange={(e) => setPlanForm((f) => ({ ...f, priceOverride: moneyInputFilter(e.target.value) }))}
                placeholder="boş = pilləli tarif (işçi sayına görə)"
              />
              <div className="muted" style={{ fontSize: 11, marginTop: 4 }}>
                {planForm.priceOverride.trim()
                  ? (parseMoney(planForm.priceOverride) !== null
                      ? <>= <b>{formatMoney(parseMoney(planForm.priceOverride)!)}</b> / ay</>
                      : <span style={{ color: 'var(--clay)' }}>Qiymət yanlışdır — məs. 1250 və ya 1250.50</span>)
                  : 'Böyük/razılaşdırılmış hesablar üçün. Boş buraxsanız qiymət işçi sayından hesablanır.'}
              </div>
            </div>
          </div>
          <div style={{ marginTop: 10 }}>
            <label className="form-label">Funksiyalar (işarəli = aktiv)</label>
            <div style={{ display: 'flex', flexWrap: 'wrap', gap: 14, marginTop: 6 }}>
              {features.map((f) => (
                <label key={f.key} style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 13, cursor: 'pointer' }}>
                  <input type="checkbox" checked={!planForm.disabled.includes(f.key)} onChange={() => toggleDisabled(f.key)} />
                  {f.label}
                </label>
              ))}
            </div>
          </div>
          <div style={{ display: 'flex', gap: 10, marginTop: 14 }}>
            <button className="btn btn-primary" disabled={savingPlan} onClick={() => void savePlan()}>
              {savingPlan ? 'Yadda saxlanır…' : 'Yadda saxla'}
            </button>
            <button className="btn" onClick={() => setPlanEdit(null)}>Bağla</button>
          </div>
        </div>
      )}

      <div className="card">
        <table className="tbl">
          <thead>
            <tr>
              <th>Şirkət</th>
              <th>Ünvan</th>
              <th className="num">İşçi</th>
              <th className="num">Filial</th>
              <th>Son skan</th>
              <th>Status</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {loading && (
              <tr><td colSpan={7} className="muted" style={{ padding: 18 }}>Yüklənir…</td></tr>
            )}
            {!loading && rows.length === 0 && (
              <tr><td colSpan={7} className="muted" style={{ padding: 18 }}>Şirkət yoxdur</td></tr>
            )}
            {rows.map((t) => (
              <tr key={t.id} style={{ opacity: t.isActive ? 1 : 0.55 }}>
                <td>
                  <div style={{ fontWeight: 700 }}>{t.displayName}</div>
                  <div style={{ fontSize: 11, color: 'var(--c400)' }}>
                    {fmtDate(t.createdAtUtc.slice(0, 10))} tarixindən
                    {t.plan ? <> · <span style={{ fontWeight: 600, color: 'var(--leaf-d)' }}>{t.plan}</span></> : null}
                  </div>
                </td>
                <td>
                  <a href={`https://${t.host}`} target="_blank" rel="noreferrer" style={{ fontSize: 13 }}>{t.host}</a>
                </td>
                <td className="num">
                  {t.employeeCount}
                  {t.maxEmployees != null && (
                    <span style={{ fontSize: 11, color: t.employeeCount > t.maxEmployees ? 'var(--clay)' : 'var(--c400)' }}> /{t.maxEmployees}</span>
                  )}
                </td>
                <td className="num">{t.locationCount}</td>
                <td style={{ fontSize: 13 }}>
                  {t.lastScanDate ? fmtDate(t.lastScanDate) : <span style={{ color: 'var(--clay)' }}>heç vaxt</span>}
                </td>
                <td>
                  {t.isActive ? (
                    <span className="tag" style={{ background: 'var(--leaf-bg)', color: 'var(--leaf-d)' }}>Aktiv</span>
                  ) : (
                    <span className="tag" style={{ background: 'rgba(154,52,18,0.12)', color: '#9a3412' }}>Söndürülüb</span>
                  )}
                </td>
                <td style={{ textAlign: 'right', whiteSpace: 'nowrap' }}>
                  {canManage && (
                    <button className="btn btn-sm" disabled={busyId === t.id} onClick={() => openPlan(t)} title="Plan, limit və funksiyalar">
                      Plan
                    </button>
                  )}{' '}
                  {t.isActive && canImpersonate && (
                    <button className="btn btn-sm" disabled={busyId === t.id} onClick={() => void impersonate(t)} title="Admin kimi daxil ol (dəstək)">
                      Daxil ol
                    </button>
                  )}{' '}
                  {canManage && (
                    <button className="btn btn-sm" disabled={busyId === t.id} onClick={() => void toggle(t)}>
                      {busyId === t.id ? '…' : t.isActive ? 'Söndür' : 'Aç'}
                    </button>
                  )}
                  {!canManage && !canImpersonate && <span className="muted" style={{ fontSize: 12 }}>—</span>}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <div className="muted" style={{ fontSize: 12, marginTop: 12, display: 'flex', gap: 6, alignItems: 'flex-start' }}>
        <IconUsers />
        <span>
          Söndürülmüş şirkətin ünvanı açılmır və heç kim girə bilmir, amma bütün məlumatı olduğu kimi qalır —
          yenidən açanda hər şey yerindədir.
        </span>
      </div>
    </div>
  )
}
