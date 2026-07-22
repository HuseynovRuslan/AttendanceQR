import { useEffect, useRef, useState } from 'react'
import { getGroupOverview, type GroupOverview } from '../../api/hq'
import { SiteMap } from './SiteMap'
import 'leaflet/dist/leaflet.css'
import './hq.css'

/** Refresh cadence. Fast enough that a figure visibly moves while someone watches, slow enough that
 *  the board is not hammering the API all day on a wall screen. */
const REFRESH_MS = 20_000

/** One accent per company, assigned in creation order. Colours rather than logos: there are no logo
 *  files, and three flat accents stay legible from the back of a room where crests do not. */
const ACCENTS = ['#7CB342', '#38BDF8', '#F59E0B', '#A78BFA', '#F472B6']

const fmt = new Intl.NumberFormat('az-AZ')

function timeOf(iso: string): string {
  return new Date(iso).toLocaleTimeString('az-AZ', { hour: '2-digit', minute: '2-digit' })
}

/** Counts from the previous value to the new one. The movement is the point: it is what tells a
 *  viewer the number is live rather than a screenshot. */
function useCountUp(target: number, duration = 900): number {
  const [value, setValue] = useState(target)
  const fromRef = useRef(target)

  useEffect(() => {
    if (window.matchMedia?.('(prefers-reduced-motion: reduce)').matches) {
      fromRef.current = target
      setValue(target)
      return
    }
    const from = fromRef.current
    const start = performance.now()
    let raf = 0
    const tick = (now: number) => {
      const t = Math.min(1, (now - start) / duration)
      const eased = 1 - Math.pow(1 - t, 3)
      setValue(from + (target - from) * eased)
      if (t < 1) raf = requestAnimationFrame(tick)
      else fromRef.current = target
    }
    raf = requestAnimationFrame(tick)
    return () => cancelAnimationFrame(raf)
  }, [target, duration])

  return value
}

/** Fourteen days of group attendance as a filled area. Hand-drawn rather than pulled from a chart
 *  library: this needs two lines of SVG, and a dependency would have to be styled back down to this
 *  anyway. */
function TrendArea({ points }: { points: { date: string; present: number }[] }) {
  const W = 720
  const H = 190
  const PAD = 14

  if (points.length < 2) return <p style={{ color: 'var(--fg-faint)', fontSize: 13 }}>Məlumat toplanır…</p>

  const max = Math.max(1, ...points.map((p) => p.present))
  const stepX = (W - PAD * 2) / (points.length - 1)
  const y = (v: number) => H - PAD - (v / max) * (H - PAD * 2.4)
  const coords = points.map((p, i) => [PAD + i * stepX, y(p.present)] as const)

  // Catmull-Rom style smoothing: a straight polyline reads as jagged noise at this size, and a
  // curve makes the shape of the fortnight legible at a glance.
  const line = coords.reduce((d, [x, py], i, all) => {
    if (i === 0) return `M ${x} ${py}`
    const [px, ppy] = all[i - 1]
    const cx = (px + x) / 2
    return `${d} C ${cx} ${ppy}, ${cx} ${py}, ${x} ${py}`
  }, '')

  const last = coords[coords.length - 1]

  return (
    <svg viewBox={`0 0 ${W} ${H}`} style={{ width: '100%', height: 'auto', display: 'block' }}>
      <defs>
        <linearGradient id="hq-fill" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stopColor="#7CB342" stopOpacity="0.42" />
          <stop offset="100%" stopColor="#7CB342" stopOpacity="0" />
        </linearGradient>
      </defs>
      {[0.25, 0.5, 0.75, 1].map((f) => (
        <line key={f} x1={PAD} x2={W - PAD} y1={y(max * f)} y2={y(max * f)} stroke="rgba(255,255,255,0.06)" strokeWidth={1} />
      ))}
      <path d={`${line} L ${last[0]} ${H - PAD} L ${PAD} ${H - PAD} Z`} fill="url(#hq-fill)" />
      <path d={line} fill="none" stroke="#7CB342" strokeWidth={2.5} strokeLinecap="round" />
      {/* Only today's point is marked — the rest is context, this is where the eye should land. */}
      <circle cx={last[0]} cy={last[1]} r={5} fill="#7CB342" />
      <circle cx={last[0]} cy={last[1]} r={10} fill="#7CB342" opacity={0.22} />
    </svg>
  )
}

