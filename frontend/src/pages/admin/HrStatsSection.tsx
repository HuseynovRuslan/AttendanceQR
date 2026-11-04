import { useEffect, useMemo, useRef, useState, type ComponentType, type ReactNode, type SVGProps } from 'react'
import { getHrStats, type HrStatsEmployee, type HrStatsLocation, type HrStatsReport } from '../../api/admin'
import { EmployeeLink } from '../../components/EmployeeLink'
import { matchesSearch } from '../../lib/searchFold'
import {
  IconAlarmClock, IconAlert, IconCalendarCheck, IconChart, IconCheck, IconChevronLeft, IconChevronRight,
  IconMapPin, IconSearch, IconThermometer, IconTrophy, IconUsers, IconUserX, IconX,
} from '../../components/icons'

/**
 * «İşçi Statistikaları» — the leadership section of the dashboard.
 *
 * Every number comes from /api/reports/hr-stats: the period from finished days (the same stored days the
 * tabel and the reports read), today from the live computation. The five category cards and the «Bu gün»
 * strip only choose what the ranking panel lists — the panel is where the people are, with search, period
 * and branch. A figure leadership cannot click into is a figure they have to take on trust. A percentage
 * with nothing to divide by is «—», never a made-up number.
 */

type Preset = 'month' | 'lastMonth' | '30' | 'custom'
type Tone = 'teal' | 'amber' | 'clay' | 'blue' | 'purple' | 'slate'
type Icon = ComponentType<SVGProps<SVGSVGElement>>

const fmtDay = (d: Date) => new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Baku' }).format(d)
const shiftDays = (iso: string, n: number) => {
  const d = new Date(`${iso}T12:00:00Z`)
  d.setUTCDate(d.getUTCDate() + n)
  return d.toISOString().slice(0, 10)
}
const showDate = (iso: string) => `${iso.slice(8, 10)}.${iso.slice(5, 7)}.${iso.slice(0, 4)}`

function presetRange(p: Preset, custom: { from: string; to: string }) {
  const today = fmtDay(new Date())
  const first = `${today.slice(0, 8)}01`
  if (p === 'month') return { from: first, to: today }
  if (p === 'lastMonth') {
    const lastOfPrev = shiftDays(first, -1)
    return { from: `${lastOfPrev.slice(0, 8)}01`, to: lastOfPrev }
  }
  if (p === '30') return { from: shiftDays(today, -29), to: today }
  return custom
}

const PRESETS: [Preset, string][] = [['month', 'Bu ay'], ['lastMonth', 'Keçən ay'], ['30', 'Son 30 gün'], ['custom', 'Aralıq']]
const TOP = 10
const PAGE = 20

const pct = (v: number | null | undefined) => {
  if (v == null) return '—'
  const r = Math.round(v * 10) / 10
  return `${Number.isInteger(r) ? r : r.toFixed(1)}%`
}
const minutes = (m: number) => (m >= 60 ? `${Math.floor(m / 60)} saat ${m % 60} dəq` : `${m} dəq`)
const unit = (n: ReactNode, u: string) => <>{n}<small>{u}</small></>
const byName = (a: HrStatsEmployee, b: HrStatsEmployee) => a.name.localeCompare(b.name, 'az')
const smooth = (): ScrollBehavior => (window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 'auto' : 'smooth')

const TODAY: Record<string, { label: string; tone: Tone }> = {
  present: { label: 'İşdə', tone: 'teal' },
  late: { label: 'Gecikib', tone: 'amber' },
  permission: { label: 'İcazəli', tone: 'blue' },
  sick: { label: 'Xəstəlik', tone: 'purple' },
  leave: { label: 'Məzuniyyət', tone: 'slate' },
  absent: { label: 'Hələ gəlməyib', tone: 'clay' },
  dayOff: { label: 'İstirahət', tone: 'slate' },
}

