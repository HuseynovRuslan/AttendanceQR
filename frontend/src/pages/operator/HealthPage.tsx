import { useEffect, useState } from 'react'
import { getHealth, type HealthResponse, type HealthRow, type HealthStatus } from '../../api/admin'
import { fmtDate } from '../../lib/format'

const STATUS: Record<HealthStatus, { label: string; bg: string; fg: string }> = {
  healthy: { label: 'Sağlam', bg: 'var(--leaf-bg)', fg: 'var(--leaf-d)' },
  quiet: { label: 'Susqun', bg: 'rgba(245,158,11,0.15)', fg: '#b45309' },
  dormant: { label: 'Ölü', bg: 'rgba(200,60,40,0.12)', fg: 'var(--clay)' },
  new: { label: 'Yeni', bg: 'rgba(0,0,0,0.05)', fg: 'var(--c400)' },
}

function lastScanLabel(r: HealthRow): string {
  if (r.daysSinceLastScan == null || r.lastScanDate == null) return 'heç vaxt'
  if (r.daysSinceLastScan <= 0) return 'bu gün'
  if (r.daysSinceLastScan === 1) return 'dünən'
  return `${r.daysSinceLastScan} gün əvvəl`
}

function Stat({ label, value, tone }: { label: string; value: string; tone?: 'ok' | 'warn' }) {
  const color = tone === 'ok' ? 'var(--leaf-d)' : tone === 'warn' ? 'var(--clay)' : 'var(--c900)'
  return (
    <div className="card card-pad" style={{ flex: 1, minWidth: 130 }}>
      <div className="muted" style={{ fontSize: 12, marginBottom: 4 }}>{label}</div>
      <div style={{ fontSize: 22, fontWeight: 800, color, fontVariantNumeric: 'tabular-nums' }}>{value}</div>
    </div>
  )
}

export function HealthPage() {
  const [data, setData] = useState<HealthResponse | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    void (async () => {
      try {
        const res = await getHealth()
        if (res.status === 200 && res.data && 'rows' in res.data) setData(res.data)
        else if (res.status === 403) setError('İcazəniz yoxdur')
        else setError('Yüklənmədi')
      } catch {
        setError('Yüklənmədi')
      } finally {
        setLoading(false)
      }
    })()
  }, [])

  const s = data?.summary

  return (
    <div>
      {error && <div className="fb fb-err" style={{ marginBottom: 14 }}>{error}</div>}

      <div style={{ display: 'flex', gap: 12, marginBottom: 18, flexWrap: 'wrap' }}>
        <Stat label="Sağlam" value={String(s?.healthy ?? 0)} tone="ok" />
        <Stat label="Susqun" value={String(s?.quiet ?? 0)} />
        <Stat label="Ölü / Yeni" value={String(s?.dormant ?? 0)} tone={s && s.dormant > 0 ? 'warn' : undefined} />
        <Stat label="Aktiv istifadəçi" value={String(s?.totalActiveUsers ?? 0)} />
        <Stat label="Bu gün giriş" value={String(s?.checkInsToday ?? 0)} />
      </div>

      <div className="card">
        <table className="tbl">
          <thead>
            <tr>
              <th>Şirkət</th>
              <th className="num">İşçi</th>
              <th className="num">Filial</th>
              <th>Son skan</th>
              <th className="num">Bu gün</th>
              <th className="num">Bu ay</th>
              <th>Status</th>
            </tr>
          </thead>
          <tbody>
            {loading && <tr><td colSpan={7} className="muted" style={{ padding: 18 }}>Yüklənir…</td></tr>}
            {!loading && data && data.rows.length === 0 && (
              <tr><td colSpan={7} className="muted" style={{ padding: 18 }}>Aktiv şirkət yoxdur</td></tr>
            )}
            {!loading && data?.rows.map((r) => {
              const st = STATUS[r.status]
              return (
                <tr key={r.tenantId}>
                  <td>
                    <div style={{ fontWeight: 700 }}>{r.displayName}</div>
                    <div style={{ fontSize: 11, color: 'var(--c400)' }}>{r.plan ?? '— plansız'}</div>
                  </td>
                  <td className="num" style={{ fontVariantNumeric: 'tabular-nums' }}>
                    {r.employeeCount}
                    {r.maxEmployees != null && (
                      <span style={{ fontSize: 11, color: r.overEmployeeLimit ? 'var(--clay)' : 'var(--c400)' }}>
                        {' '}/{r.maxEmployees}
                      </span>
                    )}
                  </td>
                  <td className="num" style={{ fontVariantNumeric: 'tabular-nums' }}>
                    {r.locationCount}
                    {r.maxLocations != null && <span style={{ fontSize: 11, color: 'var(--c400)' }}> /{r.maxLocations}</span>}
                  </td>
                  <td style={{ fontSize: 13 }} title={r.lastScanDate ? fmtDate(r.lastScanDate) : undefined}>
                    {lastScanLabel(r)}
                  </td>
                  <td className="num" style={{ fontVariantNumeric: 'tabular-nums' }}>{r.checkInsToday}</td>
                  <td className="num" style={{ fontVariantNumeric: 'tabular-nums' }}>{r.checkInsThisMonth}</td>
                  <td>
                    <span className="tag" style={{ background: st.bg, color: st.fg }}>{st.label}</span>
                  </td>
                </tr>
              )
            })}
          </tbody>
        </table>
      </div>

      <div className="muted" style={{ fontSize: 12, marginTop: 10 }}>
        Status son skana görə: son 2 gündə → Sağlam, 3–14 gün → Susqun, daha çox / heç vaxt → Ölü/Yeni.
        Diqqət tələb edənlər yuxarıda.
      </div>
    </div>
  )
}
