import { useEffect, useMemo, useState } from 'react'
import { getDashboard, getMyLocations, getToday, type DashboardReport, type DayAttendanceRow, type LocationDto } from '../../api/admin'
import { usePolling } from '../../lib/usePolling'
import { STATUS_MAP } from '../../components/StatusBadge'
import { ChartLegend, TrendChart, WeekdayBarChart } from '../../components/Charts'
import { IconX } from '../../components/icons'

function rateColor(rate: number): string {
  return rate >= 80 ? 'var(--leaf-d)' : rate >= 50 ? 'var(--amber)' : 'var(--clay)'
}

const todayIso = () => new Date().toISOString().slice(0, 10)
const daysAgoIso = (n: number) => new Date(Date.now() - n * 86_400_000).toISOString().slice(0, 10)

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

  // present = checked in AND OUT already ("Tamamlayıb" — finished their day). incomplete = checked in,
  // no check-out YET ("İşdə" — still at work; this page is always "today", so that's the only reading).
  // Both mean the employee showed up today — only their SEPARATE counts distinguish "still here" from
  // "already left", which used to be conflated as one confusing "Gəlib" bucket.
  const counts = { present: 0, absent: 0, incomplete: 0, dayOff: 0, onLeave: 0, permission: 0 }
  for (const r of rows) {
    if (r.status === 'OnTime' || r.status === 'Late') counts.present++
    else if (r.status === 'Absent') counts.absent++
    else if (r.status === 'DayOff') counts.dayOff++
    else if (r.status === 'OnLeave') counts.onLeave++
    else if (r.status === 'Permission') counts.permission++
    else counts.incomplete++
  }
  const total = rows.length
  // Employees not expected to attend today (day off, on leave, or excused) shouldn't drag the
  // rate down — excluding them from the denominator keeps a weekend/holiday/vacation-heavy day
  // from reading as a bad attendance day.
  const notExpected = counts.dayOff + counts.onLeave + counts.permission
  const expected = total - notExpected
  // "Attended today" = showed up at all, whether still here or already gone (present + incomplete).
  // This is the figure the daily attendance rate and the per-area breakdown are based on — NOT just
  // "present" (already-left) alone, which read as "0/55" all morning before anyone had checked out.
  const attended = counts.present + counts.incomplete
  const overallRate = expected ? Math.round((attended / expected) * 100) : 0

  const areaStats = useMemo(() => {
    const byArea = new Map<string, { name: string; total: number; attended: number; notExpected: number }>()
    for (const r of rows) {
      const entry = byArea.get(r.locationId) ?? { name: r.locationName, total: 0, attended: 0, notExpected: 0 }
      entry.total++
      // Attended = has a check-in today at all (OnTime/Late/Incomplete) — not Absent/excused.
      if (!['Absent', 'DayOff', 'OnLeave', 'Permission'].includes(r.status)) entry.attended++
      if (r.status === 'DayOff' || r.status === 'OnLeave' || r.status === 'Permission') entry.notExpected++
      byArea.set(r.locationId, entry)
    }
    return Array.from(byArea.values())
      .map((a) => {
        const areaExpected = a.total - a.notExpected
        return { ...a, expected: areaExpected, rate: areaExpected ? Math.round((a.attended / areaExpected) * 100) : 0 }
      })
      .sort((a, b) => a.name.localeCompare(b.name))
  }, [rows])

  // --- date-range dashboard (KPIs, trend, weekday pattern, top-5 late) ------

  const [locations, setLocations] = useState<LocationDto[]>([])
  const [dashFrom, setDashFrom] = useState(daysAgoIso(29))
  const [dashTo, setDashTo] = useState(todayIso())
  const [dashLocationId, setDashLocationId] = useState('')
  const [dashReport, setDashReport] = useState<DashboardReport | null>(null)
  const [dashLoading, setDashLoading] = useState(false)
  const [dashError, setDashError] = useState<string | null>(null)

  useEffect(() => {
    getMyLocations().then(({ status, data }) => {
      if (status === 200 && Array.isArray(data)) setLocations(data)
    })
  }, [])

  async function loadDashboard() {
    setDashError(null)
    setDashLoading(true)
    const { status, data } = await getDashboard(dashFrom, dashTo, dashLocationId || undefined)
    if (status === 200 && data && 'trend' in data) setDashReport(data)
    else if (status === 403) setDashError('İcazəniz yoxdur')
    else setDashError('Məlumat yüklənmədi')
    setDashLoading(false)
  }

  // eslint-disable-next-line react-hooks/exhaustive-deps
  useEffect(() => {
    void loadDashboard()
  }, [])

  return (
    <div>
      <div className="stat-grid">
        <div className="stat-card leaf">
          <div className="stat-lbl">Ümumi işçi</div>
          <div className="stat-val">{total}</div>
        </div>
        <div className="stat-card blue">
          <div className="stat-lbl">{STATUS_MAP.Incomplete.label}</div>
          <div className="stat-val">{counts.incomplete}</div>
          <div className="stat-sub">Hazırda işdədir, çıxışı yoxdur</div>
        </div>
        <div className="stat-card clay">
          <div className="stat-lbl">{STATUS_MAP.Absent.label}</div>
          <div className="stat-val">{counts.absent}</div>
          <div className="stat-sub">Bu gün heç giriş etməyib</div>
        </div>
        <div className="stat-card leaf">
          <div className="stat-lbl">{STATUS_MAP.OnTime.label}</div>
          <div className="stat-val">{counts.present}</div>
          <div className="stat-sub">Giriş və çıxış edib, günü bitirib</div>
        </div>
        <div className="stat-card purple">
          <div className="stat-lbl">{STATUS_MAP.DayOff.label}</div>
          <div className="stat-val">{counts.dayOff}</div>
        </div>
        <div className="stat-card purple">
          <div className="stat-lbl">{STATUS_MAP.OnLeave.label}</div>
          <div className="stat-val">{counts.onLeave}</div>
        </div>
        <div className="stat-card">
          <div className="stat-lbl">{STATUS_MAP.Permission.label}</div>
          <div className="stat-val">{counts.permission}</div>
        </div>
      </div>

      {error && (
        <div className="fb fb-err" style={{ marginBottom: 12 }}>
          <IconX />
          <span>{error}</span>
        </div>
      )}

      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(280px, 1fr))', gap: 16, marginBottom: 28 }}>
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
                <div style={{ fontSize: 12, color: 'var(--c400)' }}>{a.attended}/{a.expected} işçi işdədir</div>
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
          <p className="muted" style={{ fontSize: 12, marginTop: -8, marginBottom: 8 }}>
            Bu gün işə gələnlər (hazırda işdə olan + günü bitirən) gözlənilən işçilərə nisbətdə
          </p>
          <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', height: 120 }}>
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
                {attended} / {expected} işçi işdədir
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

      {/* --- date-range dashboard --- */}
      <div className="card card-pad" style={{ marginBottom: 16 }}>
        <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'flex-end', gap: 12 }}>
          <div style={{ minWidth: 180 }}>
            <label className="form-label">Ərazi</label>
            <select className="inp" value={dashLocationId} onChange={(e) => setDashLocationId(e.target.value)}>
              <option value="">Hamısı</option>
              {locations.map((l) => (
                <option key={l.id} value={l.id}>{l.name}</option>
              ))}
            </select>
          </div>
          <div>
            <label className="form-label">Başlanğıc</label>
            <input className="inp" type="date" value={dashFrom} onChange={(e) => setDashFrom(e.target.value)} />
          </div>
          <div>
            <label className="form-label">Son</label>
            <input className="inp" type="date" value={dashTo} onChange={(e) => setDashTo(e.target.value)} />
          </div>
          <button className="btn btn-primary" onClick={loadDashboard} disabled={dashLoading}>
            {dashLoading ? 'Yüklənir…' : 'Yüklə'}
          </button>
        </div>
      </div>

      {dashError && (
        <div className="fb fb-err" style={{ marginBottom: 12 }}>
          <IconX />
          <span>{dashError}</span>
        </div>
      )}

      {dashReport && (
        <>
          <div className="stat-grid">
            <div className="stat-card leaf">
              <div className="stat-lbl">Toplam girişlər</div>
              <div className="stat-val">{dashReport.totalCheckIns}</div>
            </div>
            <div className="stat-card blue">
              <div className="stat-lbl">Toplam çıxışlar</div>
              <div className="stat-val">{dashReport.totalCheckOuts}</div>
            </div>
            <div className="stat-card clay">
              <div className="stat-lbl">Qayıblar</div>
              <div className="stat-val">{dashReport.absentCount}</div>
            </div>
            <div className="stat-card blue">
              <div className="stat-lbl">Çıxışı unudulan günlər</div>
              <div className="stat-val">{dashReport.incompleteCount}</div>
              <div className="stat-sub">Gün bitib, çıxış qeydə alınmayıb</div>
            </div>
            <div className="stat-card purple">
              <div className="stat-lbl">İstirahət</div>
              <div className="stat-val">{dashReport.dayOffCount}</div>
            </div>
            <div className="stat-card purple">
              <div className="stat-lbl">Məzuniyyət</div>
              <div className="stat-val">{dashReport.leaveCount}</div>
            </div>
            <div className="stat-card">
              <div className="stat-lbl">İcazə</div>
              <div className="stat-val">{dashReport.permissionCount}</div>
            </div>
            <div className="stat-card leaf">
              <div className="stat-lbl">İşlənən saat</div>
              <div className="stat-val">{dashReport.totalWorkedHours}</div>
            </div>
            <div className="stat-card amber">
              <div className="stat-lbl">Overtime saat</div>
              <div className="stat-val">{dashReport.overtimeHours}</div>
            </div>
            <div className="stat-card clay">
              <div className="stat-lbl">Koordinat xarici</div>
              <div className="stat-val">{dashReport.outsideRadiusCount}</div>
            </div>
            <div className="stat-card leaf">
              <div className="stat-lbl">Aktiv cihazlar</div>
              <div className="stat-val">{dashReport.activeDeviceCount}</div>
            </div>
          </div>

          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(320px, 1fr))', gap: 16, marginTop: 4, marginBottom: 16 }}>
            <div className="card card-pad">
              <div className="card-title">Giriş / Çıxış trendi</div>
              <ChartLegend />
              <TrendChart points={dashReport.trend} />
            </div>
            <div className="card card-pad">
              <div className="card-title">Həftənin günləri</div>
              <ChartLegend />
              <WeekdayBarChart points={dashReport.weekdayBreakdown} />
            </div>
          </div>

          {/* "Ən çox gecikənlər" removed with the rest of lateness: ranking employees against a shift
              none of them actually works to is worse than showing nothing. */}
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(320px, 1fr))', gap: 16 }}>
            <div className="card card-pad">
              <div className="card-title">Ümumi baxış</div>
              <div style={{ display: 'grid', gridTemplateColumns: '1fr auto', rowGap: 10, fontSize: 13 }}>
                <span className="muted">Seçilmiş tarix aralığı</span>
                <span className="mono" style={{ textAlign: 'right' }}>{dashReport.from} – {dashReport.to}</span>
                <span className="muted">Giriş/Çıxış nisbəti</span>
                <span className="mono" style={{ textAlign: 'right' }}>{dashReport.checkInOutRatio}%</span>
                <span className="muted">Koordinat xarici faizi</span>
                <span className="mono" style={{ textAlign: 'right' }}>{dashReport.outsideRadiusRate}%</span>
                <span className="muted">
                  Gündəlik orta giriş+çıxış sayı
                  <br />
                  <span style={{ fontSize: 11, color: 'var(--c400)' }}>
                    (seçilmiş dövrdə gündə orta hesabla neçə skan qeydə alınıb)
                  </span>
                </span>
                <span className="mono" style={{ textAlign: 'right' }}>{dashReport.avgDailyOperations}</span>
              </div>
            </div>
          </div>
        </>
      )}
    </div>
  )
}