/**
 * Every company in the group on one live screen.
 *
 * Built to be shown, not worked in: the tenant panels answer "how is my company doing today", this
 * answers "how much is actually running on this system". Restricted to the super-admin allowlist,
 * because it is the only screen where the three companies appear together.
 */
export function GroupBoardPage() {
  const [data, setData] = useState<GroupOverview | null>(null)
  const [denied, setDenied] = useState(false)
  const [clock, setClock] = useState(() => new Date())
  const newestRef = useRef<string | null>(null)
  const [isFullscreen, setIsFullscreen] = useState(false)

  // A demo should not begin with someone hunting for F11 — and fullscreen also takes the address bar
  // away, which otherwise shows one company's subdomain above a screen about all three.
  function toggleFullscreen() {
    if (document.fullscreenElement) void document.exitFullscreen()
    else void document.documentElement.requestFullscreen?.()
  }

  useEffect(() => {
    const onChange = () => setIsFullscreen(!!document.fullscreenElement)
    document.addEventListener('fullscreenchange', onChange)
    return () => document.removeEventListener('fullscreenchange', onChange)
  }, [])

  useEffect(() => {
    let alive = true
    async function load() {
      const { status, data } = await getGroupOverview()
      if (!alive) return
      if (status === 403) { setDenied(true); return }
      if (status === 200 && data && 'totals' in data) setData(data)
    }
    void load()
    const poll = setInterval(() => void load(), REFRESH_MS)
    const tick = setInterval(() => setClock(new Date()), 1000)
    return () => { alive = false; clearInterval(poll); clearInterval(tick) }
  }, [])

  const totals = data?.totals
  const onDuty = Math.round(useCountUp(totals?.onDuty ?? 0))
  const employees = Math.round(useCountUp(totals?.employees ?? 0))
  const present = Math.round(useCountUp(totals?.present ?? 0))
  const scans = Math.round(useCountUp(totals?.totalScans ?? 0))

  if (denied) {
    return <div className="hq-gate">Bu səhifəyə giriş yalnız qrup administratoru üçündür.</div>
  }
  if (!data || !totals) {
    return <div className="hq-gate">Yüklənir…</div>
  }

  // The key of the most recent event, so only a genuinely new arrival animates.
  const topKey = data.feed[0] ? `${data.feed[0].fullName}-${data.feed[0].atUtc}` : null
  const isFresh = topKey !== null && topKey !== newestRef.current
  newestRef.current = topKey

  const accentOf = (i: number) => ACCENTS[i % ACCENTS.length]

  // This week against the one before it, straight out of the fortnight already on screen. Directors
  // read the arrow first and decide from it whether the number is worth reading.
  const half = Math.floor(data.trend.length / 2)
  const mean = (xs: { present: number }[]) => (xs.length ? xs.reduce((a, b) => a + b.present, 0) / xs.length : 0)
  const prevWeek = mean(data.trend.slice(0, half))
  const thisWeek = mean(data.trend.slice(half))
  const deltaPct = prevWeek === 0 ? 0 : Math.round(((thisWeek - prevWeek) / prevWeek) * 100)

  const hero = totals.onDuty > 0
    ? {
        label: 'İndi iş başında',
        value: onDuty,
        note: `Bu gün ${fmt.format(totals.present)} nəfər işə gəlib · davamiyyət ${totals.attendancePct}%`,
      }
    : totals.present > 0
      ? {
          label: 'Bu gün işə gəldi',
          value: present,
          note: `İş günü tamamlanıb · davamiyyət ${totals.attendancePct}%`,
        }
      : {
          label: 'Sistemdə qeydiyyatda',
          value: employees,
          note: `${totals.companies} şirkət · ${totals.locations} ərazi · bu gün hələ skan olmayıb`,
        }

  return (
    <div className="hq">
      <div className="hq-inner">
        <header className="hq-head hq-reveal hq-d1">
          <div className="hq-brand">
            <div className="hq-mark">Q</div>
            <div>
              <div className="hq-title">Qrup İdarəetmə Paneli</div>
              <div className="hq-sub">
                QRLog · {totals.companies} şirkət · {totals.locations} ərazi
              </div>
            </div>
          </div>
          <div style={{ display: 'flex', alignItems: 'center' }}>
            <span className="hq-live"><i />CANLI</span>
            <span className="hq-clock hq-num">
              {clock.toLocaleTimeString('az-AZ', { hour: '2-digit', minute: '2-digit', second: '2-digit' })}
            </span>
            <button
              type="button"
              className="hq-fs"
              onClick={toggleFullscreen}
              title={isFullscreen ? 'Tam ekrandan çıx' : 'Tam ekran'}
              aria-label={isFullscreen ? 'Tam ekrandan çıx' : 'Tam ekran'}
            >
              <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.4" strokeLinecap="round">
                {isFullscreen
                  ? <><path d="M9 3v6H3" /><path d="M15 21v-6h6" /></>
                  : <><path d="M3 9V3h6" /><path d="M21 15v6h-6" /></>}
              </svg>
            </button>
          </div>
        </header>

        {/* The one number worth putting on a wall. Which number that IS depends on the hour: after
            the last shift ends "İndi iş başında" is honestly zero, and a board whose headline figure
            is a huge 0 reads as broken to anyone who doesn't know the shift pattern — which is
            exactly who this screen gets shown to. It falls back to the largest true statement
            available instead. */}
        <section className="hq-hero hq-reveal hq-d2">
          <div>
            <div className="hq-hero-label">{hero.label}</div>
            <div className="hq-hero-value hq-num">
              {fmt.format(hero.value)}<span className="hq-hero-unit">nəfər</span>
            </div>
            <div className="hq-hero-note">{hero.note}</div>
          </div>
          <div className="hq-stats">
            <div className="hq-stat">
              <div className="v hq-num">{fmt.format(employees)}</div>
              <div className="l">Ümumi işçi</div>
            </div>
            <div className="hq-stat">
              <div className="v hq-num">{totals.companies}</div>
              <div className="l">Şirkət</div>
            </div>
            <div className="hq-stat">
              <div className="v hq-num">{totals.locations}</div>
              <div className="l">Ərazi</div>
            </div>
            <div className="hq-stat">
              <div className="v hq-num">{totals.attendancePct}%</div>
              <div className="l">Bugünkü davamiyyət</div>
            </div>
          </div>
        </section>

        <section className="hq-companies hq-reveal hq-d3">
          {data.companies.map((c, i) => (
            <article className="hq-co" key={c.id} style={{ ['--accent' as string]: accentOf(i) }}>
              <div className="hq-co-name">{c.name}</div>
              <div className="hq-co-meta">{c.locations} ərazi · {fmt.format(c.employees)} işçi</div>
              <div className="hq-co-row">
                <div className="hq-co-big hq-num">
                  {fmt.format(c.onDuty)}<small>/ {fmt.format(c.employees)}</small>
                </div>
                <div className="hq-co-pct hq-num">{c.attendancePct}%</div>
              </div>
              <div className="hq-bar">
                <i style={{ width: `${Math.min(100, c.attendancePct)}%` }} />
              </div>
            </article>
          ))}
        </section>

        <section className="hq-grid hq-reveal hq-d4">
          {/* The map leads, not the chart: a director recognises their own sites in a second, and
              "our people are at these places right now" is the thing a table cannot say. */}
          <div className="hq-panel">
            <div className="hq-panel-title">
              Ərazilər · hazırda iş gedən nöqtələr
            </div>
            <SiteMap sites={data.sites} accentOf={accentOf} />
          </div>

          {/* The feed is what makes the screen read as live: rows arrive while you are looking at it. */}
          <div className="hq-panel">
            <div className="hq-panel-title">Canlı hərəkət</div>
            <div className="hq-feed">
              {data.feed.length === 0 && (
                <p style={{ color: 'var(--fg-faint)', fontSize: 13 }}>Bu gün hələ skan olmayıb.</p>
              )}
              {data.feed.map((f, i) => {
                const companyIndex = data.companies.findIndex((c) => c.name === f.company)
                return (
                  <div
                    className={`hq-feed-row${i === 0 && isFresh ? ' is-new' : ''}`}
                    key={`${f.fullName}-${f.atUtc}-${f.kind}`}
                  >
                    <span className="hq-feed-time hq-num">{timeOf(f.atUtc)}</span>
                    <span className="hq-feed-name">{f.fullName}</span>
                    <span
                      className="hq-feed-co"
                      style={{
                        color: accentOf(companyIndex < 0 ? 0 : companyIndex),
                        background: `${accentOf(companyIndex < 0 ? 0 : companyIndex)}1f`,
                      }}
                    >
                      {f.company}
                    </span>
                    <span className={`hq-feed-kind ${f.kind === 'in' ? 'hq-in' : 'hq-out'}`}>
                      {f.kind === 'in' ? 'GİRİŞ' : 'ÇIXIŞ'}
                    </span>
                  </div>
                )
              })}
            </div>
          </div>
        </section>

        <section className="hq-panel hq-reveal hq-d5">
          <div className="hq-panel-title">
            Son 14 gün · qrup üzrə davamiyyət
            <span className={`hq-delta ${deltaPct > 0 ? '' : deltaPct < 0 ? 'down' : 'flat'}`}>
              {deltaPct > 0 ? '▲' : deltaPct < 0 ? '▼' : '■'} {Math.abs(deltaPct)}%
              <span style={{ fontWeight: 600, opacity: 0.75 }}>keçən həftəyə görə</span>
            </span>
          </div>
          <TrendArea points={data.trend} />
        </section>

        {/* The figures say the system is used; this says what is being used. Without it a director
            sees a chart, not a product. */}
        <section className="hq-caps hq-reveal hq-d6">
          {['QR ilə giriş', 'GPS ərazi nəzarəti', 'Üz yoxlaması', 'Oflayn skan', 'Push bildiriş', 'Maaş hesabatı', 'Ayın işçisi']
            .map((cap) => <span className="hq-cap" key={cap}><i />{cap}</span>)}
        </section>

        <footer className="hq-foot">
          {/* Uptime is only worth saying once it is long. On a young system "15 gündür işləyir" reads
              as "brand new" — the opposite of the reliability it was meant to claim — so below a
              couple of months the line simply doesn't make the claim. */}
          <span>
            {totals.daysLive >= 60 && (
              <>
                <b className="hq-num" style={{ color: 'var(--fg)' }}>{fmt.format(totals.daysLive)}</b> gündür
                fasiləsiz işləyir ·{' '}
              </>
            )}
            <b className="hq-num" style={{ color: 'var(--fg)' }}>{fmt.format(scans)}</b> giriş qeydə alınıb
            {' · '}<b className="hq-num" style={{ color: 'var(--fg)' }}>{fmt.format(totals.employees)}</b> işçi
          </span>
          <span>Hər {REFRESH_MS / 1000} saniyədə avtomatik yenilənir</span>
        </footer>
      </div>
    </div>
  )
}