type Metric = { label: string; num: (e: HrStatsEmployee) => number; show: (e: HrStatsEmployee) => ReactNode }
type ListDef = {
  title: string
  sub: string
  tone: Tone
  Icon: Icon
  keep: (e: HrStatsEmployee) => boolean
  order: (a: HrStatsEmployee, b: HrStatsEmployee) => number
  /** The ranked figure and its bar: «max» is scaled to the list's leader, «percent» already runs 0–100.
   *  A list without one is a live roster — the row shows today's status instead. */
  primary?: Metric & { bar: 'max' | 'percent' }
  secondary?: Metric
  empty: string
}

function lists(minWorked: number): Record<string, ListDef> {
  const live = (title: string, sub: string, tone: Tone, Icon: Icon, keep: ListDef['keep'], empty: string): ListDef =>
    ({ title, sub, tone, Icon, keep, order: byName, empty })
  return {
    disciplined: {
      title: 'Ən intizamlı işçilər', tone: 'teal', Icon: IconTrophy,
      sub: `Vaxtında gəlmə faizinə görə · ən azı ${minWorked} iş günü olanlar · faiz bərabərdirsə, çox işləyən öndədir`,
      keep: (e) => e.onTimePercent != null && e.workedDays >= minWorked,
      order: (a, b) => (b.onTimePercent ?? 0) - (a.onTimePercent ?? 0) || b.workedDays - a.workedDays,
      primary: { label: 'Vaxtında gəlmə', num: (e) => e.onTimePercent ?? -1, show: (e) => pct(e.onTimePercent), bar: 'percent' },
      secondary: { label: 'İşlədiyi gün', num: (e) => e.workedDays, show: (e) => `${e.workedDays} gün` },
      empty: 'Bu dövrdə kifayət qədər iş günü olan işçi yoxdur.',
    },
    late: {
      title: 'Ən çox gecikənlər', tone: 'amber', Icon: IconAlarmClock,
      sub: 'Hər kəs öz iş saatına görə · 4 saatdan uzun «gecikmə» (səhv növbə) sayılmır',
      keep: (e) => e.lateDays > 0,
      order: (a, b) => b.lateDays - a.lateDays || b.lateMinutes - a.lateMinutes,
      primary: { label: 'Gecikmə sayı', num: (e) => e.lateDays, show: (e) => unit(e.lateDays, 'dəfə'), bar: 'max' },
      secondary: { label: 'Ümumi gecikmə', num: (e) => e.lateMinutes, show: (e) => minutes(e.lateMinutes) },
      empty: 'Bu dövrdə gecikmə qeydə alınmayıb.',
    },
    absent: {
      title: 'Ən çox işə gəlməyənlər', tone: 'clay', Icon: IconUserX,
      sub: 'Səbəbsiz gəlinməyən iş günləri (Qayıb) · icazə, məzuniyyət və istirahət günləri sayılmır',
      keep: (e) => e.absentDays > 0,
      order: (a, b) => b.absentDays - a.absentDays,
      primary: { label: 'Gəlmədiyi gün', num: (e) => e.absentDays, show: (e) => unit(e.absentDays, 'gün'), bar: 'max' },
      secondary: { label: 'Davamiyyət', num: (e) => e.attendancePercent ?? -1, show: (e) => pct(e.attendancePercent) },
      empty: 'Bu dövrdə səbəbsiz gəlməmə yoxdur.',
    },
    permission: {
      title: 'Ən çox icazə alanlar', tone: 'blue', Icon: IconCalendarCheck,
      sub: 'Qeydə alınmış icazələr · ümumi müddətə görə',
      keep: (e) => e.permissionCount > 0,
      order: (a, b) => b.permissionDays - a.permissionDays || b.permissionCount - a.permissionCount,
      primary: { label: 'Ümumi müddət', num: (e) => e.permissionDays, show: (e) => unit(e.permissionDays, 'gün'), bar: 'max' },
      secondary: { label: 'İcazə sayı', num: (e) => e.permissionCount, show: (e) => `${e.permissionCount} dəfə` },
      empty: 'Bu dövrdə icazə qeydi yoxdur.',
    },
    sick: {
      title: 'Ən çox xəstəlik məzuniyyəti olanlar', tone: 'purple', Icon: IconThermometer,
      sub: 'Yalnız qeydə alınmış xəstəlik məzuniyyətləri · gün sayına görə',
      keep: (e) => e.sickCount > 0,
      order: (a, b) => b.sickDays - a.sickDays || b.sickCount - a.sickCount,
      primary: { label: 'Gün sayı', num: (e) => e.sickDays, show: (e) => unit(e.sickDays, 'gün'), bar: 'max' },
      secondary: { label: 'Hal sayı', num: (e) => e.sickCount, show: (e) => `${e.sickCount} hal` },
      empty: 'Bu dövrdə xəstəlik məzuniyyəti qeydə alınmayıb.',
    },
    attendance: {
      title: 'Davamiyyət faizi', tone: 'slate', Icon: IconChart,
      sub: 'Ən aşağıdan · gəldiyi gün ÷ (gəldiyi + səbəbsiz gəlmədiyi gün)',
      keep: (e) => e.attendancePercent != null,
      order: (a, b) => (a.attendancePercent ?? 0) - (b.attendancePercent ?? 0) || b.absentDays - a.absentDays,
      primary: { label: 'Davamiyyət', num: (e) => e.attendancePercent ?? -1, show: (e) => pct(e.attendancePercent), bar: 'percent' },
      secondary: { label: 'Gəldiyi · Qayıb', num: (e) => e.absentDays, show: (e) => `${e.workedDays} gün · ${e.absentDays} qayıb` },
      empty: 'Bu dövrdə hesablanacaq iş günü yoxdur.',
    },
    active: live('Aktiv işçilər', 'Bütün aktiv işçilər, əlifba sırası ilə · yanında bu günkü vəziyyəti', 'slate', IconUsers, () => true, 'Aktiv işçi yoxdur.'),
    presentToday: live('Bu gün işdə olanlar', 'Canlı · gecikənlər də daxildir', 'teal', IconCheck,
      (e) => e.today === 'present' || e.today === 'late', 'Bu gün hələ heç kim giriş etməyib.'),
    lateToday: live('Bu gün gecikənlər', 'Canlı · hər kəs öz iş saatına görə', 'amber', IconAlarmClock,
      (e) => e.today === 'late', 'Bu gün gecikən yoxdur.'),
    permissionToday: live('Bu gün icazəlilər', 'Canlı', 'blue', IconCalendarCheck,
      (e) => e.today === 'permission', 'Bu gün icazəli işçi yoxdur.'),
    sickToday: live('Xəstəlik məzuniyyətində olanlar', 'Bu gün', 'purple', IconThermometer,
      (e) => e.today === 'sick', 'Bu gün xəstəlik məzuniyyətində olan yoxdur.'),
  }
}

