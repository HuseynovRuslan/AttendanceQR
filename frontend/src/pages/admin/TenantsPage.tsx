import { useEffect, useState, type FormEvent } from 'react'
import { slugify, withSuffix } from '../../lib/tenantSlug'
import { RowActions } from '../../components/RowActions'
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
  getImpersonationTargets,
  impersonateTenant,
  viewTenant,
  type ImpersonationTarget,
  setTenantAdmin,
  type SetTenantAdminResult,
  getTenantDeletable,
  deleteTenant,
  type TenantDeletable,
  type DeleteTenantResult,
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
import { COMPANY_TZ, fmtDate } from '../../lib/format'
import { parseMoney, moneyInputFilter, formatMoney } from '../../lib/money'
import { useCan } from '../operator/OperatorContext'
import { IconCheck, IconUsers, IconX } from '../../components/icons'

/** EF entity names as the operator knows them. Anything unmapped falls through as-is. */
const TABLE_NAMES: Record<string, string> = {
  Employee: 'işçi',
  Location: 'filial',
  Schedule: 'növbə',
  JobPosition: 'vəzifə',
  AuditLog: 'audit qeydi',
  Announcement: 'elan',
  NonWorkingDay: 'qeyri-iş günü',
  ManagedLocation: 'filial təyinatı',
  PushSubscription: 'bildiriş abunəsi',
  EmployeeNotification: 'bildiriş',
  DeviceBinding: 'cihaz',
  TaskItem: 'tapşırıq',
  TenantInvoice: 'hesab',
}

const ERRORS: Record<string, string> = {
  NotSuperAdmin: 'İcazəniz yoxdur',
  SlugInvalid: 'Ünvan yalnız kiçik hərf, rəqəm və tire ola bilər (2–20 simvol)',
  SlugReserved: 'Bu ünvan sistem üçün ayrılıb — başqasını seçin',
  SlugTaken: 'Bu ünvan artıq istifadə olunur',
  ConfirmMismatch: 'Yazdığınız ad şirkətin adı ilə üst-üstə düşmür',
  TenantHasHistory: 'Bu şirkətdə davamiyyət tarixçəsi var — silinmir, söndürün',
  TenantIsActive: 'Əvvəlcə şirkəti söndürün',
  TenantHasInvoices: 'Bu şirkətə hesab kəsilib — silinmir',
  TenantHasOperator: 'Bu şirkətin içində operator hesabı var',
  PhoneAlreadyExists: 'Bu nömrə həmin şirkətdə artıq istifadə olunur',
  NoLocation: 'Şirkətdə aktiv filial yoxdur',
  TargetNotFound: 'Bu işçi tapılmadı və ya söndürülüb',
  TargetNotImpersonable: 'Yalnız admin və menecer kimi daxil olmaq olar',
  AdminPhoneInvalid: 'Admin nömrəsi yanlışdır',
  AdminPinInvalid: 'PIN 4 rəqəm olmalıdır',
  AdminPinTooWeak: 'Bu PIN çox sadədir — 1234, 0000, 1212 kimi PIN-lər qəbul edilmir',
  TenantNotFound: 'Şirkət tapılmadı',
  CannotDisableOwnTenant: 'Öz şirkətinizi söndürə bilməzsiniz — panelə girişiniz bağlanardı',
  TenantInactive: 'Şirkət söndürülüb — əvvəl aktiv edin',
  NoAdmin: 'Bu şirkətdə aktiv admin yoxdur',
  CannotImpersonateSelf: 'Öz hesabınıza daxil ola bilməzsiniz',
  CannotImpersonateOperator: 'Operator hesabı kimi daxil olmaq olmaz — müştərinin öz hesabını seçin',
  NoImpersonableAdmin: 'Bu şirkətdə yalnız operator adminlər var — əvvəlcə müştərinin öz adminini yaradın',
}

