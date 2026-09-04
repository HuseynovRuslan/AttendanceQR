import { useEffect, useRef, useState } from 'react'
import { getGroupOverview, type GroupOverview } from '../../api/hq'
import { SiteMap } from './SiteMap'
import 'leaflet/dist/leaflet.css'
import './hq.css'
import { COMPANY_TZ } from '../../lib/format'
import { clearToken, getImpersonation, exitImpersonation } from '../../api/client'
import { CompanyDrawer } from './CompanyDrawer'
import { fmt, timeOf } from './format'

/**
 * The Baku clock, isolated in its own component ON PURPOSE.
 *
 * It ticks every second, and a second's tick used to re-render the entire board with it. That was
 * free while the feed held fourteen rows. It stopped being free the day the feed became the whole
 * day's register: at ~750 events that is thousands of nodes reconciled every second, next to a
 * Leaflet map and an SVG trend — the board went from live to sluggish, and on a phone it locked up.
 *
 * A component that owns its own state re-renders only itself. The clock is the only thing on this
 * screen that changes every second, so it is the only thing that should redraw every second.
 */
function BakuClock() {
  const [now, setNow] = useState(() => new Date())
  useEffect(() => {
    const t = setInterval(() => setNow(new Date()), 1000)
    return () => clearInterval(t)
  }, [])
  return (
    <span className="hq-clock hq-num">
      {now.toLocaleTimeString('az-AZ', { hour: '2-digit', minute: '2-digit', second: '2-digit', timeZone: COMPANY_TZ })}
    </span>
  )
}

/** Refresh cadence. Fast enough that a figure visibly moves while someone watches, slow enough that
 *  the board is not hammering the API all day on a wall screen. */
const REFRESH_MS = 20_000

/**
 * Feed rows put in the DOM at once.
 *
 * The payload carries the whole day — the director asked for everyone, and «everyone» is right: a
 * board that shows the last fourteen events cannot answer "did Elnur come in". But everyone in the
 * DOM is a different thing from everyone in the data. At ~750 events, rendering them all made the
 * board sluggish and locked it up on a phone.
 *
 * So the data stays whole and the SCREEN is windowed: the newest rows, a box to search the rest by
 * name, company or site, and a button for more. Searching is also the better answer to "find one
 * person" than a list of 750 rows they would have to scroll.
 */
const FEED_PAGE = 60

/** One accent per company, assigned in creation order. Colours rather than logos: there are no logo
 *  files, and three flat accents stay legible from the back of a room where crests do not. */
const ACCENTS = ['#7CB342', '#38BDF8', '#F59E0B', '#A78BFA', '#F472B6']

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
/**
 * @param embedded — mounted as the operator console's İcmal rather than standing alone at /hq.
 *   The console already carries the QRLog lockup in its sidebar and the page name in its topbar, so
 *   the board drops its own brand plate there and keeps only what the shell does NOT say: the live
 *   dot, the Baku clock and the fullscreen button.
 */
