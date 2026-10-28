import { useEffect, useMemo, useState } from 'react'
import { getHrStats, type HrStatsEmployee, type HrStatsLocation, type HrStatsReport } from '../../api/admin'
import { EmployeeLink } from '../../components/EmployeeLink'
import { matchesSearch } from '../../lib/searchFold'
import { IconX } from '../../components/icons'

/**
 * «İşçi Statistikaları» — the leadership section of the dashboard.
 *
 * Every number comes from /api/reports/hr-stats: the period from finished days (the same stored days the
 * tabel and the reports read), today from the live computation. Every card and every ranking opens the
 * people behind it, with search and a branch filter — a figure leadership cannot click into is a figure
 * they have to take on trust. A percentage with nothing to divide by is «—», never a made-up number.
 */

type Preset = 'month' | 'lastMonth' | '30' | 'custom'

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

const pct = (v: number | null | undefined) => (v == null ? '—' : `${v.toFixed(1)}%`)
const minutes = (m: number) => (m >= 60 ? `${Math.floor(m / 60)} saat ${m % 60} dəq` : `${m} dəq`)
const TODAY_LABEL: Record<string, string> = {
  present: 'İşdə', late: 'Gecikib', permission: 'İcazəli', sick: 'Xəstəlik', leave: 'Məzuniyyət',
  absent: 'Hələ gəlməyib', dayOff: 'İstirahət',
}
const todayText = (e: HrStatsEmployee) => (e.today ? TODAY_LABEL[e.today] ?? '—' : '—')

type Col = { h: string; v: (e: HrStatsEmployee) => string | number; sort: (e: HrStatsEmployee) => number | string }
type ListDef = {
  title: string
  sub: string
  keep: (e: HrStatsEmployee) => boolean
  order: (a: HrStatsEmployee, b: HrStatsEmployee) => number
  cols: Col[]
  /** The figure shown in the TOP-10 preview. */
  head: (e: HrStatsEmployee) => string
}

const todayCol: Col = { h: 'Bu gün', v: todayText, sort: todayText }

