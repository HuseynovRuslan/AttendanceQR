import { useEffect, useMemo, useState, type ChangeEvent } from 'react'
import {
  bulkImport,
  bulkInvite,
  downloadXlsxTemplate,
  getAdminLocations,
  parseXlsx,
  type AdminLocation,
  type BulkImportResult,
  type BulkInviteResult,
} from '../../api/admin'
import type { Role } from '../../lib/jwt'
import { parseBulkText } from './bulkParse'
import { IconCheck, IconX } from '../../components/icons'

const ROLE_LABEL: Record<Role, string> = { Employee: 'İşçi', Manager: 'Menecer', Admin: 'Admin' }

type Mode = 'pin' | 'link'

const ERROR_AZ: Record<string, string> = {
  NameRequired: 'Ad boşdur',
  NeedEmailOrPhone: 'Telefon və ya email lazımdır',
  PhoneAlreadyExists: 'Bu nömrə artıq mövcuddur',
  EmailAlreadyExists: 'Bu email artıq mövcuddur',
  LocationNotFound: 'Bu adda filial yoxdur — adı dəqiq yazın və ya boş buraxın',
  RoleNotRecognised: 'Rol tanınmadı — İşçi / Menecer / Admin yazın və ya boş buraxın',
}


function activationLink(url: string): string {
  return `${window.location.origin}${url}`
}

