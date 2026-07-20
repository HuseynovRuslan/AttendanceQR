import { useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import {
  getDashboard, getMyLocations, getPendingDeviceChanges, getProblems, getToday,
  type DashboardReport, type DayAttendanceRow, type LocationDto,
} from '../../api/admin'
import { getOpenRecords } from '../../api/attendance'
import { usePolling } from '../../lib/usePolling'
import { DateRangePicker } from '../../components/DateRangePicker'
import { ChartLegend, TrendChart, WeekdayBarChart } from '../../components/Charts'
import { EmployeeLink } from '../../components/EmployeeLink'
import { IconX } from '../../components/icons'
import { fmtDate, fmtHM, fmtTime } from '../../lib/format'
import { initials } from '../../lib/att'

// Accent for one employee row, by their own state — colour-codes the list at a glance.
const BUCKET_COLOR: Record<string, string> = { in: 'var(--blue)', done: 'var(--leaf)', absent: 'var(--clay)', off: 'var(--purple)' }
const BUCKET_ICON: Record<string, { icon: string; accent: string }> = {
  in: { icon: '🏢', accent: 'blue' },
  done: { icon: '✅', accent: 'leaf' },
  absent: { icon: '🚫', accent: 'clay' },
  total: { icon: '👥', accent: 'leaf' },
}

// The bucket a today-row belongs to — mirrors the hero counts exactly so the pill and the list agree.
type Bucket = 'in' | 'done' | 'absent'
function bucketOf(status: string): Bucket | 'off' {
  if (status === 'OnTime' || status === 'Late') return 'done'
  if (status === 'Absent') return 'absent'
  if (status === 'DayOff' || status === 'OnLeave' || status === 'Permission') return 'off'
  return 'in'
}
const BUCKET_LABEL: Record<string, string> = { in: 'İşdə', done: 'Tamamlayıb', absent: 'Qayıb', total: 'Ümumi işçi' }

function rateColor(rate: number): string {
  return rate >= 80 ? 'var(--leaf-d)' : rate >= 50 ? 'var(--amber)' : 'var(--clay)'
}

/** Eases a number from its previous value to the new target (easeOutCubic). On first mount it counts
 *  up from 0; on later updates (the 30s poll) it ticks smoothly from the old value — never a jarring
 *  reset. Honours reduced-motion by jumping straight to the target. */
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

const todayIso = () => new Date().toISOString().slice(0, 10)
const daysAgoIso = (n: number) => new Date(Date.now() - n * 86_400_000).toISOString().slice(0, 10)

// On a large total the odd minutes are noise and only make the number wrap — round to whole hours
// past 100h, keep "saat X dəq" for the small ones where the minutes still matter.
const fmtHours = (h: number) => (h >= 100 ? `${Math.round(h)} saat` : fmtHM(h))

/** A premium section header — an accent icon chip + title + subtitle + a fading rule — that divides the
 *  dashboard into clear bands. `accent` is one of the theme colour families (leaf/blue/clay/amber/…). */
function Section({ title, sub, accent = 'leaf' }: { title: string; sub: string; accent?: string }) {
  return (
    <div className="dash-section">
      <span className="dash-section-bar" style={{ background: `var(--${accent})` }} />
      <div>
        <div className="dash-section-t">{title}</div>
        <div className="dash-section-s">{sub}</div>
      </div>
      <div className="dash-section-line" />
    </div>
  )
}

/** One tile. `tone` is the semantic status, not decoration: leaf = fine, blue = in progress,
 *  clay = wrong, amber = needs a person to act, purple = not expected in today. */
function Stat({ tone, label, value, sub, icon }: { tone?: string; label: string; value: number | string; sub: string; icon?: string }) {
  return (
    <div className={tone ? `stat-card ${tone}` : 'stat-card'}>
      {icon && <div className="stat-ic">{icon}</div>}
      <div className="stat-lbl">{label}</div>
      <div className="stat-val">{value}</div>
      <div className="stat-sub">{sub}</div>
    </div>
  )
}

export function DashboardPage() {
  const navigate = useNavigate()
  const [rows, setRows] = useState<DayAttendanceRow[]>([])
  const [error, setError] = useState<string | null>(null)
  const [openBucket, setOpenBucket] = useState<string | null>(null)
  // Action-center counts: past open records, pending device approvals, today's rejected scans.
  const [actions, setActions] = useState({ open: 0, devices: 0, problems: 0 })

  usePolling(async () => {
    const [today, open, devices, problems] = await Promise.all([
      getToday(),
      getOpenRecords(),
      getPendingDeviceChanges(),
      getProblems(todayIso()),
    ])
    if (today.status === 200 && Array.isArray(today.data)) {
      setRows(today.data)
      setError(null)
    } else if (today.status === 403) {
      setError('İcazəniz yoxdur')
    } else {
      setError('Məlumat yüklənmədi')
    }
    setActions({
      open: Array.isArray(open.data) ? open.data.length : 0,
      devices: Array.isArray(devices.data) ? devices.data.length : 0,
      problems: problems.data && 'rejectedCount' in problems.data ? problems.data.rejectedCount : 0,
    })
  }, 30_000)

  // Live activity feed — the latest check-ins/outs today, built from the rows we already have.
  const activity = useMemo(() => {
    const ev: { id: string; name: string; loc: string; type: 'in' | 'out'; at: string }[] = []
    for (const r of rows) {
      if (r.checkInAtUtc) ev.push({ id: r.employeeId + 'i', name: r.employeeName, loc: r.locationName, type: 'in', at: r.checkInAtUtc })
      if (r.checkOutAtUtc) ev.push({ id: r.employeeId + 'o', name: r.employeeName, loc: r.locationName, type: 'out', at: r.checkOutAtUtc })
    }
    return ev.sort((a, b) => (a.at < b.at ? 1 : -1)).slice(0, 12)
  }, [rows])

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

  // Says which days the band below is counting. "Bu gün" when the range is just today, so the two
  // bands do not silently look like the same question asked twice.
  const rangeHint =
    dashFrom === dashTo
      ? dashFrom === todayIso()
        ? 'Yalnız bu gün'
        : fmtDate(dashFrom)
      : `${fmtDate(dashFrom)} – ${fmtDate(dashTo)}`

  useEffect(() => {
    getMyLocations().then(({ status, data }) => {
      if (status === 200 && Array.isArray(data)) setLocations(data)
    })
  }, [])

  // Accepts explicit from/to/locationId so a preset pick (DateRangePicker) can load with the fresh
  // values immediately, without waiting on setState to settle first.
  async function loadDashboard(f = dashFrom, t = dashTo, locId = dashLocationId) {
    setDashError(null)
    setDashLoading(true)
    const { status, data } = await getDashboard(f, t, locId || undefined)
    if (status === 200 && data && 'trend' in data) setDashReport(data)
    else if (status === 403) setDashError('İcazəniz yoxdur')
    else setDashError('Məlumat yüklənmədi')
    setDashLoading(false)
  }

  // eslint-disable-next-line react-hooks/exhaustive-deps
  useEffect(() => {
    void loadDashboard()
  }, [])

  const RING_R = 62
  const RING_C = 2 * Math.PI * RING_R
  const ringColor = rateColor(overallRate)
  const ringVal = useCountUp(overallRate)
  const cIn = Math.round(useCountUp(counts.incomplete))
  const cDone = Math.round(useCountUp(counts.present))
  const cAbsent = Math.round(useCountUp(counts.absent))
  const cTotal = Math.round(useCountUp(total))

  return (
    <div className="dash-premium">
      {/* Executive hero — the live "today" picture, led by the attendance ring. */}
      <div className="dash-hero">
        <div className="dash-hero-inner">
          <div className="dash-ring">
            <svg width="150" height="150" viewBox="0 0 150 150">
              <circle cx="75" cy="75" r={RING_R} fill="none" stroke="rgba(255,255,255,0.12)" strokeWidth="12" />
              <circle
                cx="75" cy="75" r={RING_R} fill="none" stroke={ringColor} strokeWidth="12" strokeLinecap="round"
                strokeDasharray={RING_C}
                strokeDashoffset={RING_C - (ringVal / 100) * RING_C}
                style={{ transition: 'stroke .4s', filter: `drop-shadow(0 0 6px ${ringColor})` }}
              />
            </svg>
            <div className="dash-ring-c">
              <b>{Math.round(ringVal)}%</b>
              <span>davamiyyət</span>
            </div>
          </div>

          <div className="dash-hero-main">
            <div className="dash-eyebrow"><i />Bu gün · Canlı</div>
            <h2>Davamiyyət icmalı</h2>
            <div className="dash-hero-sub">
              {expected} nəfərdən <b>{attended}</b>-i işə gəlib · hər 30 saniyədə yenilənir
            </div>

            <div className="dash-pills">
              {[
                { key: 'in', v: cIn, label: 'İşdə', dot: 'var(--blue)' },
                { key: 'done', v: cDone, label: 'Tamamlayıb', dot: 'var(--leaf)' },
                { key: 'absent', v: cAbsent, label: 'Qayıb', dot: 'var(--clay)' },
                { key: 'total', v: cTotal, label: 'Ümumi işçi', dot: 'rgba(255,255,255,.4)' },
              ].map((p) => (
                <div
                  key={p.key}
                  className={`dash-pill${openBucket === p.key ? ' active' : ''}`}
                  onClick={() => setOpenBucket((b) => (b === p.key ? null : p.key))}
                  title="İşçi siyahısını aç"
                >
                  <div className="v">{p.v}</div>
                  <div className="l"><i style={{ background: p.dot }} />{p.label}</div>
                </div>
              ))}
            </div>

            {notExpected > 0 && (
              <div className="dash-hero-note">
                Bu gün gözlənilmir: {[
                  counts.dayOff && `${counts.dayOff} istirahət`,
                  counts.onLeave && `${counts.onLeave} məzuniyyət`,
                  counts.permission && `${counts.permission} icazə`,
                ].filter(Boolean).join(' · ')}
              </div>
            )}
          </div>
        </div>
      </div>

      {/* Tapping a hero pill opens the matching employee list right below — so "Qayıb 16" is one
          click from knowing exactly who. */}
      {openBucket && (() => {
        const list = (openBucket === 'total' ? rows : rows.filter((r) => bucketOf(r.status) === openBucket))
          .slice()
          .sort((a, b) => a.employeeName.localeCompare(b.employeeName))
        const head = BUCKET_ICON[openBucket] ?? BUCKET_ICON.total
        return (
          <div className="card card-pad dash-list" style={{ marginBottom: 22 }}>
            <div className="dash-list-head">
              <span className="dash-section-bar" style={{ background: `var(--${head.accent})`, height: 34 }} />
              <div style={{ flex: 1 }}>
                <div className="dash-list-t">{BUCKET_LABEL[openBucket]}</div>
                <div className="dash-list-c">{list.length} işçi</div>
              </div>
              <button className="btn btn-sm" onClick={() => setOpenBucket(null)} aria-label="Bağla"><IconX /></button>
            </div>
            {list.length === 0 ? (
              <div className="muted" style={{ padding: '8px 0' }}>Bu qrupda işçi yoxdur</div>
            ) : (
              <div className="dash-list-grid">
                {list.map((r) => {
                  const b = bucketOf(r.status)
                  const color = BUCKET_COLOR[b] ?? 'var(--c400)'
                  return (
                    <div key={r.employeeId} className="dash-emp" style={{ borderLeftColor: color }}>
                      <div className="dash-emp-av" style={{ background: color }}>{initials(r.employeeName)}</div>
                      <div className="dash-emp-main">
                        <div className="dash-emp-nm"><EmployeeLink id={r.employeeId} name={r.employeeName} /></div>
                        <div className="dash-emp-loc">{r.locationName}</div>
                      </div>
                      <div className="dash-emp-time">
                        {r.checkInAtUtc ? (
                          <>
                            <div>↙ {fmtTime(r.checkInAtUtc)}</div>
                            {r.checkOutAtUtc && <div>↗ {fmtTime(r.checkOutAtUtc)}</div>}
                          </>
                        ) : (
                          <span className="absent">Gəlməyib</span>
                        )}
                      </div>
                    </div>
                  )
                })}
              </div>
            )}
          </div>
        )
      })()}

      {error && (
        <div className="fb fb-err" style={{ marginBottom: 12 }}>
          <IconX />
          <span>{error}</span>
        </div>
      )}

      {/* Only worth a panel when there is more than one branch to compare. With a single branch its
          rows were the same number as the hero above, said twice. */}
      {areaStats.length > 1 && (
        <>
        <Section title="Filiallar üzrə" sub="Bu gün — filial performansı" accent="blue" />
        <div className="card card-pad" style={{ marginBottom: 6 }}>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
            {areaStats.map((a) => (
              <div key={a.name}>
                <div style={{ display: 'flex', alignItems: 'baseline', justifyContent: 'space-between', marginBottom: 6 }}>
                  <div style={{ fontWeight: 700, fontSize: 13, color: 'var(--c900)' }}>{a.name}</div>
                  <div style={{ display: 'flex', alignItems: 'baseline', gap: 8 }}>
                    <span style={{ fontSize: 12, color: 'var(--c400)' }}>{a.attended}/{a.expected}</span>
                    <span style={{ fontFamily: "'Manrope',sans-serif", fontWeight: 800, fontSize: 17, color: rateColor(a.rate) }}>
                      {a.rate}%
                    </span>
                  </div>
                </div>
                <div className="dash-bar">
                  <i style={{ width: `${a.rate}%`, background: rateColor(a.rate) }} />
                </div>
              </div>
            ))}
          </div>
        </div>
        </>
      )}

      {/* --- Period report section --- */}
      <Section title="Dövr hesabatı" sub={`Seçilmiş aralıq: ${rangeHint}`} accent="leaf" />

      <div className="card card-pad" style={{ marginBottom: 16 }}>
        <div className="card-title">Filial və tarix aralığı seçin</div>
        <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'flex-end', gap: 12 }}>
          <div style={{ minWidth: 180 }}>
            <label className="form-label">Filial</label>
            <select className="inp" value={dashLocationId} onChange={(e) => setDashLocationId(e.target.value)}>
              <option value="">Hamısı</option>
              {locations.map((l) => (
                <option key={l.id} value={l.id}>{l.name}</option>
              ))}
            </select>
          </div>
          <DateRangePicker
            from={dashFrom}
            to={dashTo}
            onApply={(f, t) => {
              setDashFrom(f)
              setDashTo(t)
              void loadDashboard(f, t)
            }}
          />
          <button className="btn btn-primary" onClick={() => void loadDashboard()} disabled={dashLoading}>
            {dashLoading ? 'Yüklənir…' : 'Yenilə'}
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
            <Stat tone="leaf" label="Girişlər" value={dashReport.totalCheckIns} sub="Dövr ərzində qeydə alınan giriş" />
            <Stat tone="blue" label="Çıxışlar" value={dashReport.totalCheckOuts} sub="Dövr ərzində qeydə alınan çıxış" />
            <Stat tone="clay" label="Qayıb günləri" value={dashReport.absentCount} sub="Heç giriş edilməyən iş günü" />
            <Stat tone="leaf" label="İşlənən saat" value={fmtHours(dashReport.totalWorkedHours)} sub="Bütün işçilərin cəmi" />
            {/* "Overtime" was the only English word on an Azerbaijani screen. */}
            <Stat tone="amber" label="Əlavə iş saatı" value={fmtHours(dashReport.overtimeHours)} sub="Növbə bitdikdən sonra işlənən" />
            {/* Days off / leave / permission only earn a tile when there is something to report. */}
            {dashReport.dayOffCount > 0 && (
              <Stat tone="purple" label="İstirahət" value={dashReport.dayOffCount} sub="İş günü olmayan günlər" />
            )}
            {dashReport.leaveCount > 0 && (
              <Stat tone="purple" label="Məzuniyyət" value={dashReport.leaveCount} sub="Təsdiqlənmiş məzuniyyət günləri" />
            )}
            {dashReport.permissionCount > 0 && (
              <Stat tone="purple" label="İcazə" value={dashReport.permissionCount} sub="Təsdiqlənmiş icazə günləri" />
            )}
          </div>

          <Section title="Trendlər" sub="Giriş / çıxış dinamikası və həftəlik model" accent="blue" />
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(320px, 1fr))', gap: 16, marginBottom: 4 }}>
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
          <Section title="Ümumi baxış" sub="Dövr üzrə nisbətlər" accent="leaf" />
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(320px, 1fr))', gap: 16 }}>
            <div className="card card-pad">
              <div style={{ display: 'grid', gridTemplateColumns: '1fr auto', rowGap: 10, fontSize: 13 }}>
                <span className="muted">Seçilmiş tarix aralığı</span>
                <span className="mono" style={{ textAlign: 'right' }}>{dashReport.from} – {dashReport.to}</span>
                <span className="muted">Giriş/Çıxış nisbəti</span>
                <span className="mono" style={{ textAlign: 'right' }}>{dashReport.checkInOutRatio}%</span>
              </div>
            </div>
          </div>
        </>
      )}

      {/* Action center + live activity — moved below the report per feedback. */}
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(300px, 1fr))', gap: 16, marginTop: 22 }}>
        <div className="card card-pad">
          <div className="card-title">Diqqət mərkəzi</div>
          <div className="dash-actions">
            {[
              { color: 'var(--clay)', n: counts.absent, label: 'Bu gün gəlməyib', onClick: () => setOpenBucket('absent') },
              { color: 'var(--amber)', n: actions.open, label: 'Çıxışı unudulub', onClick: () => navigate('/admin/open-records') },
              { color: 'var(--blue)', n: actions.devices, label: 'Cihaz təsdiqi gözləyir', onClick: () => navigate('/admin/device-changes') },
              { color: 'var(--clay)', n: actions.problems, label: 'Problemli skan (bu gün)', onClick: () => navigate('/admin/problems') },
            ].map((it) => {
              const calm = it.n === 0
              return (
                <button key={it.label} className={`dash-act${calm ? ' calm' : ''}`} onClick={calm ? undefined : it.onClick}>
                  <span className="dash-act-dot" style={{ background: calm ? 'var(--leaf)' : it.color }} />
                  <span className="dash-act-l">{it.label}</span>
                  <span className="dash-act-n" style={{ color: calm ? 'var(--leaf-d)' : 'var(--c900)' }}>{it.n}</span>
                  {!calm && <span className="dash-act-ar">›</span>}
                </button>
              )
            })}
          </div>
        </div>

        <div className="card card-pad">
          <div className="card-title">Son fəaliyyət</div>
          {activity.length === 0 ? (
            <div className="muted" style={{ padding: '8px 0' }}>Bu gün hələ hərəkət yoxdur</div>
          ) : (
            <div className="dash-feed">
              {activity.map((e) => (
                <div key={e.id} className="dash-feed-row">
                  <span className="dash-feed-dot" style={{ background: e.type === 'in' ? 'var(--leaf)' : 'var(--blue)' }} />
                  <div className="dash-feed-main">
                    <div className="dash-feed-nm">{e.name}</div>
                    <div className="dash-feed-a">{e.type === 'in' ? 'Giriş etdi' : 'Çıxış etdi'} · {e.loc}</div>
                  </div>
                  <span className="dash-feed-t">{fmtTime(e.at)}</span>
                </div>
              ))}
            </div>
          )}
        </div>
      </div>
    </div>
  )
}