function lists(minWorked: number): Record<string, ListDef> {
  return {
    late: {
      title: 'Ən çox gecikən işçilər', sub: 'Hər kəs öz iş saatına görə · 4 saatdan uzun «gecikmə» (səhv növbə) sayılmır', keep: (e) => e.lateDays > 0,
      order: (a, b) => b.lateDays - a.lateDays || b.lateMinutes - a.lateMinutes,
      cols: [
        { h: 'Gecikmə sayı', v: (e) => e.lateDays, sort: (e) => e.lateDays },
        { h: 'Ümumi gecikmə', v: (e) => minutes(e.lateMinutes), sort: (e) => e.lateMinutes },
      ],
      head: (e) => `${e.lateDays} dəfə · ${minutes(e.lateMinutes)}`,
    },
    permission: {
      title: 'Ən çox icazə alanlar', sub: 'İcazə qeydlərinə görə', keep: (e) => e.permissionCount > 0,
      order: (a, b) => b.permissionDays - a.permissionDays || b.permissionCount - a.permissionCount,
      cols: [
        { h: 'İcazə sayı', v: (e) => e.permissionCount, sort: (e) => e.permissionCount },
        { h: 'Ümumi müddət', v: (e) => `${e.permissionDays} gün`, sort: (e) => e.permissionDays },
      ],
      head: (e) => `${e.permissionCount} dəfə · ${e.permissionDays} gün`,
    },
    sick: {
      title: 'Ən çox xəstəlik məzuniyyəti olanlar', sub: 'Yalnız qeydə alınmış məzuniyyətlər', keep: (e) => e.sickCount > 0,
      order: (a, b) => b.sickDays - a.sickDays || b.sickCount - a.sickCount,
      cols: [
        { h: 'Hal sayı', v: (e) => e.sickCount, sort: (e) => e.sickCount },
        { h: 'Gün sayı', v: (e) => e.sickDays, sort: (e) => e.sickDays },
      ],
      head: (e) => `${e.sickCount} hal · ${e.sickDays} gün`,
    },
    absent: {
      title: 'Ən çox işə gəlməyənlər', sub: 'Səbəbsiz gəlməmə (Qayıb)', keep: (e) => e.absentDays > 0,
      order: (a, b) => b.absentDays - a.absentDays,
      cols: [{ h: 'Səbəbsiz gəlməmə', v: (e) => e.absentDays, sort: (e) => e.absentDays }],
      head: (e) => `${e.absentDays} gün`,
    },
    disciplined: {
      title: 'Ən intizamlı işçilər', sub: `Ən azı ${minWorked} iş günü olanlar`,
      keep: (e) => e.onTimePercent != null && e.workedDays >= minWorked,
      order: (a, b) => (b.onTimePercent ?? 0) - (a.onTimePercent ?? 0) || b.workedDays - a.workedDays,
      cols: [
        { h: 'Vaxtında gəlmə', v: (e) => pct(e.onTimePercent), sort: (e) => e.onTimePercent ?? -1 },
        { h: 'İşlədiyi gün', v: (e) => e.workedDays, sort: (e) => e.workedDays },
      ],
      head: (e) => `${pct(e.onTimePercent)} · ${e.workedDays} gün`,
    },
    active: {
      title: 'Aktiv işçilər', sub: 'Bütün aktiv işçilər', keep: () => true,
      order: (a, b) => a.name.localeCompare(b.name, 'az'),
      cols: [{ h: 'Vəzifə', v: (e) => e.position ?? '—', sort: (e) => e.position ?? '' }, todayCol],
      head: todayText,
    },
    presentToday: {
      title: 'Bu gün işdə olanlar', sub: 'Canlı', keep: (e) => e.today === 'present' || e.today === 'late',
      order: (a, b) => a.name.localeCompare(b.name, 'az'), cols: [todayCol], head: todayText,
    },
    lateToday: {
      title: 'Bu gün gecikənlər', sub: 'Canlı, hər kəs öz iş saatına görə', keep: (e) => e.today === 'late',
      order: (a, b) => a.name.localeCompare(b.name, 'az'), cols: [todayCol], head: todayText,
    },
    permissionToday: {
      title: 'Bu gün icazəlilər', sub: 'Canlı', keep: (e) => e.today === 'permission',
      order: (a, b) => a.name.localeCompare(b.name, 'az'), cols: [todayCol], head: todayText,
    },
    sickToday: {
      title: 'Xəstəlik məzuniyyətində olanlar', sub: 'Bu gün', keep: (e) => e.today === 'sick',
      order: (a, b) => a.name.localeCompare(b.name, 'az'), cols: [todayCol], head: todayText,
    },
    attendance: {
      title: 'Davamiyyət faizi', sub: 'Ən aşağıdan — gəldiyi gün ÷ (gəldiyi + səbəbsiz gəlmədiyi gün)',
      keep: (e) => e.attendancePercent != null,
      order: (a, b) => (a.attendancePercent ?? 0) - (b.attendancePercent ?? 0) || b.absentDays - a.absentDays,
      cols: [
        { h: 'Davamiyyət', v: (e) => pct(e.attendancePercent), sort: (e) => e.attendancePercent ?? -1 },
        { h: 'Gəldiyi gün', v: (e) => e.workedDays, sort: (e) => e.workedDays },
        { h: 'Qayıb', v: (e) => e.absentDays, sort: (e) => e.absentDays },
      ],
      head: (e) => pct(e.attendancePercent),
    },
  }
}

