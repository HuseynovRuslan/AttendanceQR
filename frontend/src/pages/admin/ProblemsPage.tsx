import { useEffect, useState } from 'react'
import { getProblems, type ProblemsReport } from '../../api/admin'
import { IconX } from '../../components/icons'

/** Reason → human label + colour. `blocking` = the employee genuinely could NOT check in/out. */
const REASON: Record<string, { label: string; cls: string; blocking?: boolean }> = {
  DeviceMismatch: { label: 'Cihaz uyğun deyil', cls: 'bg-red-100 text-red-700', blocking: true },
  NoDeviceBound: { label: 'Cihaz bağlanmayıb', cls: 'bg-red-100 text-red-700', blocking: true },
  EmployeeNotFoundOrInactive: { label: 'Hesab aktiv deyil', cls: 'bg-red-100 text-red-700', blocking: true },
  LocationNotFound: { label: 'Lokasiya tapılmadı', cls: 'bg-red-100 text-red-700', blocking: true },
  LocationInactive: { label: 'Lokasiya deaktiv', cls: 'bg-red-100 text-red-700', blocking: true },
  OutsideRadius: { label: 'İş yerindən kənarda', cls: 'bg-amber-100 text-amber-700', blocking: true },
  TokenExpired: { label: 'QR köhnəlib', cls: 'bg-amber-100 text-amber-700' },
  TokenReused: { label: 'QR təkrar işlədilib', cls: 'bg-amber-100 text-amber-700' },
  TokenMalformed: { label: 'Yanlış QR', cls: 'bg-amber-100 text-amber-700' },
  SignatureInvalid: { label: 'Yanlış QR (imza)', cls: 'bg-amber-100 text-amber-700' },
  TooSoonToCheckOut: { label: 'Çox tez çıxış cəhdi', cls: 'bg-blue-100 text-blue-700' },
  // Reported by the phone itself — the scan never reached the server.
  GpsPermissionDenied: { label: 'Məkan icazəsi verilməyib', cls: 'bg-red-100 text-red-700', blocking: true },
  GpsUnavailable: { label: 'Telefonda məkan bağlıdır', cls: 'bg-red-100 text-red-700', blocking: true },
  GpsTimeout: { label: 'GPS siqnal tapmadı', cls: 'bg-amber-100 text-amber-700', blocking: true },
  GpsUnsupported: { label: 'Brauzer məkanı dəstəkləmir', cls: 'bg-red-100 text-red-700', blocking: true },
  // Measured, warned about, but never blocked — so it isn't a "could not scan" reason.
  GpsInaccurate: { label: 'GPS dəqiq deyil', cls: 'bg-amber-100 text-amber-700' },
  AlreadyCompleted: { label: 'Gün artıq tamamlanıb', cls: 'bg-slate-100 text-slate-600' },
  DuplicateCheckIn: { label: 'Təkrar giriş', cls: 'bg-slate-100 text-slate-600' },
}

const ACTION_AZ: Record<string, string> = {
  CheckIn: 'Giriş',
  CheckOut: 'Çıxış',
  Device: 'Telefonda',
}

function meta(reason: string) {
  return REASON[reason] ?? { label: reason, cls: 'bg-slate-100 text-slate-600' }
}

function todayLocal(): string {
  const d = new Date()
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
}

function fmtTime(iso: string): string {
  return new Date(iso).toLocaleTimeString('az-AZ', { hour: '2-digit', minute: '2-digit' })
}

