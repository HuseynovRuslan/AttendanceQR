import { useMemo, useState } from 'react'
import { getToday, type DayAttendanceRow } from '../../api/admin'
import { usePolling } from '../../lib/usePolling'
import { STATUS_MAP } from '../../components/StatusBadge'
import { IconX } from '../../components/icons'

function rateColor(rate: number): string {
  return rate >= 80 ? 'var(--leaf-d)' : rate >= 50 ? 'var(--amber)' : 'var(--clay)'
}

export function DashboardPage() {
  const [rows, setRows] = useState<DayAttendanceRow[]>([])
  const [error, setError] = useState<string | null>(null)

  usePolling(async () => {
    const { status, data } = await getToday()
    if (status === 200 && Array.isArray(data)) {
      setRows(data)
      setError(null)
    } else if (status === 403) {
      setError('İcazəniz yoxdur')
    } else {
      setError('Məlumat yüklənmədi')
    }
  }, 30_000)

  const counts = { present: 0, late: 0, absent: 0, incomplete: 0, dayOff: 0 }
  for (const r of rows) {
    if (r.status === 'OnTime') counts.present++
    else if (r.status === 'Late') counts.late++
    else if (r.status === 'Absent') counts.absent++
    else if (r.status === 'DayOff') counts.dayOff++
    else counts.incomplete++
  }
  const total = rows.length
  // Employees on a day off aren't expected to attend — excluding them from the rate's denominator
  // keeps a weekend/holiday from reading as a bad attendance day.
  const expected = total - counts.dayOff
  const overallRate = expected ? Math.round(((counts.present + counts.late) / expected) * 100) : 0

  const areaStats = useMemo(() => {
    const byArea = new Map<string, { name: string; total: number; present: number; dayOff: number }>()
    for (const r of rows) {
      const entry = byArea.get(r.locationId) ?? { name: r.locationName, total: 0, present: 0, dayOff: 0 }
      entry.total++
      if (r.status === 'OnTime' || r.status === 'Late') entry.present++
      else if (r.status === 'DayOff') entry.dayOff++
      byArea.set(r.locationId, entry)
    }
    return Array.from(byArea.values())
      .map((a) => {
        const areaExpected = a.total - a.dayOff
        return { ...a, expected: areaExpected, rate: areaExpected ? Math.round((a.present / areaExpected) * 100) : 0 }
      })
      .sort((a, b) => a.name.localeCompare(b.name))
  }, [rows])

  return (
    <div>
      <div className="stat-grid">
        <div className="stat-card leaf">
          <div className="stat-lbl">Ümumi işçi</div>
          <div className="stat-val">{total}</div>
        </div>
        <div className="stat-card leaf">
          <div className="stat-lbl">{STATUS_MAP.OnTime.label}</div>
          <div className="stat-val">{counts.present}</div>
        </div>
        <div className="stat-card amber">
          <div className="stat-lbl">{STATUS_MAP.Late.label}</div>
          <div className="stat-val">{counts.late}</div>
        </div>
        <div className="stat-card clay">
          <div className="stat-lbl">{STATUS_MAP.Absent.label}</div>
          <div className="stat-val">{counts.absent}</div>
        </div>
        <div className="stat-card blue">
          <div className="stat-lbl">{STATUS_MAP.Incomplete.label}</div>
          <div className="stat-val">{counts.incomplete}</div>
        </div>
        <div className="stat-card purple">
          <div className="stat-lbl">{STATUS_MAP.DayOff.label}</div>
          <div className="stat-val">{counts.dayOff}</div>
        </div>
      </div>

      {error && (
        <div className="fb fb-err" style={{ marginBottom: 12 }}>
          <IconX />
          <span>{error}</span>
        </div>
      )}

      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(280px, 1fr))', gap: 16 }}>
        <div className="card card-pad">
          <div className="card-title">Ərazilər üzrə bugün</div>
          {areaStats.length === 0 && <p className="muted" style={{ fontSize: 13 }}>Məlumat yoxdur</p>}
          {areaStats.map((a) => (
            <div
              key={a.name}
              style={{
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'space-between',
                padding: '10px 0',
                borderBottom: '1px solid var(--c50)',
              }}
            >
              <div>
                <div style={{ fontWeight: 700, fontSize: 13 }}>{a.name}</div>
                <div style={{ fontSize: 12, color: 'var(--c400)' }}>{a.present}/{a.expected} işçi</div>
              </div>
              <div
                style={{
                  fontFamily: "'Sora',sans-serif",
                  fontWeight: 800,
                  fontSize: 18,
                  color: rateColor(a.rate),
                }}
              >
                {a.rate}%
              </div>
            </div>
          ))}
        </div>

        <div className="card card-pad">
          <div className="card-title">Günlük davamiyyət faizi</div>
          <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', height: 140 }}>
            <div style={{ textAlign: 'center' }}>
              <div
                style={{
                  fontFamily: "'Sora',sans-serif",
                  fontWeight: 800,
                  fontSize: 52,
                  color: rateColor(overallRate),
                }}
              >
                {overallRate}%
              </div>
              <div style={{ fontSize: 13, color: 'var(--c400)', marginTop: 4 }}>
                {counts.present + counts.late} / {expected} işçi
              </div>
            </div>
          </div>
          <div style={{ height: 8, background: 'var(--c100)', borderRadius: 999, overflow: 'hidden', marginTop: 8 }}>
            <div
              style={{
                height: '100%',
                background: rateColor(overallRate),
                borderRadius: 999,
                width: `${overallRate}%`,
                transition: 'width .6s',
              }}
            />
          </div>
        </div>
      </div>
    </div>
  )
}