export function HrStatsSection() {
  const [preset, setPreset] = useState<Preset>('month')
  const [custom, setCustom] = useState(() => presetRange('month', { from: '', to: '' }))
  const [locationId, setLocationId] = useState('')
  const [report, setReport] = useState<HrStatsReport | null>(null)
  const [allLocations, setAllLocations] = useState<HrStatsLocation[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [open, setOpen] = useState<string | null>(null)

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
        } else setError(status === 403 ? 'Bu statistikaya baxmaq icazəniz yoxdur.' : 'Statistika yüklənmədi.')
      })
      .catch(() => { if (alive) setError('Serverə qoşulmaq mümkün olmadı.') })
      .finally(() => { if (alive) setLoading(false) })
    return () => { alive = false }
  }, [range.from, range.to, locationId])

  const countedDays = report ? Math.max(0, (Date.parse(report.countedTo) - Date.parse(report.from)) / 86_400_000 + 1) : 0
  const defs = useMemo(() => lists(countedDays >= 10 ? 5 : 1), [countedDays])
  const emps = report?.employees ?? []
  const listOf = (key: string) => emps.filter(defs[key].keep).sort(defs[key].order)

  const kpis: { key: string; label: string; value: string }[] = report
    ? [
        { key: 'active', label: 'Ümumi aktiv işçi', value: String(report.activeEmployees) },
        { key: 'presentToday', label: 'Bu gün işdə', value: String(report.presentToday) },
        { key: 'lateToday', label: 'Bu gün gecikənlər', value: String(report.lateToday) },
        { key: 'permissionToday', label: 'Bu gün icazəli', value: String(report.permissionToday) },
        { key: 'sickToday', label: 'Xəstəlik məzuniyyətində', value: String(report.sickToday) },
        { key: 'attendance', label: 'Davamiyyət faizi', value: pct(report.attendancePercent) },
      ]
    : []

  return (
    <section className="card lux-panel lux-rise lux-d5" style={{ marginTop: 16 }}>
      <div className="lux-panel-h" style={{ flexWrap: 'wrap', gap: 10 }}>
        <span>İşçi Statistikaları</span>
        <div className="lux-spanpick" style={{ flexWrap: 'wrap' }}>
          {([['month', 'Bu ay'], ['lastMonth', 'Keçən ay'], ['30', 'Son 30 gün'], ['custom', 'Tarix aralığı']] as const).map(([p, l]) => (
            <button key={p} className={`lux-chip${preset === p ? ' active' : ''}`} onClick={() => { if (p === 'custom') setCustom(range); setPreset(p) }}>{l}</button>
          ))}
          <select className="inp" style={{ maxWidth: 220 }} value={locationId} onChange={(e) => setLocationId(e.target.value)} aria-label="Ərazi">
            <option value="">Bütün ərazilər</option>
            {allLocations.map((l) => <option key={l.locationId} value={l.locationId}>{l.name}</option>)}
          </select>
        </div>
      </div>

      {preset === 'custom' && (
        <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', padding: '0 18px 8px', alignItems: 'center' }}>
          <input className="inp" type="date" value={custom.from} max={custom.to} onChange={(e) => e.target.value && setCustom((c) => ({ ...c, from: e.target.value }))} aria-label="Başlanğıc" />
          <span className="muted">—</span>
          <input className="inp" type="date" value={custom.to} min={custom.from} onChange={(e) => e.target.value && setCustom((c) => ({ ...c, to: e.target.value }))} aria-label="Son" />
        </div>
      )}

      <div style={{ padding: '0 18px 18px', display: 'flex', flexDirection: 'column', gap: 16 }}>
        {report && (
          <div className="muted" style={{ fontSize: 12 }}>
            Dövr: {showDate(report.from)} – {showDate(report.to)} · dövr göstəriciləri {report.countedTo >= report.from ? `${showDate(report.countedTo)}-dək tamamlanmış günlərdən` : 'üçün tamamlanmış gün hələ yoxdur'} · «Bu gün» kartları canlıdır
          </div>
        )}
        {loading && !report && <div className="muted">Statistika yüklənir…</div>}
        {error && <div className="muted" style={{ color: 'var(--danger, #b91c1c)' }}>{error}</div>}

        {report && (
          <>
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(150px, 1fr))', gap: 10, opacity: loading ? 0.6 : 1 }}>
              {kpis.map((k) => (
                <button key={k.key} type="button" onClick={() => setOpen(k.key)} className="card"
                  style={{ textAlign: 'left', padding: '14px 16px', cursor: 'pointer', border: '1px solid var(--c100)' }}>
                  <div style={{ fontSize: 26, fontWeight: 800, color: 'var(--c900)' }}>{k.value}</div>
                  <div className="muted" style={{ fontSize: 12, marginTop: 2 }}>{k.label}</div>
                </button>
              ))}
            </div>

            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(280px, 1fr))', gap: 12, opacity: loading ? 0.6 : 1 }}>
              {(['disciplined', 'late', 'absent', 'permission', 'sick'] as const).map((key) => {
                const all = listOf(key)
                return (
                  <div key={key} className="card" style={{ padding: 14, border: '1px solid var(--c100)', display: 'flex', flexDirection: 'column', gap: 8 }}>
                    <div>
                      <div style={{ fontWeight: 800, color: 'var(--c900)' }}>{defs[key].title}</div>
                      <div className="muted" style={{ fontSize: 12 }}>{defs[key].sub}</div>
                    </div>
                    {all.length === 0 ? (
                      <div className="muted" style={{ fontSize: 13, padding: '6px 0' }}>Bu dövrdə məlumat yoxdur</div>
                    ) : (
                      <ol style={{ margin: 0, paddingLeft: 20, display: 'flex', flexDirection: 'column', gap: 4 }}>
                        {all.slice(0, 10).map((e) => (
                          <li key={e.employeeId} style={{ fontSize: 13 }}>
                            <div style={{ display: 'flex', justifyContent: 'space-between', gap: 8 }}>
                              <span style={{ minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                                <EmployeeLink id={e.employeeId} name={e.name} />
                              </span>
                              <b style={{ whiteSpace: 'nowrap', fontVariantNumeric: 'tabular-nums' }}>{defs[key].head(e)}</b>
                            </div>
                            <div className="muted" style={{ fontSize: 11 }}>{e.locationName}</div>
                          </li>
                        ))}
                      </ol>
                    )}
                    {all.length > 0 && (
                      <button type="button" className="btn" style={{ alignSelf: 'flex-start', marginTop: 'auto' }} onClick={() => setOpen(key)}>
                        Hamısına bax ({all.length})
                      </button>
                    )}
                  </div>
                )
              })}
            </div>

            <LocationsTable rows={report.locations} />
          </>
        )}
      </div>

      {open && report && (
        <PeopleDrawer
          def={defs[open]}
          rows={listOf(open)}
          period={`${showDate(report.from)} – ${showDate(report.to)}`}
          onClose={() => setOpen(null)}
        />
      )}
    </section>
  )
}