export function BulkInvitePage() {
  const [locations, setLocations] = useState<AdminLocation[]>([])
  const [locationId, setLocationId] = useState('')
  const [role, setRole] = useState<Role>('Employee')
  const [mode, setMode] = useState<Mode>('pin')
  const [text, setText] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [pinResult, setPinResult] = useState<BulkImportResult | null>(null)
  const [linkResult, setLinkResult] = useState<BulkInviteResult | null>(null)
  const [copied, setCopied] = useState<string | null>(null)
  const [importing, setImporting] = useState(false)

  useEffect(() => {
    void getAdminLocations().then((r) => {
      if (r.status === 200 && Array.isArray(r.data)) {
        setLocations(r.data)
        setLocationId(r.data[0]?.id ?? '')
      }
    })
  }, [])

  const rows = useMemo(() => parseBulkText(text), [text])
  const failed = pinResult?.failed ?? linkResult?.failed ?? []

  async function submit() {
    if (rows.length === 0 || !locationId) return
    setBusy(true)
    setError(null)
    setPinResult(null)
    setLinkResult(null)

    if (mode === 'pin') {
      const { status, data } = await bulkImport({ locationId, role, rows })
      setBusy(false)
      if (status === 200 && data && 'created' in data) {
        setPinResult(data)
        setText('')
      } else setError(status === 403 ? 'İcazəniz yoxdur' : 'Əlavə edilmədi')
    } else {
      const { status, data } = await bulkInvite({ locationId, role, rows })
      setBusy(false)
      if (status === 200 && data && 'created' in data) {
        setLinkResult(data)
        setText('')
      } else setError(status === 403 ? 'İcazəniz yoxdur' : 'Əlavə edilmədi')
    }
  }

  // A chosen file fills the textarea, then the normal preview + submit flow takes over. .csv is read
  // in the browser; .xlsx is parsed on the server (no risky xlsx dependency in the bundle).
  async function onFile(e: ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0]
    e.target.value = '' // let the same file be re-picked
    if (!file) return
    setError(null)
    if (file.name.toLowerCase().endsWith('.csv')) {
      setText(await file.text())
      return
    }
    setImporting(true)
    const { status, rows: parsed } = await parseXlsx(file)
    setImporting(false)
    if (status !== 200) {
      setError(status === 403 ? 'İcazəniz yoxdur' : 'Fayl oxunmadı')
      return
    }
    if (parsed.length === 0) {
      setError('Faylda işçi sətri tapılmadı')
      return
    }
    // Back into the textarea in the template's column order, so the preview shows exactly what the
    // file said and the admin can still edit it before importing. Empty cells stay as empty fields —
    // parse() reads them positionally.
    const lines = parsed.map((r) => {
      const cells = [
        r.fullName,
        r.phoneNumber ?? '',
        r.position ?? '',
        r.fatherName ?? '',
        r.birthYear != null ? String(r.birthYear) : '',
        r.email ?? '',
        r.roleName ?? '',
        r.locationName ?? '',
      ]
      while (cells.length > 1 && cells[cells.length - 1] === '') cells.pop()
      return cells.join(', ')
    })
    setText(lines.join('\n'))
  }

  async function copy(value: string, key: string) {
    try {
      await navigator.clipboard.writeText(value)
      setCopied(key)
      setTimeout(() => setCopied((c) => (c === key ? null : c)), 1500)
    } catch {
      /* clipboard unavailable */
    }
  }

  function copyAllPins() {
    if (!pinResult) return
    const lines = pinResult.created.map((c) => `${c.fullName} · ${c.phoneNumber ?? '—'} · PIN: ${c.tempPin}`)
    void copy(lines.join('\n'), 'all')
  }

  function copyAllLinks() {
    if (!linkResult) return
    const lines = linkResult.created.map((c) => `${c.fullName}: ${activationLink(c.activationUrl)}`)
    void copy(lines.join('\n'), 'all')
  }

  const createdCount = pinResult?.createdCount ?? linkResult?.createdCount ?? 0
  const failedCount = pinResult?.failedCount ?? linkResult?.failedCount ?? 0

  return (
    <div>
      <div style={{ marginBottom: 14 }}>
        <h1 style={{ fontSize: 18, fontWeight: 800, color: 'var(--c900)' }}>Toplu işçi əlavəsi</h1>
        <div className="muted" style={{ fontSize: 13 }}>
          Excel-dən sətirləri kopyalayıb aşağı yapışdırın — hər sətir bir işçi.
        </div>
      </div>

      {error && (
        <div className="fb fb-err" style={{ marginBottom: 12 }}>
          <IconX />
          <span>{error}</span>
        </div>
      )}

      <div className="card card-pad" style={{ marginBottom: 16 }}>
        {/* How each new account gets its first PIN. */}
        <label className="form-label">Necə əlavə olunsun?</label>
        <div style={{ display: 'flex', gap: 8, marginBottom: 14, flexWrap: 'wrap' }}>
          <button
            type="button"
            className={`btn ${mode === 'pin' ? 'btn-primary' : ''}`}
            onClick={() => setMode('pin')}
          >
            Müvəqqəti PIN
          </button>
          <button
            type="button"
            className={`btn ${mode === 'link' ? 'btn-primary' : ''}`}
            onClick={() => setMode('link')}
          >
            Aktivləşdirmə linki
          </button>
        </div>
        <div className="muted" style={{ fontSize: 12, marginBottom: 14 }}>
          {mode === 'pin'
            ? 'Hər işçiyə müvəqqəti PIN yaranır. Onu işçiyə verin — ilk girişdə öz PIN-ini təyin edəcək.'
            : 'Hər işçi üçün aktivləşdirmə linki yaranır — link göndərilir, işçi açıb PIN təyin edir.'}
        </div>

        <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap', marginBottom: 12 }}>
          <div style={{ flex: '1 1 220px' }}>
            <label className="form-label">Lokasiya</label>
            <select className="inp" value={locationId} onChange={(e) => setLocationId(e.target.value)}>
              {locations.map((l) => (
                <option key={l.id} value={l.id}>{l.name}</option>
              ))}
            </select>
          </div>
          <div style={{ flex: '0 1 180px' }}>
            <label className="form-label">Rol</label>
            <select className="inp" value={role} onChange={(e) => setRole(e.target.value as Role)}>
              <option value="Employee">{ROLE_LABEL.Employee}</option>
              <option value="Manager">{ROLE_LABEL.Manager}</option>
            </select>
          </div>
        </div>

        <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 10, flexWrap: 'wrap' }}>
          <button type="button" className="btn btn-sm" onClick={() => void downloadXlsxTemplate()}>
            📥 Excel şablonu yüklə
          </button>
          <label className="btn" style={{ cursor: importing ? 'default' : 'pointer' }}>
            {importing ? 'Oxunur…' : '📄 Excel / CSV faylı seç'}
            <input
              type="file"
              accept=".xlsx,.xls,.csv"
              onChange={onFile}
              disabled={importing}
              style={{ display: 'none' }}
            />
          </label>
          <span className="muted" style={{ fontSize: 12 }}>və ya aşağı birbaşa yapışdırın</span>
        </div>
        <div className="muted" style={{ fontSize: 12, marginBottom: 10 }}>
          Şablonu yükləyin → içini doldurun → «faylı seç» ilə geri yükləyin. Sütunlar başlıqlarına görə
          oxunur, ona görə lazımsız sütunu silə və ya yerini dəyişə bilərsiniz. <b>Yalnız «Ad Soyad» məcburidir.</b>
        </div>

        <label className="form-label">İşçilər — hər sətir bir nəfər</label>
        <textarea
          className="inp"
          rows={9}
          value={text}
          onChange={(e) => setText(e.target.value)}
          placeholder={
            'Əli Vəliyev, 0501234567\n' +
            'Ayşə Məmmədova, 0557654321, Mühasib\n' +
            'Rəşad Quliyev, 0701112233, Bağban, Səməd oğlu, 1990, rashad@mail.az'
          }
          style={{ fontFamily: 'inherit', resize: 'vertical' }}
        />
        <div className="muted" style={{ fontSize: 12, marginTop: 6 }}>
          Format: <b>Ad Soyad, Telefon, Vəzifə, Ata adı, Təvəllüd ili, Email, Rol, Filial</b> —
          yalnız ad məcburidir, qalanını buraxa bilərsiniz. Vergül və ya tab ilə ayırın.
          <br />
          Ortadakı sahəni ötürmək üçün yerini boş saxlayın:{' '}
          <code style={{ fontSize: 11 }}>Əli Vəliyev, 0501234567, , , , ali@mail.az</code>
        </div>

        <div style={{ display: 'flex', alignItems: 'center', gap: 12, marginTop: 14 }}>
          <button className="btn btn-primary" disabled={busy || rows.length === 0 || !locationId} onClick={submit}>
            {busy ? 'Əlavə edilir…' : `${rows.length} işçini əlavə et`}
          </button>
          {rows.length > 0 && !busy && (
            <span className="muted" style={{ fontSize: 13 }}>{rows.length} sətir oxundu</span>
          )}
        </div>
      </div>

      {(pinResult || linkResult) && (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
          <div className="fb fb-ok">
            <IconCheck />
            <span>
              {createdCount} işçi əlavə olundu
              {failedCount > 0 ? ` · ${failedCount} sətir keçmədi` : ''}
            </span>
          </div>

          {pinResult && pinResult.created.length > 0 && (
            <section>
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 8, gap: 10, flexWrap: 'wrap' }}>
                <h2 style={{ fontSize: 15, fontWeight: 800, color: 'var(--c900)' }}>Müvəqqəti PIN-lər</h2>
                <button className="btn btn-sm" onClick={copyAllPins}>
                  {copied === 'all' ? '✓ Kopyalandı' : 'Hamısını kopyala'}
                </button>
              </div>
              <div className="muted" style={{ fontSize: 12, marginBottom: 8 }}>
                Hər işçiyə telefon nömrəsini və müvəqqəti PIN-ini verin. İlk girişdə öz PIN-ini təyin edəcək.
              </div>
              <div className="tbl-wrap tbl-cards">
                <table>
                  <thead>
                    <tr><th>İşçi</th><th>Telefon</th><th>Müvəqqəti PIN</th><th /></tr>
                  </thead>
                  <tbody>
                    {pinResult.created.map((c) => (
                      <tr key={c.employeeId}>
                        <td style={{ fontWeight: 700, color: 'var(--c900)' }}>{c.fullName}</td>
                        <td className="mono">{c.phoneNumber ?? '—'}</td>
                        <td className="mono" style={{ fontWeight: 800, letterSpacing: 2, color: 'var(--c900)' }}>{c.tempPin}</td>
                        <td style={{ textAlign: 'right' }}>
                          <button
                            className="btn btn-sm"
                            onClick={() => copy(`${c.phoneNumber ?? ''} · PIN: ${c.tempPin}`, c.employeeId)}
                          >
                            {copied === c.employeeId ? '✓' : 'Kopyala'}
                          </button>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </section>
          )}

          {linkResult && linkResult.created.length > 0 && (
            <section>
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 8, gap: 10, flexWrap: 'wrap' }}>
                <h2 style={{ fontSize: 15, fontWeight: 800, color: 'var(--c900)' }}>Aktivləşdirmə linkləri</h2>
                <button className="btn btn-sm" onClick={copyAllLinks}>
                  {copied === 'all' ? '✓ Kopyalandı' : 'Hamısını kopyala'}
                </button>
              </div>
              <div className="muted" style={{ fontSize: 12, marginBottom: 8 }}>
                Hər işçiyə öz linkini göndərin — o, linki açıb PIN təyin edərək hesabını aktivləşdirəcək.
              </div>
              <div className="tbl-wrap tbl-cards">
                <table>
                  <thead>
                    <tr><th>İşçi</th><th>Telefon</th><th>Aktivləşdirmə linki</th><th /></tr>
                  </thead>
                  <tbody>
                    {linkResult.created.map((c) => {
                      const link = activationLink(c.activationUrl)
                      return (
                        <tr key={c.employeeId}>
                          <td style={{ fontWeight: 700, color: 'var(--c900)' }}>{c.fullName}</td>
                          <td className="mono">{c.phoneNumber ?? '—'}</td>
                          <td className="mono" style={{ maxWidth: 320, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                            {link}
                          </td>
                          <td style={{ textAlign: 'right' }}>
                            <button className="btn btn-sm" onClick={() => copy(link, c.employeeId)}>
                              {copied === c.employeeId ? '✓' : 'Kopyala'}
                            </button>
                          </td>
                        </tr>
                      )
                    })}
                  </tbody>
                </table>
              </div>
            </section>
          )}

          {failed.length > 0 && (
            <section>
              <h2 style={{ fontSize: 15, fontWeight: 800, color: '#991b1b', marginBottom: 8 }}>
                Keçməyən sətirlər ({failed.length})
              </h2>
              <div className="tbl-wrap tbl-cards">
                <table>
                  <thead><tr><th>İşçi</th><th>Səbəb</th></tr></thead>
                  <tbody>
                    {failed.map((f, i) => (
                      <tr key={`${f.fullName}-${i}`}>
                        <td>{f.fullName || <span className="muted">(ad yoxdur)</span>}</td>
                        <td style={{ color: '#b91c1c' }}>{ERROR_AZ[f.error] ?? f.error}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </section>
          )}
        </div>
      )}
    </div>
  )
}