export function ProblemsPage() {
  const [date, setDate] = useState(todayLocal())
  const [report, setReport] = useState<ProblemsReport | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    void load(date)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [date])

  async function load(d: string) {
    setLoading(true)
    setError(null)
    const { status, data } = await getProblems(d)
    setLoading(false)
    if (status === 200 && data && 'rows' in data) {
      setReport(data)
    } else if (status === 403) {
      setError('İcazəniz yoxdur')
    } else {
      setError('Məlumat yüklənmədi')
    }
  }

  // Employees who genuinely could not scan — the actionable part.
  const blocked = new Map<string, { count: number; reasons: Set<string> }>()
  for (const r of report?.rows ?? []) {
    if (!meta(r.reason).blocking) continue
    const cur = blocked.get(r.employeeName) ?? { count: 0, reasons: new Set<string>() }
    cur.count++
    cur.reasons.add(r.reason)
    blocked.set(r.employeeName, cur)
  }

  return (
    <div>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-end', gap: 12, marginBottom: 14, flexWrap: 'wrap' }}>
        <div>
          <h1 style={{ fontSize: 18, fontWeight: 800, color: 'var(--c900)' }}>Problemlər</h1>
          <div className="muted" style={{ fontSize: 13 }}>Rədd edilmiş skanlar — kim skan edə bilmədi və niyə.</div>
        </div>
        <div>
          <label className="form-label">Tarix</label>
          <input className="inp" type="date" value={date} max={todayLocal()} onChange={(e) => setDate(e.target.value)} />
        </div>
      </div>

      {error && (
        <div className="fb fb-err" style={{ marginBottom: 12 }}>
          <IconX />
          <span>{error}</span>
        </div>
      )}

      <div className="stat-grid" style={{ marginBottom: 14 }}>
        <div className="stat-card clay">
          <div className="stat-lbl">Problemli skan</div>
          <div className="stat-val">{report?.rejectedCount ?? '—'}</div>
        </div>
        <div className="stat-card leaf">
          <div className="stat-lbl">Uğurlu skan</div>
          <div className="stat-val">{report?.successCount ?? '—'}</div>
        </div>
      </div>

      {blocked.size > 0 && (
        <div className="card card-pad" style={{ marginBottom: 14, borderColor: '#fecaca', background: '#fef2f2' }}>
          <div style={{ fontWeight: 700, color: '#991b1b', marginBottom: 8 }}>
            ⚠️ Bu işçilər skan edə bilmədi ({blocked.size})
          </div>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
            {Array.from(blocked, ([name, info]) => (
              <div key={name} style={{ fontSize: 13, color: 'var(--c700)' }}>
                <b>{name}</b> — {info.count} cəhd ·{' '}
                {Array.from(info.reasons).map((r) => meta(r).label).join(', ')}
              </div>
            ))}
          </div>
        </div>
      )}

      {report && report.summary.length > 0 && (
        <div className="chip-row">
          {report.summary.map((s) => (
            <span key={s.reason} className={`inline-flex items-center gap-1 rounded-full px-3 py-1 text-xs font-semibold ${meta(s.reason).cls}`}>
              {meta(s.reason).label} · {s.count}
            </span>
          ))}
        </div>
      )}

      <div className="tbl-wrap">
        <table>
          <thead>
            <tr>
              <th>Vaxt</th>
              <th>İşçi</th>
              <th>Əməliyyat</th>
              <th>Səbəb</th>
            </tr>
          </thead>
          <tbody>
            {(report?.rows ?? []).map((r, i) => (
              <tr key={`${r.atUtc}-${i}`}>
                <td className="mono">{fmtTime(r.atUtc)}</td>
                <td style={{ fontWeight: 700, color: 'var(--c900)' }}>{r.employeeName}</td>
                <td>{ACTION_AZ[r.action] ?? r.action}</td>
                <td>
                  <span className={`inline-flex items-center rounded-full px-2 py-0.5 text-xs font-semibold ${meta(r.reason).cls}`}>
                    {meta(r.reason).label}
                  </span>
                  {r.detail && (
                    <span className="muted" style={{ marginLeft: 6, fontSize: 12 }}>
                      {r.reason === 'GpsInaccurate' ? `±${r.detail} m` : r.detail}
                    </span>
                  )}
                </td>
              </tr>
            ))}
            {!loading && report && report.rows.length === 0 && !error && (
              <tr>
                <td colSpan={4} className="muted" style={{ textAlign: 'center', padding: 28 }}>
                  Bu gün heç bir problem olmayıb 🎉
                </td>
              </tr>
            )}
            {loading && (
              <tr>
                <td colSpan={4} className="muted" style={{ textAlign: 'center', padding: 28 }}>
                  Yüklənir…
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  )
}