function LocationsTable({ rows }: { rows: HrStatsLocation[] }) {
  if (rows.length === 0) return null
  const sorted = [...rows].sort((a, b) => (b.attendancePercent ?? -1) - (a.attendancePercent ?? -1))
  return (
    <div>
      <div style={{ fontWeight: 800, color: 'var(--c900)', marginBottom: 8 }}>Ərazilər üzrə müqayisə</div>
      <div className="tbl-wrap">
        <table>
          <thead>
            <tr>
              <th>Ərazi</th><th>İşçi</th><th>Davamiyyət</th><th>Gecikmə</th><th>İcazə</th><th>Xəstəlik günü</th>
            </tr>
          </thead>
          <tbody>
            {sorted.map((l) => (
              <tr key={l.locationId}>
                <td>{l.name}</td>
                <td>{l.employees}</td>
                <td style={{ minWidth: 150 }}>
                  <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                    <div style={{ flex: 1, height: 6, borderRadius: 3, background: 'var(--c100)', overflow: 'hidden' }}>
                      <div style={{ width: `${Math.max(0, Math.min(100, l.attendancePercent ?? 0))}%`, height: '100%', background: 'var(--leaf)' }} />
                    </div>
                    <span style={{ fontVariantNumeric: 'tabular-nums', minWidth: 48, textAlign: 'right' }}>{pct(l.attendancePercent)}</span>
                  </div>
                </td>
                <td>{l.lateDays}</td>
                <td>{l.permissionCount}</td>
                <td>{l.sickDays}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  )
}

function PeopleDrawer({ def, rows, period, onClose }: { def: ListDef; rows: HrStatsEmployee[]; period: string; onClose: () => void }) {
  const [q, setQ] = useState('')
  const [loc, setLoc] = useState('')
  const [sort, setSort] = useState<{ i: number; desc: boolean } | null>(null)
  const locations = useMemo(() => [...new Set(rows.map((r) => r.locationName))].sort((a, b) => a.localeCompare(b, 'az')), [rows])

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose() }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  const shown = useMemo(() => {
    const kept = rows.filter((r) => (!loc || r.locationName === loc) && matchesSearch(q, r.name, r.locationName, r.position))
    if (!sort) return kept
    const col = def.cols[sort.i]
    return [...kept].sort((a, b) => {
      const x = col.sort(a), y = col.sort(b)
      const c = typeof x === 'number' && typeof y === 'number' ? x - y : String(x).localeCompare(String(y), 'az')
      return sort.desc ? -c : c
    })
  }, [rows, q, loc, sort, def])

  return (
    <>
      <div className="acc-backdrop" onClick={onClose} />
      <aside className="acc-drawer" role="dialog" aria-label={def.title} style={{ width: 'min(760px, 100vw)' }}>
        <div className="acc-drawer-head">
          <div>
            <h3 className="acc-drawer-title">{def.title}</h3>
            <p className="acc-drawer-sub">{def.sub} · {period} · {shown.length} nəfər</p>
          </div>
          <button type="button" className="acc-close" onClick={onClose} aria-label="Bağla"><IconX /></button>
        </div>
        <div className="acc-drawer-body">
          <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
            <input className="inp" type="search" value={q} onChange={(e) => setQ(e.target.value)} placeholder="Ad, ərazi və ya vəzifə axtar…" style={{ flex: 1, minWidth: 180 }} autoFocus />
            <select className="inp" value={loc} onChange={(e) => setLoc(e.target.value)} aria-label="Ərazi" style={{ maxWidth: 240 }}>
              <option value="">Bütün ərazilər</option>
              {locations.map((l) => <option key={l} value={l}>{l}</option>)}
            </select>
          </div>
          {shown.length === 0 ? (
            <div className="muted">Nəticə yoxdur.</div>
          ) : (
            <div className="tbl-wrap">
              <table>
                <thead>
                  <tr>
                    <th>#</th>
                    <th>Ad, soyad</th>
                    <th>Ərazi</th>
                    {def.cols.map((c, i) => (
                      <th key={c.h}>
                        <button type="button" className={`tbl-sort${sort?.i === i ? ' active' : ''}`}
                          onClick={() => setSort((s) => (s?.i === i ? { i, desc: !s.desc } : { i, desc: true }))}>
                          {c.h}<span className="tbl-sort-a">{sort?.i === i ? (sort.desc ? '↓' : '↑') : ''}</span>
                        </button>
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {shown.map((e, n) => (
                    <tr key={e.employeeId}>
                      <td className="muted">{n + 1}</td>
                      <td><EmployeeLink id={e.employeeId} name={e.name} /></td>
                      <td>{e.locationName}</td>
                      {def.cols.map((c) => <td key={c.h} style={{ fontVariantNumeric: 'tabular-nums' }}>{c.v(e)}</td>)}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      </aside>
    </>
  )
}
