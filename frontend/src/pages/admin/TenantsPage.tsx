import { useEffect, useState, type FormEvent } from 'react'
import {
  createTenant,
  getSuperTenants,
  setTenantActive,
  type CreateTenantResult,
  type SuperTenant,
} from '../../api/admin'
import { fmtDate } from '../../lib/format'
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
}

const EMPTY = { slug: '', displayName: '', adminName: '', adminPhone: '', adminPin: '', locationName: '' }

export function TenantsPage() {
  const [rows, setRows] = useState<SuperTenant[]>([])
  const [loading, setLoading] = useState(true)
  const [showForm, setShowForm] = useState(false)
  const [form, setForm] = useState({ ...EMPTY })
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [created, setCreated] = useState<CreateTenantResult | null>(null)
  const [busyId, setBusyId] = useState<string | null>(null)
  const [copied, setCopied] = useState(false)

  async function refresh() {
    const { status, data } = await getSuperTenants()
    setLoading(false)
    if (status === 200 && Array.isArray(data)) setRows(data)
    else if (status === 403) setError('İcazəniz yoxdur')
  }

  useEffect(() => {
    void refresh()
  }, [])

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

  async function copyHandover() {
    if (!created) return
    // The one thing the operator has to pass on, in a form they can paste into a message.
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
      <div style={{ display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between', gap: 12, marginBottom: 14 }}>
        <div>
          <h1 style={{ fontSize: 18, fontWeight: 800, color: 'var(--c900)' }}>Şirkətlər</h1>
          <div className="muted" style={{ fontSize: 13 }}>
            Bütün müştərilər. Yeni şirkət yaradan kimi ünvanı öz-özünə açılır.
          </div>
        </div>
        {!showForm && (
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

      {/* Shown once, right after creation: the temp PIN is hashed on save and cannot be read back. */}
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
                  <div style={{ fontSize: 11, color: 'var(--c400)' }}>{fmtDate(t.createdAtUtc.slice(0, 10))} tarixindən</div>
                </td>
                <td>
                  <a href={`https://${t.host}`} target="_blank" rel="noreferrer" style={{ fontSize: 13 }}>{t.host}</a>
                </td>
                <td className="num">{t.employeeCount}</td>
                <td className="num">{t.locationCount}</td>
                <td style={{ fontSize: 13 }}>
                  {/* The honest "is anyone using this" column — created-at cannot tell you that. */}
                  {t.lastScanDate ? fmtDate(t.lastScanDate) : <span style={{ color: 'var(--clay)' }}>heç vaxt</span>}
                </td>
                <td>
                  {t.isActive ? (
                    <span className="tag" style={{ background: 'var(--leaf-bg)', color: 'var(--leaf-d)' }}>Aktiv</span>
                  ) : (
                    <span className="tag" style={{ background: 'rgba(154,52,18,0.12)', color: '#9a3412' }}>Söndürülüb</span>
                  )}
                </td>
                <td style={{ textAlign: 'right' }}>
                  <button className="btn btn-sm" disabled={busyId === t.id} onClick={() => void toggle(t)}>
                    {busyId === t.id ? '…' : t.isActive ? 'Söndür' : 'Aç'}
                  </button>
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