const EMPTY = { displayName: '', adminName: '', adminPhone: '', adminPin: '', locationName: '' }

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
  // The operator may well be reading this from anywhere; the companies are all in Baku, and so are
  // the events being listed.
  return new Date(iso).toLocaleString('az-AZ', {
    day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit',
    timeZone: COMPANY_TZ,
  })
}

function errorCodeOf(data: unknown): string {
  return data && typeof data === 'object' && 'error' in data ? String((data as { error: unknown }).error) : ''
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
                <div style={{ fontWeight: 700 }}>{a.displayName}</div>
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
  // Once the operator edits the address by hand, the company name stops writing over it.
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
  const [planForm, setPlanForm] = useState({ plan: '', maxEmployees: '', maxLocations: '', priceOverride: '', trialEnds: '', disabled: [] as string[] })
  const [savingPlan, setSavingPlan] = useState(false)
  // Naming the customer's admin — the handover, which now happens after the company is built rather
  // than in the first field of the creation form.
  // The company whose seats are being offered, with the seats themselves — held together so the
  // dialog cannot render a stale list against a different tenant.
  const [impersonateFor, setImpersonateFor] =
    useState<{ tenant: SuperTenant; targets: ImpersonationTarget[] } | null>(null)
  const [adminFor, setAdminFor] = useState<SuperTenant | null>(null)
  const [adminForm, setAdminForm] = useState({ fullName: '', phone: '', pin: '' })
  const [savingAdmin, setSavingAdmin] = useState(false)
  const [adminIssued, setAdminIssued] = useState<{ tenant: string; result: SetTenantAdminResult } | null>(null)
  // Deleting a company: what it would destroy, and the typed name that has to match before it can.
  const [deleteFor, setDeleteFor] = useState<TenantDeletable | null>(null)
  const [deleteTyped, setDeleteTyped] = useState('')
  const [deleting, setDeleting] = useState(false)
  const [deleted, setDeleted] = useState<{ name: string; result: DeleteTenantResult } | null>(null)

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
      // Seeded from the tenant so saving the form cannot quietly clear a demo — see TenantPlanInput.
      trialEnds: t.trialEndsAtUtc ? t.trialEndsAtUtc.slice(0, 10) : '',
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
      trialEndsAtUtc: planForm.trialEnds || null,
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
    const payload = {
      displayName: form.displayName.trim() || undefined,
      adminName: form.adminName.trim() || undefined,
      adminPhone: form.adminPhone.trim() || undefined,
      adminPin: form.adminPin.trim() || undefined,
      locationName: form.locationName.trim() || undefined,
    }

    // The address is derived and no longer on screen, so a clash cannot be handed back to the operator
    // to fix in a field they cannot see. A second "Yeni Şirkət MMC", or a name that folds onto a
    // reserved label like "app", takes the next free counter here instead of failing the form.
    const base = slugify(form.displayName) || 'sirket'
    let result = await createTenant({ ...payload, slug: base })
    for (let n = 2; n <= 20 && ['SlugTaken', 'SlugReserved', 'SlugInvalid'].includes(errorCodeOf(result.data)); n++) {
      result = await createTenant({ ...payload, slug: withSuffix(base, n) })
    }
    const { status, data } = result
    setSaving(false)
    if (status === 200 && data && !('error' in data)) {
      setCreated(data)
      setForm({ ...EMPTY })
      setShowForm(false)
      await refresh()
    } else {
      setError(ERRORS[errorCodeOf(data)] ?? 'Yaradılmadı')
    }
  }

  async function toggle(t: SuperTenant) {
    if (t.isActive && !window.confirm(`"${t.displayName}" söndürülsün? İşçiləri daxil ola bilməyəcək, məlumat qalır.`)) return
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

  // Opening the picker rather than borrowing the admin outright. Half the support calls are about a
  // MANAGER's screen — a branch gate, an empty employee list, a leave they cannot file — and the
  // admin's view is the one screen guaranteed not to reproduce any of them.
  async function impersonate(t: SuperTenant) {
    if (!t.isActive) { setError(ERRORS.TenantInactive); return }
    setError(null)
    setBusyId(t.id)
    const { status, data } = await getImpersonationTargets(t.id)
    setBusyId(null)
    if (status === 200 && Array.isArray(data)) {
      if (data.length === 0) { setError('Daxil olmaq üçün uyğun hesab yoxdur'); return }
      setImpersonateFor({ tenant: t, targets: data })
    } else {
      setError(ERRORS[errorCodeOf(data)] ?? 'Siyahı alınmadı')
    }
  }

  async function borrowSeat(t: SuperTenant, target: ImpersonationTarget) {
    const seat = target.role === 'Admin' ? 'admin' : 'menecer'
    if (!window.confirm(
      `"${t.displayName}" şirkətində ${target.fullName} (${seat}) kimi daxil olursunuz.
` +
      '60 dəqiqəlik sessiya — hər addım audit olunur, şirkət də bunu öz jurnalında görür.')) return
    setError(null)
    setBusyId(t.id)
    const { status, data } = await impersonateTenant(t.id, target.id)
    setBusyId(null)
    if (status === 200 && data && !('error' in data)) {
      const r = data as ImpersonateResult
      setImpersonateFor(null)
      startImpersonation(r.token, { tenantName: r.tenantName, adminName: r.adminName })
      // Both seats land on /admin — the console reads the token's role and shows the manager the
      // reduced version, which is exactly the screen a support call is about.
      window.location.href = '/admin'
    } else {
      setError(ERRORS[errorCodeOf(data)] ?? 'Alınmadı')
    }
  }

  /** «Bax» — open the company's own screens with a session that cannot write. The confirm is
   *  deliberately calm: nothing here can go wrong, which is the whole difference from «Daxil ol». */
  async function view(t: SuperTenant) {
    setError(null)
    setBusyId(t.id)
    const { status, data } = await viewTenant(t.id)
    setBusyId(null)
    if (status === 200 && data && !('error' in data)) {
      const r = data as ImpersonateResult
      startImpersonation(r.token, { tenantName: r.tenantName, adminName: r.adminName, readOnly: true })
      window.location.href = '/admin'
    } else {
      setError(ERRORS[errorCodeOf(data)] ?? 'Alınmadı')
    }
  }

  function openAdmin(t: SuperTenant) {
    setError(null)
    setAdminIssued(null)
    setAdminFor(t)
    setAdminForm({ fullName: '', phone: '', pin: '' })
  }

  async function submitAdmin(e: FormEvent) {
    e.preventDefault()
    if (!adminFor) return
    setError(null)
    setSavingAdmin(true)
    const { status, data } = await setTenantAdmin(adminFor.id, {
      phone: adminForm.phone.trim(),
      fullName: adminForm.fullName.trim() || undefined,
      pin: adminForm.pin.trim() || undefined,
    })
    setSavingAdmin(false)
    if (status === 200 && data && !('error' in data)) {
      setAdminIssued({ tenant: adminFor.displayName, result: data as SetTenantAdminResult })
      setAdminFor(null)
      setCreated(null)
      await refresh()
    } else {
      setError(ERRORS[errorCodeOf(data)] ?? 'Təyin edilmədi')
    }
  }

  async function openDelete(t: SuperTenant) {
    setError(null)
    setDeleteTyped('')
    setBusyId(t.id)
    const { status, data } = await getTenantDeletable(t.id)
    setBusyId(null)
    if (status === 200 && data && !('error' in data)) setDeleteFor(data as TenantDeletable)
    else setError('Yoxlanmadı')
  }

  async function confirmDelete() {
    if (!deleteFor) return
    setError(null)
    setDeleting(true)
    const name = deleteFor.displayName
    const { status, data } = await deleteTenant(deleteFor.id, deleteTyped.trim())
    setDeleting(false)
    if (status === 200 && data && !('error' in data)) {
      // Said out loud, because the row simply vanishing from the table is not an answer to "did it
      // take the photos too" — and storage can fail without failing the request.
      setDeleted({ name, result: data as DeleteTenantResult })
      setDeleteFor(null)
      setCreated(null)
      await refresh()
    } else {
      setError(ERRORS[errorCodeOf(data)] ?? 'Silinmədi')
    }
  }

  async function copyHandover() {
    if (!created) return
    const text =
      `Giriş: https://app.qrlog.az\n` +
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

      {/* A company was just created. If no admin was named, the next step is to BUILD it — not to
          hand credentials to anybody, because there is nobody yet. */}
      {created && !created.tempPin && (
        <div className="card card-pad" style={{ marginBottom: 16, borderColor: 'var(--leaf)' }}>
          <div className="card-title" style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <IconCheck /> «{created.displayName || created.slug}» yaradıldı
          </div>
          <div style={{ fontSize: 13, lineHeight: 1.7 }}>
            Şirkət hazırdır: bir filial («Baş ofis») və iki növbə şablonu yaradıldı.
            <br />
            İndi <b>«Qur»</b> düyməsi ilə içəri keçib filialın koordinatını, iş saatını və işçiləri təyin edin.
            Hazır olanda müştərinin adminini təyin edərsiniz — PIN o vaxt yaranacaq.
          </div>
          <div className="muted" style={{ fontSize: 12, marginTop: 8 }}>
            Filial Bakının mərkəzində yaranır — koordinatı düzəltməsəniz heç kim skan edə bilməyəcək.
          </div>
          <div style={{ display: 'flex', gap: 8, marginTop: 12 }}>
            <button
              className="btn btn-sm btn-primary"
              onClick={() => {
                const t = rows.find((r) => r.id === created.id)
                if (t) void impersonate(t)
              }}
            >
              Şirkəti qur
            </button>
            <button className="btn btn-sm" onClick={() => setCreated(null)}>Bağla</button>
          </div>
        </div>
      )}

      {/* Created WITH an admin named — the old one-step path, still there for a company whose owner
          is known on day one. */}
      {created && created.tempPin && (
        <div className="card card-pad" style={{ marginBottom: 16, borderColor: 'var(--leaf)' }}>
          <div className="card-title" style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <IconCheck /> «{created.displayName || created.slug}» yaradıldı
          </div>
          <div style={{ fontSize: 13, lineHeight: 1.9 }}>
            Giriş: <b>https://app.qrlog.az</b>
            <br />
            Admin telefonu: <b>0{created.adminPhone}</b>
            <br />
            Müvəqqəti PIN: <b style={{ fontFamily: "'IBM Plex Mono',monospace", fontSize: 16 }}>{created.tempPin}</b>
          </div>
          <div className="muted" style={{ fontSize: 12, marginTop: 8 }}>
            PIN yalnız indi görünür — saxlanmır, sonra yalnız sıfırlamaq olar. Admin ilk girişdə öz PIN-ini
            təyin edəcək.
          </div>
          <div style={{ display: 'flex', gap: 8, marginTop: 12 }}>
            <button className="btn btn-sm btn-primary" onClick={copyHandover}>
              {copied ? '✓ Kopyalandı' : 'Məlumatları kopyala'}
            </button>
            <a className="btn btn-sm" href="https://app.qrlog.az" target="_blank" rel="noreferrer">
              Girişi aç
            </a>
            <button className="btn btn-sm" onClick={() => setCreated(null)}>Bağla</button>
          </div>
        </div>
      )}

      {/* The handover itself: the PIN exists for one screenful and is never readable again. */}
      {adminIssued && (
        <div className="card card-pad" style={{ marginBottom: 16, borderColor: 'var(--leaf)' }}>
          <div className="card-title" style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <IconCheck /> «{adminIssued.tenant}» müştəriyə hazırdır
          </div>
          <div style={{ fontSize: 13, lineHeight: 1.9 }}>
            Admin: <b>{adminIssued.result.fullName}</b>
            <br />
            Giriş: <b>https://app.qrlog.az</b>
            <br />
            Telefon: <b>0{adminIssued.result.phone}</b>
            <br />
            Müvəqqəti PIN: <b style={{ fontFamily: "'IBM Plex Mono',monospace", fontSize: 16 }}>{adminIssued.result.tempPin}</b>
          </div>
          <div className="muted" style={{ fontSize: 12, marginTop: 8 }}>
            PIN yalnız indi görünür. Admin ilk girişdə öz PIN-ini təyin edəcək — şəkil istənməyəcək.
          </div>
          <div style={{ display: 'flex', gap: 8, marginTop: 12 }}>
            <button
              className="btn btn-sm btn-primary"
              onClick={() => {
                const r = adminIssued.result
                void navigator.clipboard
                  .writeText(
                    `Giriş: https://app.qrlog.az\nTelefon: 0${r.phone}\nMüvəqqəti PIN: ${r.tempPin}\n` +
                      `(ilk girişdə öz PIN-inizi təyin edəcəksiniz)`,
                  )
                  .then(() => {
                    setCopied(true)
                    setTimeout(() => setCopied(false), 1500)
                  })
                  .catch(() => {})
              }}
            >
              {copied ? '✓ Kopyalandı' : 'Məlumatları kopyala'}
            </button>
            <button className="btn btn-sm" onClick={() => setAdminIssued(null)}>Bağla</button>
          </div>
        </div>
      )}

      {/* Naming the admin. */}
      {/* Whose session to borrow. It used to be nobody's choice: the button took the founding admin,
          which answers an admin's question and none of a manager's. */}
      {impersonateFor && (
        <div className="card card-pad" style={{ marginBottom: 16, borderColor: 'var(--blue)' }}>
          <div className="card-title">
            «{impersonateFor.tenant.displayName}» — kimin hesabı ilə daxil olursunuz?
          </div>
          <div className="muted" style={{ fontSize: 12, marginBottom: 12, lineHeight: 1.6 }}>
            60 dəqiqəlik sessiya. Hər addım audit olunur və şirkət də bunu öz jurnalında görür.
            Menecer seçsəniz onun gördüyü ekranı görəcəksiniz — filial məhdudiyyəti ilə birlikdə.
          </div>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
            {impersonateFor.targets.map((x) => (
              <button
                key={x.id}
                type="button"
                className="btn"
                disabled={busyId === impersonateFor.tenant.id}
                onClick={() => void borrowSeat(impersonateFor.tenant, x)}
                style={{ justifyContent: 'flex-start', textAlign: 'left', padding: '10px 12px' }}
              >
                <span style={{ fontWeight: 700 }}>{x.fullName}</span>
                <span className={`badge ${x.role === 'Admin' ? 'b-present' : 'b-sick'}`} style={{ marginLeft: 8 }}>
                  {x.role === 'Admin' ? 'Admin' : 'Menecer'}
                </span>
                {x.role === 'Manager' && (
                  <span className="muted" style={{ marginLeft: 8, fontSize: 12 }}>
                    {x.branches.length > 0 ? x.branches.join(', ') : 'filial təyin edilməyib'}
                  </span>
                )}
              </button>
            ))}
          </div>
          <button type="button" className="btn btn-sm" style={{ marginTop: 12 }} onClick={() => setImpersonateFor(null)}>
            Ləğv et
          </button>
        </div>
      )}

      {adminFor && (
        <form className="card card-pad" style={{ marginBottom: 16, borderColor: 'var(--leaf)' }} onSubmit={submitAdmin}>
          <div className="card-title">«{adminFor.displayName}» — müştərinin adminini təyin et</div>
          <div className="muted" style={{ fontSize: 12, marginBottom: 12 }}>
            {adminFor.hasAdmin
              ? 'Bu şirkətin artıq admini var — bu, yanına ikinci admin əlavə edəcək.'
              : 'Şirkət qurularkən yaradılan admin hesabı bu şəxsə veriləcək.'}
          </div>
          <div className="form-row cols2">
            <div>
              <label className="form-label">Adı, soyadı</label>
              <input
                className="inp"
                value={adminForm.fullName}
                onChange={(e) => setAdminForm((f) => ({ ...f, fullName: e.target.value }))}
                placeholder="Admin"
              />
            </div>
            <div>
              <label className="form-label">Telefon</label>
              <input
                className="inp"
                type="tel"
                inputMode="tel"
                required
                value={adminForm.phone}
                onChange={(e) => setAdminForm((f) => ({ ...f, phone: e.target.value }))}
                placeholder="0501234567"
              />
            </div>
          </div>
          <div className="form-row cols2">
            <div>
              <label className="form-label">Müvəqqəti PIN</label>
              <input
                className="inp"
                value={adminForm.pin}
                onChange={(e) => setAdminForm((f) => ({ ...f, pin: e.target.value.replace(/\D/g, '') }))}
                placeholder="boş = avtomatik"
                maxLength={4}
              />
            </div>
          </div>
          <div style={{ display: 'flex', gap: 10, marginTop: 4 }}>
            <button className="btn btn-primary" disabled={savingAdmin || !adminForm.phone.trim()}>
              {savingAdmin ? 'Təyin edilir…' : 'Təyin et və PIN ver'}
            </button>
            <button type="button" className="btn" onClick={() => setAdminFor(null)}>Ləğv et</button>
          </div>
        </form>
      )}

      {deleted && (
        <div
          className="card card-pad"
          style={{ marginBottom: 16, borderColor: deleted.result.photosPending > 0 ? 'var(--clay)' : 'var(--leaf)' }}
        >
          <div className="card-title" style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <IconCheck /> «{deleted.name}» silindi
          </div>
          <div style={{ fontSize: 13 }}>
            {deleted.result.rowsDeleted} sətir, {deleted.result.photosDeleted} şəkil silindi.
          </div>
          {deleted.result.photosPending > 0 && (
            <div className="fb fb-err" style={{ marginTop: 10 }}>
              <IconX />
              <span>
                Diqqət: {deleted.result.photosPending} şəkil anbardan silinmədi. Şirkət getdi, şəkillər
                qaldı — server loglarına baxın.
              </span>
            </div>
          )}
          <div style={{ display: 'flex', gap: 8, marginTop: 12 }}>
            <button className="btn btn-sm" onClick={() => setDeleted(null)}>Bağla</button>
          </div>
        </div>
      )}

      {/* Deleting a company. Shown as a panel rather than a browser confirm() because the numbers are
          the safeguard: an operator recognises "3 employees, 1 branch" as their own test company and
          fails to recognise 64 as anything they made. */}
      {deleteFor && (
        <div className="card card-pad" style={{ marginBottom: 16, borderColor: 'var(--clay)' }}>
          <div className="card-title">«{deleteFor.displayName}» silinsin?</div>

          {!deleteFor.canDelete ? (
            <>
              <div style={{ fontSize: 13, lineHeight: 1.7 }}>
                {deleteFor.reason === 'TenantIsActive' && (
                  <>Bu şirkət <b>işləkdir</b>. Silmək üçün əvvəlcə <b>«Söndür»</b> edin — bu geri qaytarıla bilər.</>
                )}
                {deleteFor.reason === 'TenantHasHistory' && (
                  <>
                    Bu şirkətdə <b>davamiyyət tarixçəsi var</b> — {deleteFor.usage.records} skan,{' '}
                    {deleteFor.usage.summaries} günlük yekun, {deleteFor.usage.visits} sahə ziyarəti.
                    <br />
                    Bir skan kiminsə bir günlük maaşıdır, ona görə belə şirkət silinmir. Söndürülmüş
                    halda qalsın: heç kim girə bilməyəcək, məlumat isə olduğu kimi qalacaq.
                  </>
                )}
                {deleteFor.reason === 'TenantHasInvoices' && (
                  <>
                    Bu şirkətə <b>{deleteFor.usage.invoices} hesab</b> kəsilib — yəni müştəridir, skan
                    olub-olmamasından asılı olmayaraq. Hesab tarixçəsi silinməməlidir.
                  </>
                )}
                {deleteFor.reason === 'TenantHasOperator' && (
                  <>
                    Bu şirkətin içində <b>operator hesabı</b> var — silinsə, öz girişinizi itirərsiniz.
                    Əvvəlcə həmin hesabı çıxarın.
                  </>
                )}
              </div>
              <div style={{ display: 'flex', gap: 8, marginTop: 12 }}>
                <button className="btn btn-sm" onClick={() => setDeleteFor(null)}>Bağla</button>
              </div>
            </>
          ) : (
            <>
              <div style={{ fontSize: 13, lineHeight: 1.7 }}>
                Bu şirkətdə heç kim heç vaxt skan etməyib, ona görə silinə bilər. Həmişəlik gedəcək:
              </div>
              <div style={{ fontSize: 13, marginTop: 8, display: 'flex', flexWrap: 'wrap', gap: 6 }}>
                {Object.keys(deleteFor.rows).length === 0 ? (
                  <span className="muted">boş şirkət — silinəcək sətir yoxdur</span>
                ) : (
                  Object.entries(deleteFor.rows).map(([table, n]) => (
                    <span key={table} className="tag">
                      {TABLE_NAMES[table] ?? table}: {n}
                    </span>
                  ))
                )}
              </div>
              <div className="muted" style={{ fontSize: 12, marginTop: 10 }}>
                Geri qaytarmaq olmur. Baza gecə ehtiyat nüsxəsindən bərpa oluna bilər, şəkillər isə yox.
              </div>
              <div style={{ marginTop: 12, maxWidth: 360 }}>
                <label className="form-label">Təsdiq üçün şirkətin adını yazın</label>
                <input
                  className="inp"
                  value={deleteTyped}
                  onChange={(e) => setDeleteTyped(e.target.value)}
                  placeholder={deleteFor.displayName}
                  autoFocus
                />
              </div>
              <div style={{ display: 'flex', gap: 8, marginTop: 12 }}>
                <button
                  className="btn btn-sm btn-danger"
                  disabled={deleting || deleteTyped.trim() !== deleteFor.displayName}
                  onClick={() => void confirmDelete()}
                >
                  {deleting ? 'Silinir…' : 'Həmişəlik sil'}
                </button>
                <button className="btn btn-sm" onClick={() => setDeleteFor(null)}>Ləğv et</button>
              </div>
            </>
          )}
        </div>
      )}

      {showForm && (
        <form className="card card-pad" style={{ marginBottom: 16 }} onSubmit={onSubmit}>
          <div className="card-title">Yeni şirkət</div>

          <div className="form-row cols2">
            <div>
              <label className="form-label">Şirkətin adı</label>
              <input
                className="inp"
                required
                value={form.displayName}
                onChange={(e) => set('displayName', e.target.value)}
                placeholder="məs. Yeni Şirkət MMC"
              />
            </div>
            <div>
              <label className="form-label">Admin telefonu — istəyə bağlı</label>
              <input
                className="inp"
                type="tel"
                inputMode="tel"
                value={form.adminPhone}
                onChange={(e) => set('adminPhone', e.target.value)}
                placeholder="boş buraxın"
              />
              <p className="muted" style={{ fontSize: 12, marginTop: 4 }}>
                Boş buraxsanız şirkəti özünüz qurub, sonda «Admini təyin et» ilə müştəriyə verirsiniz.
              </p>
            </div>
          </div>

          <div className="form-row cols2">
            <div>
              <label className="form-label">Admin adı</label>
              <input className="inp" value={form.adminName} onChange={(e) => set('adminName', e.target.value)} placeholder="Admin" />
            </div>
            <div>
              <label className="form-label">İlk filialın adı</label>
              <input className="inp" value={form.locationName} onChange={(e) => set('locationName', e.target.value)} placeholder="Baş ofis" />
              <div className="muted" style={{ fontSize: 11, marginTop: 4 }}>Koordinatı admin özü təyin edəcək.</div>
            </div>
          </div>

          <div className="form-row cols2">
            <div>
              <label className="form-label">Müvəqqəti PIN</label>
              <input className="inp" value={form.adminPin} onChange={(e) => set('adminPin', e.target.value)} placeholder="boş = avtomatik" maxLength={4} />
              <div className="muted" style={{ fontSize: 11, marginTop: 4 }}>Boş buraxsanız təsadüfi PIN yaranır.</div>
            </div>
          </div>

          <div style={{ display: 'flex', gap: 10, marginTop: 14 }}>
            <button className="btn btn-primary" disabled={saving || !form.displayName.trim()}>
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
            <label className="form-label">Demo bitmə tarixi</label>
            <input
              type="date"
              className="inp"
              style={{ maxWidth: 220 }}
              value={planForm.trialEnds}
              onChange={(e) => setPlanForm((f) => ({ ...f, trialEnds: e.target.value }))}
            />
            <div className="muted" style={{ fontSize: 11, marginTop: 4 }}>
              Bu tarixə qədər müştəri «Demo versiya» görür və heç nə hesablanmır. Boş = adi abunəlik.
              Tarix keçəndə <b>heç nə söndürülmür</b> — sadəcə ekranda demo bitdiyi yazılır.
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
                  {/* Built but not handed over: the admin account exists and belongs to nobody. Without
                      this the operator has no way to tell the two apart from the outside. */}
                  {!t.hasAdmin && (
                    <span className="tag" style={{ background: 'rgba(154,52,18,0.12)', color: '#9a3412', marginTop: 4 }}>
                      admin təyin edilməyib
                    </span>
                  )}
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
                  {/* No permission gate on the block itself: even an operator with NO permissions —
                      «Qrup rəhbəri» — still has the read-only «Bax» entry, which is their only way in.
                      Each individual action carries its own `hidden`, so nobody sees a door they
                      cannot open. */}
                  <RowActions
                      primary={{
                        // The verb changed with the job: this is how the operator gets inside to build
                        // a company, not only how they answer a support question about one.
                        label: t.hasAdmin ? 'Daxil ol' : 'Qur',
                        onClick: () => void impersonate(t),
                        disabled: busyId === t.id,
                        hidden: !t.isActive || !canImpersonate,
                        title: t.hasAdmin ? 'Admin kimi daxil ol (dəstək)' : 'İçəri keçib şirkəti qur',
                      }}
                      actions={[
                        // Read-only entry. Shown to everyone (any operator may read), and it is the
                        // ONLY way in for «Qrup rəhbəri», who has no impersonation permission.
                        {
                          label: 'Bax (yalnız oxu)',
                          onClick: () => void view(t),
                          hidden: !t.isActive || !t.hasAdmin,
                        },
                        {
                          label: t.hasAdmin ? 'Admin əlavə et' : 'Admini təyin et',
                          onClick: () => openAdmin(t),
                          hidden: !canManage,
                        },
                        { label: 'Plan və limitlər', onClick: () => openPlan(t), hidden: !canManage },
                        {
                          label: t.isActive ? 'Söndür' : 'Aç',
                          onClick: () => void toggle(t),
                          danger: t.isActive,
                          disabled: busyId === t.id,
                          hidden: !canManage,
                        },
                        {
                          // Only for a company that is already switched off. A live customer's menu
                          // has no delete in it at all — which matters because RowActions groups every
                          // danger item together, so "Söndür" and "Şirkəti sil" would be neighbours.
                          label: 'Şirkəti sil',
                          onClick: () => void openDelete(t),
                          danger: true,
                          disabled: busyId === t.id,
                          hidden: !canManage || t.isActive,
                          title: 'Yalnız heç vaxt skan olunmamış şirkət silinə bilər',
                        },
                    ]}
                  />
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
