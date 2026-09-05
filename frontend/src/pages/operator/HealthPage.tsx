import { useEffect, useMemo, useState } from 'react'
import { getHealth, type HealthResponse, type HealthRow, type HealthStatus } from '../../api/admin'
import { fmtDate } from '../../lib/format'
import { ConsoleToolbar, EmptyState, TableSkeleton, TenantAvatar, matches, useTableSort } from './console'

const STATUS: Record<HealthStatus, { label: string; bg: string; fg: string }> = {
  healthy: { label: 'Sağlam', bg: 'var(--leaf-bg)', fg: 'var(--leaf-d)' },
  // Both halves through tokens. The fg was #b45309 — a dark amber chosen for a white card, which on
  // the console's near-black ground is unreadable — and «Yeni» filled with a black wash that tints
  // to nothing there. Light and dark now each get a value that carries on their own surface.
  quiet: { label: 'Susqun', bg: 'var(--amber-bg)', fg: 'var(--amber-fg, #b45309)' },
  dormant: { label: 'Ölü', bg: 'var(--clay-bg)', fg: 'var(--clay)' },
  new: { label: 'Yeni', bg: 'var(--tag-neutral, rgba(0,0,0,0.05))', fg: 'var(--c400)' },
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
  const [query, setQuery] = useState('')
  const [only, setOnly] = useState('all')
  // Worst first by default: this screen exists to surface the companies that have gone quiet, and
  // alphabetical order buries them among the healthy ones.
  const { sort, Th, key: sortKey, dir: sortDir } = useTableSort<HealthRow>('daysSinceLastScan', 'desc')
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

  // Search and the status filter, applied once. The table is small, so this is a memo for tidiness
  // rather than for speed — it keeps the JSX below reading as markup.
  const shown = useMemo(() => {
    const rows = (data?.rows ?? []).filter((r) => only === 'all' || r.status === only)
    return sort(rows.filter((r) => matches(query, r.displayName, r.plan)))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [data, query, only, sortKey, sortDir])

  const FILTERS = [
    { key: 'all', label: 'Hamısı', count: data?.rows.length ?? 0 },
    { key: 'healthy', label: 'Sağlam', count: s?.healthy ?? 0 },
    { key: 'quiet', label: 'Susqun', count: s?.quiet ?? 0 },
    { key: 'dormant', label: 'Ölü / Yeni', count: s?.dormant ?? 0 },
  ]

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
        <ConsoleToolbar
          query={query}
          onQuery={setQuery}
          placeholder="Şirkət və ya plan üzrə axtar…"
          filters={FILTERS}
          activeFilter={only}
          onFilter={setOnly}
        />
        <table className="tbl">
          <thead>
            <tr>
              <Th field="displayName">Şirkət</Th>
              <Th field="employeeCount" className="num">İşçi</Th>
              <Th field="locationCount" className="num">Filial</Th>
              <Th field="daysSinceLastScan">Son skan</Th>
              <Th field="checkInsToday" className="num">Bu gün</Th>
              <Th field="checkInsThisMonth" className="num">Bu ay</Th>
              <th>Status</th>
            </tr>
          </thead>
          <tbody>
            {loading && <TableSkeleton rows={6} cols={7} />}
            {!loading && shown.length === 0 && (
              <tr><td colSpan={7} style={{ padding: 0 }}>
                <EmptyState
                  icon={data && data.rows.length > 0 ? '🔍' : '🏢'}
                  title={data && data.rows.length > 0 ? 'Bu şərtlərə uyğun şirkət yoxdur' : 'Hələ aktiv şirkət yoxdur'}
                  note={data && data.rows.length > 0
                    ? 'Axtarışı və ya statusu dəyişin.'
                    : 'Şirkət yaradılan kimi onun skan aktivliyi burada görünəcək.'}
                />
              </td></tr>
            )}
            {!loading && shown.map((r) => {
              const st = STATUS[r.status]
              return (
                <tr key={r.tenantId}>
                  <td>
                    <div className="con-named">
                      <TenantAvatar name={r.displayName} />
                      <div className="con-named-t">
                        <b>{r.displayName}</b>
                        <span>{r.plan ?? '— plansız'}</span>
                      </div>
                    </div>
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