const CATEGORIES = ['disciplined', 'late', 'absent', 'permission', 'sick'] as const
type SortKey = 'name' | 'loc' | 'p' | 's'

export function HrStatsSection() {
  const [preset, setPreset] = useState<Preset>('month')
  const [custom, setCustom] = useState(() => presetRange('month', { from: '', to: '' }))
  const [locationId, setLocationId] = useState('')
  const [report, setReport] = useState<HrStatsReport | null>(null)
  const [allLocations, setAllLocations] = useState<HrStatsLocation[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [reload, setReload] = useState(0)
  const [selected, setSelected] = useState('disciplined')
  const [q, setQ] = useState('')
  const [sort, setSort] = useState<{ key: SortKey; desc: boolean } | null>(null)
  const [expanded, setExpanded] = useState(false)
  const [page, setPage] = useState(0)
  const panelRef = useRef<HTMLDivElement>(null)

  const range = presetRange(preset, custom)

  useEffect(() => {
    let alive = true
    setLoading(true)
    setError(null)
    getHrStats(range.from, range.to, locationId || undefined)
      .then(({ status, data }) => {
        if (!alive) return
        if (status === 200 && data && 'employees' in data) {
          setReport(data)
          if (!locationId) setAllLocations(data.locations)
          return
        }
        // No figures from the previous filter under the new one — they would read as the answer to it.
        setReport(null)
        setError(status === 403 ? 'Bu statistikaya baxmaq icazəniz yoxdur.'
          : status === 400 ? 'Tarix aralığı düzgün deyil: başlanğıc sondan əvvəl olmalı, aralıq 366 gündən uzun olmamalıdır.'
          : 'Statistika yüklənmədi.')
      })
      .catch(() => { if (alive) { setReport(null); setError('Serverə qoşulmaq mümkün olmadı.') } })
      .finally(() => { if (alive) setLoading(false) })
    return () => { alive = false }
  }, [range.from, range.to, locationId, reload])

  const countedDays = report ? Math.max(0, (Date.parse(report.countedTo) - Date.parse(report.from)) / 86_400_000 + 1) : 0
  const noDays = report != null && report.countedTo < report.from
  const defs = useMemo(() => lists(countedDays >= 10 ? 5 : 1), [countedDays])
  const emps = useMemo(() => report?.employees ?? [], [report])
  const def = defs[selected]

  const cards = useMemo(() => {
    const sum = (f: (e: HrStatsEmployee) => number) => emps.reduce((s, e) => s + f(e), 0)
    const people = (key: string) => emps.filter(defs[key].keep).length
    const onTime = sum((e) => e.onTimeDays)
    const late = sum((e) => e.lateDays)
    const perfect = emps.filter((e) => defs.disciplined.keep(e) && (e.onTimePercent ?? 0) >= 100).length
    const [lateN, absentN, permN, sickN] = [people('late'), people('absent'), people('permission'), people('sick')]
    const out: Record<(typeof CATEGORIES)[number], { value: string; unit?: string; foot: string }> = {
      disciplined: onTime + late > 0
        ? { value: pct((onTime / (onTime + late)) * 100), unit: 'vaxtında', foot: `${perfect} işçi heç gecikməyib` }
        : { value: '—', foot: 'Hesablanacaq iş günü yoxdur' },
      late: { value: String(late), unit: 'dəfə', foot: lateN ? `${lateN} işçi` : 'Gecikən olmayıb' },
      absent: { value: String(sum((e) => e.absentDays)), unit: 'gün', foot: absentN ? `${absentN} işçi` : 'Səbəbsiz gəlməmə yoxdur' },
      permission: {
        value: String(sum((e) => e.permissionCount)), unit: 'icazə',
        foot: permN ? `${permN} işçi · ${sum((e) => e.permissionDays)} gün` : 'İcazə qeydi yoxdur',
      },
      sick: {
        value: String(sum((e) => e.sickDays)), unit: 'gün',
        foot: sickN ? `${sickN} işçi · ${sum((e) => e.sickCount)} hal` : 'Qeyd yoxdur',
      },
    }
    return out
  }, [emps, defs])

  const full = useMemo(() => {
    const kept = emps.filter(def.keep).sort(def.order)
    if (!sort) return kept
    const val = (e: HrStatsEmployee): number | string =>
      sort.key === 'name' ? e.name : sort.key === 'loc' ? e.locationName
        : sort.key === 'p' ? def.primary?.num(e) ?? 0 : def.secondary?.num(e) ?? 0
    return kept.sort((a, b) => {
      const x = val(a), y = val(b)
      const c = typeof x === 'number' && typeof y === 'number' ? x - y : String(x).localeCompare(String(y), 'az')
      return (sort.desc ? -c : c) || def.order(a, b)
    })
  }, [emps, def, sort])

  // The rank is the place in the whole list; a search only narrows what is shown, so a name found by
  // search still shows where that person stands.
  const needle = q.trim()
  const ranked = full.map((e, i) => ({ e, rank: i + 1 }))
  const matched = needle ? ranked.filter(({ e }) => matchesSearch(needle, e.name, e.locationName, e.position)) : ranked
  const paging = expanded || needle !== ''
  const pages = Math.max(1, Math.ceil(matched.length / PAGE))
  const pg = Math.min(page, pages - 1)
  const shown = paging ? matched.slice(pg * PAGE, (pg + 1) * PAGE) : matched.slice(0, TOP)
  const leader = def.primary?.bar === 'max' ? full.reduce((m, e) => Math.max(m, def.primary!.num(e)), 0) : 100

  const locName = allLocations.find((l) => l.locationId === locationId)?.name
  const stale = loading && report ? ' hrs-stale' : ''

  const choose = (key: string) => {
    setSelected(key)
    setSort(null)
    setExpanded(false)
    setPage(0)
    // On a phone the panel sits below five cards; bring it up, or the tap looks like it did nothing.
    const el = panelRef.current
    if (el && el.getBoundingClientRect().top > window.innerHeight * 0.66) el.scrollIntoView({ block: 'start', behavior: smooth() })
  }
  const turn = (n: number) => {
    setPage(n)
    const el = panelRef.current
    if (el && el.getBoundingClientRect().top < 64) el.scrollIntoView({ block: 'start', behavior: smooth() })
  }
  const pickPreset = (p: Preset) => {
    if (p === 'custom') setCustom(range)
    setPreset(p)
    setPage(0)
  }
  const pickLocation = (id: string) => {
    setLocationId(id)
    setPage(0)
  }
  const sortBy = (key: SortKey) => {
    setSort((s) => (s?.key === key ? { key, desc: !s.desc } : { key, desc: key === 'p' || key === 's' }))
    setPage(0)
  }
  const head = (key: SortKey, label: string, end?: boolean) => (
    <button type="button" className={`hrs-sort${sort?.key === key ? ' on' : ''}${end ? ' end' : ''}`} onClick={() => sortBy(key)}>
      {label}
      {sort?.key === key && <span aria-hidden="true">{sort.desc ? '↓' : '↑'}</span>}
    </button>
  )

  const kpis: { key: string; label: string; value: string; tone: Tone }[] = report
    ? [
        { key: 'active', label: 'Aktiv işçi', value: String(report.activeEmployees), tone: 'slate' },
        { key: 'presentToday', label: 'Bu gün işdə', value: String(report.presentToday), tone: 'teal' },
        { key: 'lateToday', label: 'Bu gün gecikən', value: String(report.lateToday), tone: 'amber' },
        { key: 'permissionToday', label: 'Bu gün icazəli', value: String(report.permissionToday), tone: 'blue' },
        { key: 'sickToday', label: 'Xəstəlik məzuniyyətində', value: String(report.sickToday), tone: 'purple' },
        { key: 'attendance', label: 'Davamiyyət faizi · dövr', value: pct(report.attendancePercent), tone: 'slate' },
      ]
    : []

  return (
    <section className="hrs" aria-labelledby="hrs-title">
      <div className="hrs-head">
        <div>
          <h2 id="hrs-title" className="hrs-title">İşçi Statistikaları</h2>
          <p className="hrs-sub">
            {report ? (
              <>
                {showDate(report.from)} – {showDate(report.to)} ·{' '}
                {noDays ? 'tamamlanmış gün hələ yoxdur' : `${showDate(report.countedTo)}-dək tamamlanmış günlər`} · «Bu gün» göstəriciləri canlıdır
              </>
            ) : 'Rəhbərlik üçün davamiyyət göstəriciləri'}
          </p>
        </div>
        {locationId && locName && (
          <span className="hrs-scope">
            <IconMapPin aria-hidden="true" />
            {locName}
            <button type="button" onClick={() => pickLocation('')} aria-label="Ərazi filtrini sil"><IconX /></button>
          </span>
        )}
      </div>

      {report ? (
        <div className={`hrs-strip${stale}`} role="group" aria-label="Ümumi göstəricilər">
          {kpis.map((k) => (
            <button key={k.key} type="button" className={`hrs-kpi hrs-t-${k.tone}`} aria-pressed={selected === k.key} onClick={() => choose(k.key)}>
              <span className="hrs-kpi-v">{k.value}</span>
              <span className="hrs-kpi-l"><i className="hrs-dot" />{k.label}</span>
            </button>
          ))}
        </div>
      ) : loading && <StripSkeleton />}

      {report ? (
        <div className={`hrs-cats${stale}`} role="group" aria-label="Kateqoriyalar">
          {CATEGORIES.map((key) => {
            const d = defs[key]
            const c = cards[key]
            return (
              <button key={key} type="button" className={`hrs-cat hrs-t-${d.tone}`} aria-pressed={selected === key} onClick={() => choose(key)}>
                <span className="hrs-ic"><d.Icon /></span>
                <span className="hrs-cat-t">{d.title}</span>
                <span className="hrs-cat-m">
                  <span className="hrs-cat-v">{c.value}{c.unit && <small>{c.unit}</small>}</span>
                  <span className="hrs-cat-f">{c.foot}</span>
                </span>
              </button>
            )
          })}
        </div>
      ) : loading && <CardsSkeleton />}

      <div ref={panelRef} className={`hrs-box hrs-panel hrs-t-${def.tone}`} aria-busy={loading}>
        <div className="hrs-ph">
          <span className="hrs-ic"><def.Icon /></span>
          <div className="hrs-ph-tx">
            <h3 className="hrs-ph-t">{def.title}</h3>
            <p className="hrs-ph-s">{def.sub}</p>
          </div>
          {report && (
            <span className="hrs-count">
              {loading ? 'Yenilənir…' : needle ? `${matched.length} / ${full.length} işçi` : `${full.length} işçi`}
            </span>
          )}
        </div>

        <div className="hrs-tools">
          <label className="hrs-search">
            <IconSearch aria-hidden="true" />
            <input className="inp" type="search" value={q} onChange={(e) => { setQ(e.target.value); setPage(0) }}
              placeholder="Ad, ərazi və ya vəzifə axtar…" aria-label="İşçi axtar" />
          </label>
          <div className="hrs-seg" role="group" aria-label="Dövr">
            {PRESETS.map(([p, l]) => (
              <button key={p} type="button" aria-pressed={preset === p} onClick={() => pickPreset(p)}>{l}</button>
            ))}
          </div>
          <select className="inp hrs-locsel" value={locationId} onChange={(e) => pickLocation(e.target.value)} aria-label="Ərazi">
            <option value="">Bütün ərazilər</option>
            {allLocations.map((l) => <option key={l.locationId} value={l.locationId}>{l.name}</option>)}
          </select>
        </div>

        {preset === 'custom' && (
          <div className="hrs-range">
            <input className="inp" type="date" value={custom.from} max={custom.to} aria-label="Başlanğıc tarixi"
              onChange={(e) => e.target.value && setCustom((c) => ({ ...c, from: e.target.value }))} />
            <span aria-hidden="true">—</span>
            <input className="inp" type="date" value={custom.to} min={custom.from} aria-label="Son tarix"
              onChange={(e) => e.target.value && setCustom((c) => ({ ...c, to: e.target.value }))} />
          </div>
        )}

        {!report ? (
          loading ? <RowsSkeleton /> : error && (
            <Empty Icon={IconAlert} title={error}
              action={<button type="button" className="btn btn-sm" onClick={() => setReload((n) => n + 1)}>Yenidən yüklə</button>} />
          )
        ) : full.length === 0 ? (
          <Empty Icon={def.Icon} title={def.empty} text={noDays && def.primary ? 'Bu dövr üçün tamamlanmış gün hələ yoxdur — göstəricilər hər günün sonunda hesablanır.' : undefined} />
        ) : matched.length === 0 ? (
          <Empty Icon={IconSearch} title={`«${needle}» tapılmadı`} text="Bu siyahıda belə ad, ərazi və ya vəzifə yoxdur."
            action={<button type="button" className="btn btn-sm" onClick={() => { setQ(''); setPage(0) }}>Axtarışı təmizlə</button>} />
        ) : (
          <>
            <div className={`hrs-rows${def.primary ? '' : ' plain'}${stale}`}>
              <div className="hrs-lh">
                <span>#</span>
                {head('name', 'İşçi')}
                {head('loc', 'Ərazi')}
                {def.primary ? head('p', def.primary.label) : <span>Bu gün</span>}
                {def.secondary && head('s', def.secondary.label, true)}
              </div>
              <ol className="hrs-list">
                {shown.map(({ e, rank }) => <Row key={e.employeeId} e={e} rank={rank} def={def} leader={leader} podium={!sort && rank <= 3} />)}
              </ol>
            </div>
            <div className="hrs-pf">
              <span className="hrs-pf-n">
                {paging
                  ? <><b>{pg * PAGE + 1}–{Math.min((pg + 1) * PAGE, matched.length)}</b> / {matched.length} işçi</>
                  : full.length > TOP ? <><b>TOP {TOP}</b> · {full.length} işçidən</> : <>{full.length} işçi</>}
              </span>
              <div className="hrs-pager">
                {paging && pages > 1 && (
                  <>
                    <button type="button" className="btn btn-sm" disabled={pg === 0} onClick={() => turn(pg - 1)} aria-label="Əvvəlki səhifə"><IconChevronLeft /></button>
                    <span className="hrs-pg">{pg + 1} / {pages}</span>
                    <button type="button" className="btn btn-sm" disabled={pg >= pages - 1} onClick={() => turn(pg + 1)} aria-label="Növbəti səhifə"><IconChevronRight /></button>
                  </>
                )}
                {!paging && full.length > TOP && (
                  <button type="button" className="btn btn-sm" onClick={() => { setExpanded(true); setPage(0) }}>
                    Hamısına bax ({full.length}) <IconChevronRight />
                  </button>
                )}
                {expanded && !needle && (
                  <button type="button" className="btn btn-sm btn-ghost" onClick={() => { setExpanded(false); setPage(0) }}>Yalnız TOP {TOP}</button>
                )}
              </div>
            </div>
          </>
        )}
      </div>

      {report && <LocationsCard rows={report.locations} filtered={!!locationId} onPick={pickLocation} stale={stale} />}
    </section>
  )
}

function Row({ e, rank, def, leader, podium }: { e: HrStatsEmployee; rank: number; def: ListDef; leader: number; podium: boolean }) {
  const p = def.primary
  const v = p ? p.num(e) : 0
  const w = !p || v <= 0 ? 0 : Math.max(2, Math.min(100, p.bar === 'percent' ? v : leader > 0 ? (v / leader) * 100 : 0))
  const st = e.today ? TODAY[e.today] : undefined
  return (
    <li className="hrs-row">
      <span className={`hrs-rank${p && podium ? ' top' : ''}`}>{rank}</span>
      <div className="hrs-who">
        <div className="hrs-name"><EmployeeLink id={e.employeeId} name={e.name} /></div>
        <div className="hrs-meta">
          <span className="hrs-m-loc">{e.locationName}</span>
          {e.position && <span className="hrs-m-pos">{e.position}</span>}
        </div>
      </div>
      <div className="hrs-lc">{e.locationName}</div>
      {p ? (
        <>
          <div className="hrs-val">{p.show(e)}</div>
          {def.secondary && <div className="hrs-sec">{def.secondary.show(e)}</div>}
          <div className="hrs-bar" aria-hidden="true"><i style={{ width: `${w}%` }} /></div>
        </>
      ) : (
        <div className="hrs-val">
          {st ? <span className={`hrs-chip hrs-t-${st.tone}`}>{st.label}</span> : <span className="hrs-none">—</span>}
        </div>
      )}
    </li>
  )
}

function LocationsCard({ rows, filtered, onPick, stale }: { rows: HrStatsLocation[]; filtered: boolean; onPick: (id: string) => void; stale: string }) {
  if (rows.length === 0) return null
  const sorted = [...rows].sort((a, b) => (b.attendancePercent ?? -1) - (a.attendancePercent ?? -1))
  return (
    <div className={`hrs-box hrs-t-teal${stale}`}>
      <div className="hrs-ph">
        <span className="hrs-ic"><IconMapPin /></span>
        <div className="hrs-ph-tx">
          <h3 className="hrs-ph-t">Ərazilər üzrə müqayisə</h3>
          <p className="hrs-ph-s">
            Davamiyyət faizinə görə, yuxarıdan aşağı{filtered ? '' : ' · ərazinin adına toxunsanız, bütün bölmə o əraziyə görə göstərilir'}
          </p>
        </div>
      </div>
      <div className="hrs-tbl">
        <table>
          <thead>
            <tr>
              <th>Ərazi</th><th className="num">İşçi</th><th>Davamiyyət</th><th className="num">Gecikmə</th><th className="num">İcazə</th><th className="num">Xəstəlik günü</th>
            </tr>
          </thead>
          <tbody>
            {sorted.map((l) => (
              <tr key={l.locationId}>
                <td>{filtered ? l.name : <button type="button" className="hrs-lnk" onClick={() => onPick(l.locationId)}>{l.name}</button>}</td>
                <td className="num">{l.employees}</td>
                <td>
                  <div className="hrs-att">
                    <div className="hrs-bar" aria-hidden="true"><i style={{ width: `${Math.max(0, Math.min(100, l.attendancePercent ?? 0))}%` }} /></div>
                    <b>{pct(l.attendancePercent)}</b>
                  </div>
                </td>
                <td className="num">{l.lateDays}</td>
                <td className="num">{l.permissionCount}</td>
                <td className="num">{l.sickDays}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  )
}

function Empty({ Icon, title, text, action }: { Icon: Icon; title: string; text?: string; action?: ReactNode }) {
  return (
    <div className="hrs-empty">
      <span className="hrs-empty-ic"><Icon /></span>
      <div className="hrs-empty-t">{title}</div>
      {text && <div className="hrs-empty-s">{text}</div>}
      {action}
    </div>
  )
}

const Sk = ({ w, h = 12 }: { w: number | string; h?: number }) => <i className="hrs-sk" style={{ width: w, height: h }} />

function StripSkeleton() {
  return (
    <div className="hrs-strip" aria-hidden="true">
      {Array.from({ length: 6 }, (_, i) => (
        <div key={i} className="hrs-kpi is-sk"><Sk w="38%" h={24} /><Sk w="70%" /></div>
      ))}
    </div>
  )
}

function CardsSkeleton() {
  return (
    <div className="hrs-cats" aria-hidden="true">
      {Array.from({ length: 5 }, (_, i) => (
        <div key={i} className="hrs-cat is-sk">
          <Sk w={36} h={36} />
          <Sk w="80%" h={14} />
          <span className="hrs-cat-m"><Sk w="45%" h={26} /><Sk w="60%" /></span>
        </div>
      ))}
    </div>
  )
}

function RowsSkeleton() {
  return (
    <div className="hrs-rows" aria-hidden="true">
      {Array.from({ length: 8 }, (_, i) => (
        <div key={i} className="hrs-skrow">
          <Sk w={28} h={28} />
          <div><Sk w="42%" h={14} /><Sk w="26%" h={11} /></div>
          <Sk w={84} h={14} />
        </div>
      ))}
    </div>
  )
}
