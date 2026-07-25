import { useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import {
  getAdminLocations, getDashboard, getPendingDeviceChanges, getProblems, getToday,
  type AdminLocation, type DashboardReport, type DayAttendanceRow,
} from '../../api/admin'
import { getOpenRecords } from '../../api/attendance'
import { usePolling } from '../../lib/usePolling'
import { DashboardMap, type DashSite, type DashPerson } from './DashboardMap'
import { DateRangePicker } from '../../components/DateRangePicker'
import { EmployeeLink } from '../../components/EmployeeLink'
import { IconX } from '../../components/icons'
import { fmtTime } from '../../lib/format'

/**
 * The admin's live "today" board, dressed as the group panel a director reacted to — but in white,
 * Apple-plain rather than the dark glass, because this lives inside the admin app next to white cards.
 *
 * It is one company, so the group board's per-company cards give way to what actually matters here:
 * a big live "on duty right now" number, the day's buckets as tappable pills, the company's sites on
 * a map sized by who is standing at each, a live activity feed, and a selectable-period attendance
 * sparkline. The old trend/weekday charts and the period-ratio card were removed on purpose — they
 * restated the same numbers a third time and nobody read them.
 */

type Bucket = 'in' | 'done' | 'absent' | 'total'

/** Which today-row belongs to which tappable bucket. Mirrors the counts below exactly, so a pill and
 *  the list it opens can never disagree. */
function inBucket(status: string, bucket: Bucket): boolean {
  if (bucket === 'total') return true
  if (bucket === 'done') return status === 'OnTime' || status === 'Late'
  if (bucket === 'absent') return status === 'Absent'
  // "in" = still at work (checked in, not out) — Incomplete, plus the not-yet-due Pending, which is
  // "expected today" more than any other bucket.
  return status === 'Incomplete' || status === 'Pending'
}

const BUCKET_LABEL: Record<Bucket, string> = { in: 'İşdə', done: 'Tamamlayıb', absent: 'Qayıb', total: 'Ümumi işçi' }

/** Eases a number from its previous value to the target (easeOutCubic); counts up from 0 on mount,
 *  ticks smoothly on each poll, and jumps straight to the value under reduced-motion. */
function useCountUp(target: number, duration = 750): number {
  const [val, setVal] = useState(target)
  const fromRef = useRef(0)
  useEffect(() => {
    if (typeof window !== 'undefined' && window.matchMedia?.('(prefers-reduced-motion: reduce)').matches) {
      fromRef.current = target
      setVal(target)
      return
    }
    const from = fromRef.current
    const start = performance.now()
    let raf = 0
    const tick = (now: number) => {
      const p = Math.min(1, (now - start) / duration)
      const eased = 1 - Math.pow(1 - p, 3)
      setVal(from + (target - from) * eased)
      if (p < 1) raf = requestAnimationFrame(tick)
      else fromRef.current = target
    }
    raf = requestAnimationFrame(tick)
    return () => cancelAnimationFrame(raf)
  }, [target, duration])
  return val
}

const isoDay = (d: Date) => {
  const p = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`
}
const todayIso = () => isoDay(new Date())
const daysAgoIso = (n: number) => isoDay(new Date(Date.now() - n * 86_400_000))

/** A minimal area sparkline over the daily attendance figures — the group board's trend, distilled
 *  to one white card. No axes or grid; the shape is the whole point. */
function Sparkline({ points }: { points: number[] }) {
  if (points.length < 2) return <div className="lux-spark-empty">Kifayət qədər məlumat yoxdur</div>
  const w = 600, h = 90, pad = 6
  const max = Math.max(1, ...points)
  const step = (w - pad * 2) / (points.length - 1)
  const x = (i: number) => pad + i * step
  const y = (v: number) => h - pad - (v / max) * (h - pad * 2)
  const line = points.map((v, i) => `${i === 0 ? 'M' : 'L'}${x(i).toFixed(1)},${y(v).toFixed(1)}`).join(' ')
  const area = `${line} L${x(points.length - 1).toFixed(1)},${h} L${x(0).toFixed(1)},${h} Z`
  return (
    <svg className="lux-spark" viewBox={`0 0 ${w} ${h}`} preserveAspectRatio="none" aria-hidden="true">
      <defs>
        <linearGradient id="luxfill" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stopColor="var(--leaf)" stopOpacity="0.28" />
          <stop offset="100%" stopColor="var(--leaf)" stopOpacity="0" />
        </linearGradient>
      </defs>
      <path d={area} fill="url(#luxfill)" />
      <path d={line} fill="none" stroke="var(--leaf-d)" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round" />
      <circle cx={x(points.length - 1)} cy={y(points[points.length - 1])} r="3.5" fill="var(--leaf-d)" />
    </svg>
  )
}

export function DashboardPage() {
  const navigate = useNavigate()
  const [rows, setRows] = useState<DayAttendanceRow[]>([])
  const [error, setError] = useState<string | null>(null)
  const [openBucket, setOpenBucket] = useState<Bucket | null>(null)
  const [actions, setActions] = useState({ open: 0, devices: 0, problems: 0 })
  const [locations, setLocations] = useState<AdminLocation[]>([])
  const [now, setNow] = useState(() => new Date())

  useEffect(() => {
    getAdminLocations().then(({ status, data }) => {
      if (status === 200 && Array.isArray(data)) setLocations(data)
    })
  }, [])

  // Live clock, like the group board — the seconds ticking is half of what makes it read as live.
  useEffect(() => {
    const t = setInterval(() => setNow(new Date()), 1000)
    return () => clearInterval(t)
  }, [])

  usePolling(async () => {
    const [today, open, devices, problems] = await Promise.all([
      getToday(),
      getOpenRecords(),
      getPendingDeviceChanges(),
      getProblems(todayIso(), todayIso()),
    ])
    if (today.status === 200 && Array.isArray(today.data)) {
      setRows(today.data)
      setError(null)
    } else if (today.status === 403) setError('İcazəniz yoxdur')
    else setError('Məlumat yüklənmədi')
    setActions({
      open: Array.isArray(open.data) ? open.data.length : 0,
      devices: Array.isArray(devices.data) ? devices.data.length : 0,
      problems: problems.data && 'rejectedCount' in problems.data ? problems.data.rejectedCount : 0,
    })
  }, 30_000)

  const activity = useMemo(() => {
    const ev: { id: string; name: string; loc: string; type: 'in' | 'out'; at: string }[] = []
    for (const r of rows) {
      if (r.checkInAtUtc) ev.push({ id: r.employeeId + 'i', name: r.employeeName, loc: r.locationName, type: 'in', at: r.checkInAtUtc })
      if (r.checkOutAtUtc) ev.push({ id: r.employeeId + 'o', name: r.employeeName, loc: r.locationName, type: 'out', at: r.checkOutAtUtc })
    }
    return ev.sort((a, b) => (a.at < b.at ? 1 : -1)).slice(0, 14)
  }, [rows])

  const counts = { present: 0, absent: 0, incomplete: 0, pending: 0, dayOff: 0, onLeave: 0, permission: 0 }
  for (const r of rows) {
    if (r.status === 'OnTime' || r.status === 'Late') counts.present++
    else if (r.status === 'Absent') counts.absent++
    else if (r.status === 'Pending') counts.pending++
    else if (r.status === 'DayOff') counts.dayOff++
    else if (r.status === 'OnLeave') counts.onLeave++
    else if (r.status === 'Permission') counts.permission++
    else counts.incomplete++
  }
  const total = rows.length
  const notExpected = counts.dayOff + counts.onLeave + counts.permission
  const expected = total - notExpected
  const attended = counts.present + counts.incomplete
  const overallRate = expected ? Math.round((attended / expected) * 100) : 0

  // On duty RIGHT NOW = checked in and not yet out. The hero number.
  const onDutyNow = counts.incomplete
  const cOnDuty = Math.round(useCountUp(onDutyNow))
  const cDone = Math.round(useCountUp(counts.present))
  const cAbsent = Math.round(useCountUp(counts.absent))
  const cTotal = Math.round(useCountUp(total))
  const cRate = Math.round(useCountUp(overallRate))

  // Sites for the map: each location joined with how many of its people are on duty now.
  const sites = useMemo<DashSite[]>(() => {
    return locations
      .filter((l) => l.isActive && Number.isFinite(l.latitude) && Number.isFinite(l.longitude))
      .map((l) => {
        const mine = rows.filter((r) => r.locationId === l.id)
        return {
          id: l.id,
          name: l.name,
          lat: l.latitude,
          lng: l.longitude,
          radiusMeters: l.radiusMeters,
          onDuty: mine.filter((r) => r.status === 'Incomplete').length,
          staff: mine.length,
        }
      })
  }, [locations, rows])

  // Individual people, plotted where they actually scanned in (green = on duty, blue = left). Only
  // those whose check-in position is known; the rest fall back to the site marker inside the map.
  const people = useMemo<DashPerson[]>(() => {
    return rows
      .filter((r) => r.checkInLatitude != null && r.checkInLongitude != null)
      .map((r) => ({
        id: r.employeeId,
        name: r.employeeName,
        lat: r.checkInLatitude as number,
        lng: r.checkInLongitude as number,
        onDuty: r.status === 'Incomplete',
        siteName: r.locationName,
      }))
  }, [rows])

  // --- period sparkline -------------------------------------------------------
  // Two presets people actually ask for — 14 gün / 1 ay — plus "Əl ilə" for any other window.
  const [mode, setMode] = useState<'14' | 'month' | 'manual'>('14')
  const [manual, setManual] = useState<{ from: string; to: string }>({ from: daysAgoIso(13), to: todayIso() })
  const [report, setReport] = useState<DashboardReport | null>(null)

  const range = mode === '14'
    ? { from: daysAgoIso(13), to: todayIso() }
    : mode === 'month'
      ? { from: daysAgoIso(29), to: todayIso() }
      : manual

  useEffect(() => {
    void getDashboard(range.from, range.to).then(({ status, data }) => {
      if (status === 200 && data && 'trend' in data) setReport(data)
    })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [mode, manual.from, manual.to])

  const spark = useMemo(() => (report?.trend ?? []).map((t) => t.checkIns), [report])
  // Direction of travel: the second half of the window against the first. A rising second half is ▲.
  const delta = useMemo(() => {
    if (spark.length < 4) return 0
    const mid = Math.floor(spark.length / 2)
    const a = spark.slice(0, mid).reduce((s, v) => s + v, 0)
    const b = spark.slice(mid).reduce((s, v) => s + v, 0)
    if (a === 0) return b > 0 ? 100 : 0
    return Math.round(((b - a) / a) * 100)
  }, [spark])

  const bucketRows = openBucket ? rows.filter((r) => inBucket(r.status, openBucket)).sort((a, b) => a.employeeName.localeCompare(b.employeeName)) : []

  const clock = now.toLocaleTimeString('az-AZ', { hour: '2-digit', minute: '2-digit', second: '2-digit' })

  return (
    <div className="lux">
      {/* Header — title, live pill, clock. */}
      <header className="lux-head lux-rise lux-d1">
        <div>
          <div className="lux-title">İdarəetmə paneli</div>
          <div className="lux-sub">Bu gün · canlı davamiyyət</div>
        </div>
        <div className="lux-head-r">
          <span className="lux-live"><i />CANLI</span>
          <span className="lux-clock">{clock}</span>
        </div>
      </header>

      {error && <div className="fb fb-err" style={{ marginBottom: 14 }}><IconX /><span>{error}</span></div>}

      {/* Hero — the big "on duty now" number + a compact stat strip. */}
      <section className="lux-hero lux-rise lux-d2">
        <div className="lux-hero-main">
          <div className="lux-hero-label">İndi iş başında</div>
          <div className="lux-hero-num">{cOnDuty}<span className="lux-hero-unit">nəfər</span></div>
          <div className="lux-hero-note">{cTotal} işçidən {cOnDuty}-i hazırda işdədir · gün ərzində {cRate}% iştirak</div>
        </div>
        <div className="lux-hero-ring" aria-hidden="true">
          <svg viewBox="0 0 120 120">
            <circle cx="60" cy="60" r="52" fill="none" stroke="var(--c100)" strokeWidth="10" />
            <circle
              cx="60" cy="60" r="52" fill="none" stroke="var(--leaf)" strokeWidth="10" strokeLinecap="round"
              strokeDasharray={2 * Math.PI * 52}
              strokeDashoffset={2 * Math.PI * 52 * (1 - cRate / 100)}
              transform="rotate(-90 60 60)"
              style={{ transition: 'stroke-dashoffset .5s ease' }}
            />
          </svg>
          <div className="lux-hero-ring-c">{cRate}%</div>
        </div>
      </section>

      {/* Buckets as tappable pills. Order per request: Ümumi işçi first (leftmost), Tamamlayıb last
          (rightmost); the day's live states sit between. Leave/permission/rest appear only when > 0. */}
      <section className="lux-pills lux-rise lux-d3">
        <Pill tone="slate" n={cTotal} label="Ümumi işçi" active={openBucket === 'total'} onClick={() => setOpenBucket(openBucket === 'total' ? null : 'total')} />
        <Pill tone="blue" n={cOnDuty} label="İşdə" active={openBucket === 'in'} onClick={() => setOpenBucket(openBucket === 'in' ? null : 'in')} />
        <Pill tone="clay" n={cAbsent} label="Qayıb" active={openBucket === 'absent'} onClick={() => setOpenBucket(openBucket === 'absent' ? null : 'absent')} />
        {counts.pending > 0 && <Pill tone="slate" n={counts.pending} label="Gözlənilir" />}
        {counts.onLeave > 0 && <Pill tone="purple" n={counts.onLeave} label="Məzuniyyət" />}
        {counts.permission > 0 && <Pill tone="purple" n={counts.permission} label="İcazə" />}
        {counts.dayOff > 0 && <Pill tone="purple" n={counts.dayOff} label="İstirahət" />}
        <Pill tone="leaf" n={cDone} label="Tamamlayıb" active={openBucket === 'done'} onClick={() => setOpenBucket(openBucket === 'done' ? null : 'done')} />
      </section>

      {/* Tapping a pill opens the matching employee list right here. */}
      {openBucket && (
        <div className="card lux-list lux-rise">
          <div className="lux-list-head">
            <b>{BUCKET_LABEL[openBucket]}</b>
            <span className="muted">{bucketRows.length} nəfər</span>
            <button className="btn btn-sm" onClick={() => setOpenBucket(null)} aria-label="Bağla"><IconX /></button>
          </div>
          {bucketRows.length === 0 ? (
            <div className="muted" style={{ padding: '10px 16px' }}>Siyahı boşdur.</div>
          ) : (
            <div className="lux-list-body">
              {bucketRows.map((r) => (
                <div className="lux-list-row" key={r.employeeId}>
                  <span className="lux-list-nm"><EmployeeLink id={r.employeeId} name={r.employeeName} /></span>
                  <span className="muted lux-list-loc">{r.locationName}</span>
                  <span className="mono lux-list-t">
                    {r.checkInAtUtc ? fmtTime(r.checkInAtUtc) : '—'}
                    {r.checkOutAtUtc ? ` – ${fmtTime(r.checkOutAtUtc)}` : ''}
                  </span>
                </div>
              ))}
            </div>
          )}
        </div>
      )}

      {/* Map + live feed, side by side. */}
      <section className="lux-grid lux-rise lux-d4">
        <div className="card lux-panel">
          <div className="lux-panel-h">
            <span>Ərazilər</span>
            <span className="muted">nöqtənin böyüklüyü — hazırda işdə olanların sayı</span>
          </div>
          {sites.length > 0 || people.length > 0 ? (
            <DashboardMap sites={sites} people={people} />
          ) : (
            <div className="muted" style={{ padding: 24 }}>Koordinatı olan aktiv ərazi yoxdur.</div>
          )}
        </div>

        <div className="card lux-panel">
          <div className="lux-panel-h"><span>Son fəaliyyət</span></div>
          {activity.length === 0 ? (
            <div className="muted" style={{ padding: '10px 16px' }}>Bu gün hələ hərəkət yoxdur</div>
          ) : (
            <div className="lux-feed">
              {activity.map((e, i) => (
                <div key={e.id} className={`lux-feed-row${i === 0 ? ' is-new' : ''}`}>
                  <span className="lux-feed-dot" style={{ background: e.type === 'in' ? 'var(--leaf)' : 'var(--blue)' }} />
                  <span className="lux-feed-nm">{e.name}</span>
                  <span className="muted lux-feed-a">{e.type === 'in' ? 'giriş' : 'çıxış'} · {e.loc}</span>
                  <span className="mono lux-feed-t">{fmtTime(e.at)}</span>
                </div>
              ))}
            </div>
          )}
        </div>
      </section>

      {/* Attendance sparkline with a selectable window. */}
      <section className="card lux-panel lux-rise lux-d5" style={{ marginBottom: 16 }}>
        <div className="lux-panel-h">
          <span>Davamiyyət tendensiyası</span>
          <div className="lux-spanpick">
            <button className={`lux-chip${mode === '14' ? ' active' : ''}`} onClick={() => setMode('14')}>14 gün</button>
            <button className={`lux-chip${mode === 'month' ? ' active' : ''}`} onClick={() => setMode('month')}>1 ay</button>
            <button className={`lux-chip${mode === 'manual' ? ' active' : ''}`} onClick={() => setMode('manual')}>Əl ilə</button>
          </div>
        </div>
        {mode === 'manual' && (
          <div style={{ padding: '0 18px 6px' }}>
            <DateRangePicker
              from={manual.from}
              to={manual.to}
              onApply={(f, t) => setManual({ from: f, to: t })}
            />
          </div>
        )}
        <div className="lux-spark-row">
          <div className="lux-spark-delta">
            <span className={delta > 0 ? 'up' : delta < 0 ? 'down' : 'flat'}>
              {delta > 0 ? '▲' : delta < 0 ? '▼' : '■'} {Math.abs(delta)}%
            </span>
            <span className="muted">{mode === 'month' ? 'son ay' : mode === '14' ? 'son 14 gün' : 'seçilmiş aralıq'}</span>
          </div>
          <Sparkline points={spark} />
        </div>
      </section>

      {/* Action center — the things that need a person. */}
      <section className="card lux-panel lux-rise lux-d5">
        <div className="lux-panel-h"><span>Diqqət mərkəzi</span></div>
        <div className="lux-actions">
          {[
            // Goes to the today board rather than opening the pill list at the very top of the page —
            // pressing something at the bottom and having the screen jump upward reads as a glitch.
            { color: 'var(--clay)', n: counts.absent, label: 'Bu gün gəlməyib', onClick: () => navigate('/admin/today') },
            { color: 'var(--amber)', n: actions.open, label: 'Çıxışı unudulub', onClick: () => navigate('/admin/open-records') },
            { color: 'var(--blue)', n: actions.devices, label: 'Cihaz təsdiqi gözləyir', onClick: () => navigate('/admin/device-changes') },
            { color: 'var(--clay)', n: actions.problems, label: 'Problemli skan (bu gün)', onClick: () => navigate('/admin/problems') },
          ].map((it) => {
            const calm = it.n === 0
            return (
              <button key={it.label} className={`lux-act${calm ? ' calm' : ''}`} onClick={calm ? undefined : it.onClick}>
                <span className="lux-act-dot" style={{ background: calm ? 'var(--leaf)' : it.color }} />
                <span className="lux-act-l">{it.label}</span>
                <span className="lux-act-n">{it.n}</span>
                {!calm && <span className="lux-act-ar">›</span>}
              </button>
            )
          })}
        </div>
      </section>
    </div>
  )
}

function Pill({ tone, n, label, active, onClick }:
  { tone: string; n: number; label: string; active?: boolean; onClick?: () => void }) {
  return (
    <button className={`lux-pill ${tone}${active ? ' active' : ''}${onClick ? '' : ' static'}`} onClick={onClick} disabled={!onClick}>
      <span className="lux-pill-n">{n}</span>
      <span className="lux-pill-l">{label}</span>
    </button>
  )
}