export function GroupBoardPage({ embedded = false }: { embedded?: boolean } = {}) {
  const [data, setData] = useState<GroupOverview | null>(null)
  const [denied, setDenied] = useState(false)
  const [failed, setFailed] = useState<number | null>(null)
  const [feedQuery, setFeedQuery] = useState('')
  const [feedShown, setFeedShown] = useState(FEED_PAGE)
  const newestRef = useRef<string | null>(null)
  const [isFullscreen, setIsFullscreen] = useState(false)
  // The OPEN company's id, not a copy of its row. The board refreshes every 20 seconds; holding a
  // snapshot would freeze the panel's numbers the moment it opened — on a live board, the one place
  // someone is looking closely would be the one place the figures stopped moving. Resolved against
  // the current payload on every render instead, so the panel counts up with the board behind it.
  const [selectedId, setSelectedId] = useState<string | null>(null)

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
      if (status === 200 && data && 'totals' in data) { setData(data); setFailed(null); return }
      // ANY other answer — 401 on an expired token, a network drop, a 500 — used to fall through to
      // nothing, and the board sat on «Yüklənir…» forever. A spinner that never resolves is the
      // worst of the three outcomes: the reader cannot tell a slow morning from a broken session,
      // and there is nothing on the screen to act on. Only say it once the board has NEVER loaded;
      // a live board that has data already should keep showing it through a blip.
      if (!data || !('totals' in data)) setFailed(status)
    }
    void load()
    const poll = setInterval(() => void load(), REFRESH_MS)
    return () => { alive = false; clearInterval(poll) }
  }, [])

  const totals = data?.totals
  const onDuty = Math.round(useCountUp(totals?.onDuty ?? 0))
  const employees = Math.round(useCountUp(totals?.employees ?? 0))
  const present = Math.round(useCountUp(totals?.present ?? 0))
  const scans = Math.round(useCountUp(totals?.totalScans ?? 0))

  if (denied) {
    // A 403 here has two very different causes, and telling them apart is the difference between a
    // dead end and a working screen. Someone genuinely not on the allowlist needs the sentence. But
    // an operator who is INSIDE a company session is refused for a different reason — the token is a
    // tenant token, not their own — and until this branch existed the board simply told them they
    // were not allowed on their own board, with no way back. That is exactly what happened to the
    // group head at 09:56 today.
    const inside = getImpersonation()
    if (inside) {
      return (
        <div className="hq-gate">
          <p><b>{inside.tenantName}</b> şirkətinin seansındasınız — qrup lövhəsi bu seansda açılmır.</p>
          <button
            type="button"
            className="hq-gate-btn"
            onClick={() => { exitImpersonation(); window.location.reload() }}
          >
            Seansdan çıx və lövhəyə qayıt
          </button>
        </div>
      )
    }
    return <div className="hq-gate">Bu səhifəyə giriş yalnız qrup administratoru üçündür.</div>
  }
  if (!data || !totals) {
    if (failed !== null) {
      // 401 is its own thing: the token expired (a view session's token is short-lived), and the one
      // action that fixes it is signing in again — not waiting, not reloading.
      const expired = failed === 401
      return (
        <div className="hq-gate">
          <p>
            {expired
              ? 'Seansın vaxtı bitib — məlumat gəlmir.'
              : `Məlumat gəlmədi (${failed || 'şəbəkə'}).`}
          </p>
          <button
            type="button"
            className="hq-gate-btn"
            onClick={() => {
              if (expired) { exitImpersonation(); clearToken() }
              window.location.href = expired ? '/login' : '/hq'
            }}
          >
            {expired ? 'Yenidən giriş et' : 'Yenidən yüklə'}
          </button>
        </div>
      )
    }
    return <div className="hq-gate">Yüklənir…</div>
  }

  // The key of the most recent event, so only a genuinely new arrival animates.
  const topKey = data.feed[0] ? `${data.feed[0].fullName}-${data.feed[0].atUtc}` : null
  const isFresh = topKey !== null && topKey !== newestRef.current
  newestRef.current = topKey

  const accentOf = (i: number) => ACCENTS[i % ACCENTS.length]
  // -1 when nothing is open OR the open company has gone from the payload. `sites` carry the same
  // index (companyIndex), so this is also what keeps the panel's branch list correct.
  const selectedIndex = selectedId ? data.companies.findIndex((c) => c.id === selectedId) : -1

  // Searched over the WHOLE day, not over the rows that happen to be on screen — otherwise the box
  // would only search what the reader could already see, which is no help at all.
  const q = feedQuery.trim().toLocaleLowerCase('az-AZ')
  const feed = q
    ? data.feed.filter((f) =>
        f.fullName.toLocaleLowerCase('az-AZ').includes(q) ||
        f.company.toLocaleLowerCase('az-AZ').includes(q) ||
        f.location.toLocaleLowerCase('az-AZ').includes(q))
    : data.feed

  // This week against the one before it, straight out of the fortnight already on screen. Directors
  // read the arrow first and decide from it whether the number is worth reading.
  const half = Math.floor(data.trend.length / 2)
  const mean = (xs: { present: number }[]) => (xs.length ? xs.reduce((a, b) => a + b.present, 0) / xs.length : 0)
  const prevWeek = mean(data.trend.slice(0, half))
  const thisWeek = mean(data.trend.slice(half))
  const deltaPct = prevWeek === 0 ? 0 : Math.round(((thisWeek - prevWeek) / prevWeek) * 100)

  // Everyone the system can see: total staff minus those who have never once opened the app. This is
  // attendancePct's denominator, and it has to be ON SCREEN next to the percentage. It was not, and
  // the board contradicted itself in the only way a director would certainly notice: the header said
  // «656 işçi», the hero said «224 gəlib», and between them sat «68%» — while 224/656 is 34%. The
  // percentage was right and the reader had no way to know why.
  const observed = totals.employees - totals.notStarted

  const hero = totals.onDuty > 0
    ? {
        label: 'İndi iş başında',
        value: onDuty,
        note: `Bu gün ${fmt.format(totals.present)} / ${fmt.format(observed)} nəfər işə gəlib — ${totals.attendancePct}% · ${fmt.format(totals.notStarted)} nəfər tətbiqi hələ açmayıb`,
      }
    : totals.present > 0
      ? {
          label: 'Bu gün işə gəldi',
          value: present,
          note: `İş günü tamamlanıb · ${fmt.format(totals.present)} / ${fmt.format(observed)} — ${totals.attendancePct}%`,
        }
      : {
          label: 'Sistemdə qeydiyyatda',
          value: employees,
          note: `${totals.companies} şirkət · ${totals.locations} filial · bu gün hələ skan olmayıb`,
        }

  return (
    <div className={embedded ? 'hq hq-embedded' : 'hq'}>
      <div className="hq-inner">
        <header className="hq-head hq-reveal hq-d1">
          <div className="hq-brand">
            {/* The real lockup, on a white plate: the mark is dark navy and would vanish into a dark
                board. With the brand carrying the name, the title beside it no longer has to shout —
                it states what the screen is and hands the emphasis back to the logo. */}
            {!embedded && <>
              <img className="hq-logo" src="/brand/qrlog-logo.png" alt="QRLog" />
              <span className="hq-rule" aria-hidden="true" />
            </>}
            <div>
              <div className="hq-title">Qrup idarəetmə paneli</div>
              <div className="hq-sub">
                {totals.companies} şirkət · {totals.locations} filial · {fmt.format(totals.employees)} işçi
              </div>
            </div>
          </div>
          <div style={{ display: 'flex', alignItems: 'center' }}>
            <span className="hq-live"><i />CANLI</span>
            <BakuClock />
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
              <div className="l">Filial</div>
            </div>
            <div className="hq-stat">
              <div className="v hq-num">{totals.attendancePct}%</div>
              <div className="l">Bugünkü davamiyyət</div>
            </div>
          </div>
        </section>

        <section className="hq-companies hq-reveal hq-d3">
          {data.companies.map((c, i) => (
            // A button, not an article: the card IS the way into that company. The group head reads
            // the totals here and taps the one that needs a closer look, which opens that company's
            // panel over the board (CompanyDrawer) — the board itself is never left, and the full
            // admin panel stays one further, deliberate click away inside it.
            <button
              type="button"
              className={`hq-co${selectedId === c.id ? ' is-active' : ''}`}
              key={c.id}
              style={{ ['--accent' as string]: accentOf(i) }}
              onClick={() => setSelectedId(c.id)}
              title={`${c.name} — ətraflı`}
              aria-haspopup="dialog"
            >
              <div className="hq-co-name">{c.name}</div>
              <div className="hq-co-meta">
                {c.locations} filial · {fmt.format(c.employees)} işçi
                {c.notStarted > 0 && <> · <span className="hq-co-idle">{fmt.format(c.notStarted)} aktivləşdirməyib</span></>}
              </div>
              <div className="hq-co-row">
                {/* Out of the people who have actually started using the app, not out of everyone on
                    the payroll — the same denominator as the percentage beside it. Labelled «aktiv»
                    because the line above says «80 işçi» and an unexplained 67 underneath reads as
                    an error rather than as a different, narrower question. */}
                <div className="hq-co-big hq-num">
                  {fmt.format(c.onDuty)}<small>/ {fmt.format(c.employees - c.notStarted)} aktiv</small>
                </div>
                <div className="hq-co-pct hq-num">{c.attendancePct}%</div>
              </div>
              <div className="hq-bar">
                <i style={{ width: `${Math.min(100, c.attendancePct)}%` }} />
              </div>
              <div className="hq-co-go" aria-hidden="true">↗</div>
            </button>
          ))}
        </section>

        <section className="hq-grid hq-reveal hq-d4">
          {/* The map leads, not the chart: a director recognises their own sites in a second, and
              "our people are at these places right now" is the thing a table cannot say. */}
          <div className="hq-panel">
            <div className="hq-panel-title">
              Filiallar · hazırda iş gedən nöqtələr
            </div>
            <SiteMap sites={data.sites} accentOf={accentOf} />
          </div>

          {/* The feed is what makes the screen read as live: rows arrive while you are looking at it. */}
          <div className="hq-panel">
            <div className="hq-panel-head">
              <div className="hq-panel-title">Canlı hərəkət <small>· bu gün</small></div>
              <span className="hq-panel-count hq-num">{fmt.format(data.feed.length)}</span>
            </div>
            <input
              className="hq-feed-search"
              type="search"
              value={feedQuery}
              onChange={(e) => { setFeedQuery(e.target.value); setFeedShown(FEED_PAGE) }}
              placeholder="Ad, şirkət və ya filial üzrə axtar…"
              aria-label="Canlı hərəkətdə axtar"
            />
            <div className="hq-feed">
              {data.feed.length === 0 && (
                <p style={{ color: 'var(--fg-faint)', fontSize: 13 }}>Bu gün hələ skan olmayıb.</p>
              )}
              {data.feed.length > 0 && feed.length === 0 && (
                <p style={{ color: 'var(--fg-faint)', fontSize: 13 }}>«{feedQuery}» üzrə nəticə yoxdur.</p>
              )}
              {feed.slice(0, feedShown).map((f, i) => {
                // By id: two tenants may share a display name, and the accent is how a reader tells
                // the companies apart on this board.
                const companyIndex = data.companies.findIndex((c) => c.id === f.companyId)
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
                    {/* A field visit is an arrival like any other — it just happened at a site with
                        no poster, on GPS and a selfie. Tagged rather than hidden: without the 📍 a
                        director reading the feed cannot tell the two apart, and the whole «səyyar»
                        route was invisible on this board until now. */}
                    <span className={`hq-feed-kind ${f.kind.endsWith('in') ? 'hq-in' : 'hq-out'}`}>
                      {f.kind.startsWith('field') ? '📍 ' : ''}{f.kind.endsWith('in') ? 'GİRİŞ' : 'ÇIXIŞ'}
                    </span>
                  </div>
                )
              })}
              {feed.length > feedShown && (
                <button
                  type="button"
                  className="hq-feed-more"
                  onClick={() => setFeedShown((n) => n + FEED_PAGE)}
                >
                  daha {fmt.format(Math.min(FEED_PAGE, feed.length - feedShown))} göstər
                  <small> · {fmt.format(feed.length - feedShown)} qalıb</small>
                </button>
              )}
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
          {['QR ilə giriş', 'GPS ərazi nəzarəti', 'Üz yoxlaması', 'Oflayn skan', 'Push bildiriş', 'Maaş hesabatı']
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

      {/* The company detail comes to the board rather than the board being left behind. Resolved
          fresh each render: if the open company disappears from the payload (suspended, deleted),
          the panel closes itself instead of showing numbers for something no longer there. */}
      {selectedIndex >= 0 && (
        <CompanyDrawer
          company={data.companies[selectedIndex]}
          companyIndex={selectedIndex}
          accent={accentOf(selectedIndex)}
          sites={data.sites}
          feed={data.feed}
          onClose={() => setSelectedId(null)}
        />
      )}
    </div>
  )
}
