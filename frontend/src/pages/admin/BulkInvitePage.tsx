import { useEffect, useMemo, useState } from 'react'
import {
  bulkImport,
  bulkInvite,
  getAdminLocations,
  type AdminLocation,
  type BulkImportResult,
  type BulkInviteResult,
} from '../../api/admin'
import type { Role } from '../../lib/jwt'
import { IconCheck, IconX } from '../../components/icons'

const ROLE_LABEL: Record<Role, string> = { Employee: 'İşçi', Manager: 'Menecer', Admin: 'Admin' }

type Mode = 'pin' | 'link'

const ERROR_AZ: Record<string, string> = {
  NameRequired: 'Ad boşdur',
  NeedEmailOrPhone: 'Telefon və ya email lazımdır',
  PhoneAlreadyExists: 'Bu nömrə artıq mövcuddur',
  EmailAlreadyExists: 'Bu email artıq mövcuddur',
}

interface ParsedRow {
  fullName: string
  phoneNumber?: string
  position?: string
}

// Each line = one employee. Fields separated by comma / tab / semicolon, so pasting straight from an
// Excel selection (tab-separated) works:  Ad Soyad, telefon, vəzifə(istəyə bağlı)
function parse(text: string): ParsedRow[] {
  return text
    .split('\n')
    .map((line) => line.trim())
    .filter((line) => line.length > 0)
    .map((line) => {
      const parts = line.split(/[,\t;]+/).map((p) => p.trim())
      return {
        fullName: parts[0] ?? '',
        phoneNumber: parts[1] || undefined,
        position: parts[2] || undefined,
      }
    })
    .filter((r) => r.fullName.length > 0)
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

  useEffect(() => {
    void getAdminLocations().then((r) => {
      if (r.status === 200 && Array.isArray(r.data)) {
        setLocations(r.data)
        setLocationId(r.data[0]?.id ?? '')
      }
    })
  }, [])

  const rows = useMemo(() => parse(text), [text])
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

        <label className="form-label">İşçilər — hər sətir bir nəfər</label>
        <textarea
          className="inp"
          rows={9}
          value={text}
          onChange={(e) => setText(e.target.value)}
          placeholder={'Əli Vəliyev, 0501234567\nAyşə Məmmədova, 0557654321, Mühasib\nRəşad Quliyev, 0701112233'}
          style={{ fontFamily: 'inherit', resize: 'vertical' }}
        />
        <div className="muted" style={{ fontSize: 12, marginTop: 6 }}>
          Format: <b>Ad Soyad, telefon, vəzifə</b> (vəzifə istəyə bağlı). Vergül və ya tab ilə ayırın.
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
              <div className="tbl-wrap">
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
              <div className="tbl-wrap">
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
              <div className="tbl-wrap">
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
