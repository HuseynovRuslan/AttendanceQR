import { useEffect, useState } from 'react'
import {
  downloadTabelExcel,
  getMyLocations,
  getTabel,
  type LocationDto,
  type TabelReport,
  type TabelRow,
} from '../../api/admin'
import { IconDownload } from '../../components/icons'
import { formatNumber } from '../../lib/numberFormat'
import { tabelScopeLabel, wholeScopeLabel } from '../../lib/tabelScope'
import { useAccess } from '../../auth/AccessContext'
import './tabel.css'

const MONTHS = [
  'Yanvar', 'Fevral', 'Mart', 'Aprel', 'May', 'İyun',
  'İyul', 'Avqust', 'Sentyabr', 'Oktyabr', 'Noyabr', 'Dekabr',
]

// Each code gets a colour so the month reads as a picture, not a wall of letters — a run of red
// jumps out as a problem long before anyone reads the name beside it.
const CODE_CLASS: Record<string, string> = {
  'İ': 'tb-work',
  'Q': 'tb-absent',
  'M': 'tb-leave',
  'X': 'tb-sick',
  'ÖM': 'tb-leave',
  'İC': 'tb-perm',
  'Ez': 'tb-trip',
  'B': 'tb-holiday',
  'H': 'tb-off',
  // «İs» — a rest day somebody GRANTED, as against «H», the roster's own day off. Its own tint, or
  // the month reads as though nobody ever assigned one: two thirds of every leave filed in this
  // product is an İstirahət, and until now all of them printed as the plain weekend.
  'İs': 'tb-rest',
  // «G» — Gözlənilir: the shift (start + late grace) has not passed yet, so it is neither worked nor an
  // absence. Only ever today's column, or yesterday's for the minutes a late-night grace runs past
  // midnight. Neutral on purpose: it must never read as the first stage of a Qayıb.
  'G': 'tb-pending',
}

/** A flexible day's cell: hours worked read as worked, «0» as a quiet day, «Açıq» as the one thing to fix. */
function flexibleCellClass(cell: string): string {
  if (cell === 'Açıq') return 'tb-absent'
  if (cell === '0') return 'tb-off'
  return 'tb-work'
}

/** Weekday initial under each day number, so the eye can find Sundays without counting. */
const WEEKDAY_AZ = ['B', 'Be', 'Ça', 'Çə', 'Ca', 'Cü', 'Ş'] // Sunday..Saturday

/**
 * Aylıq Tabel — the monthly timesheet an accountant reconciles at month end.
 *
 * The whole value is that every cell is derived from data already in the system — a check-in, an
 * approved leave, a declared holiday — so it is never typed by hand and never disagrees with the
 * other reports. The screen's job is only to make a 31-wide grid readable and printable.
 */
export function TabelPage() {
  const { access } = useAccess()
  const now = new Date()
  const [year, setYear] = useState(now.getFullYear())
  const [month, setMonth] = useState(now.getMonth() + 1) // 1-based
  const [locationId, setLocationId] = useState('')
  const [locations, setLocations] = useState<LocationDto[]>([])
  const [report, setReport] = useState<TabelReport | null>(null)
  const [loading, setLoading] = useState(false)
  const [exporting, setExporting] = useState(false)

  useEffect(() => {
    void getMyLocations().then(({ status, data }) => {
      if (status === 200 && Array.isArray(data)) setLocations(data)
    })
  }, [])

  useEffect(() => {
    setLoading(true)
    void getTabel(year, month, locationId || undefined).then(({ status, data }) => {
      setReport(status === 200 && data && 'rows' in data ? data : null)
      setLoading(false)
    })
  }, [year, month, locationId])

  async function onExport() {
    setExporting(true)
    try {
      await downloadTabelExcel(year, month, locationId || undefined)
    } finally {
      setExporting(false)
    }
  }

  function shiftMonth(delta: number) {
    const d = new Date(year, month - 1 + delta, 1)
    setYear(d.getFullYear())
    setMonth(d.getMonth() + 1)
  }

  const dayHeaders = report
    ? Array.from({ length: report.daysInMonth }, (_, i) => {
        const date = new Date(year, month - 1, i + 1)
        const dow = date.getDay() // 0=Sunday
        return { day: i + 1, weekday: WEEKDAY_AZ[dow], isRest: dow === 0 }
      })
    : []

  // The grid is ~31 <td> per employee: at 2000 people "Bütün filiallar" is ~70,000 cells, which
  // freezes the tab and hangs printing. Rather than changing the default (small companies rightly
  // see everyone at once), the RENDER is capped — the accountant picks a branch to go deeper, and
  // the Excel export is untouched because the server builds that from the full data either way.
  const RENDER_CAP = 400
  const rows = report ? report.rows.slice(0, RENDER_CAP) : []
  const hiddenRows = report ? report.rows.length - rows.length : 0
  // The «Norma» column only when somebody on the page is on a flexible shift with a norm.
  const hasNorm = rows.some((r) => r.normHours != null || r.weeklyNormHours != null)
  // «Elastik qrafik» with a weekly norm: compared week by week below the grid, never as a month figure.
  const weekly = rows.filter((r) => r.normWeeks && r.normWeeks.length > 0)
  // What «all» means for this person, and the heading's scope — from the role and /api/me/access,
  // never from the report's scopeLabel (see lib/tabelScope).
  const allLabel = wholeScopeLabel(access)
  const scope = tabelScopeLabel(locationId, locations, access)

  return (
    <div className="tb-wrap">
      {/* Controls stay out of print — the printed sheet is the grid and its legend, nothing else. */}
      <div className="tb-controls no-print">
        <div className="tb-month">
          <button className="btn btn-sm" onClick={() => shiftMonth(-1)} aria-label="Əvvəlki ay">‹</button>
          <select className="inp" value={month} onChange={(e) => setMonth(Number(e.target.value))}>
            {MONTHS.map((m, i) => <option key={m} value={i + 1}>{m}</option>)}
          </select>
          <select className="inp" value={year} onChange={(e) => setYear(Number(e.target.value))}>
            {[now.getFullYear(), now.getFullYear() - 1].map((y) => <option key={y} value={y}>{y}</option>)}
          </select>
          <button className="btn btn-sm" onClick={() => shiftMonth(1)} aria-label="Növbəti ay">›</button>
        </div>

        <select className="inp" value={locationId} onChange={(e) => setLocationId(e.target.value)}>
          <option value="">{allLabel}</option>
          {locations.map((l) => <option key={l.id} value={l.id}>{l.name}</option>)}
        </select>

        <div className="tb-actions">
          <button className="btn btn-sm" onClick={() => window.print()}>Çap et</button>
          <button className="btn btn-primary btn-sm" disabled={exporting || !report} onClick={() => void onExport()}>
            <IconDownload /> {exporting ? 'Yüklənir…' : 'Excel'}
          </button>
        </div>
      </div>

      <div className="tb-title">
        {MONTHS[month - 1]} {year} — Tabel
        <span className="tb-scope"> · {scope}</span>
      </div>

      {loading && <div className="card card-pad muted">Yüklənir…</div>}

      {!loading && report && report.rows.length === 0 && (
        <div className="card card-pad muted" style={{ textAlign: 'center' }}>Bu ay üçün işçi tapılmadı.</div>
      )}

      {!loading && report && hiddenRows > 0 && (
        <div className="card card-pad no-print" style={{ marginBottom: 10, background: 'var(--sand)', fontSize: 13 }}>
          Siyahı çox böyükdür: {report.rows.length} işçidən ilk {RENDER_CAP}-ü göstərilir.
          Hamısını görmək üçün <b>filial seçin</b> — Excel faylına isə bütün işçilər onsuz da tam düşür.
        </div>
      )}

      {!loading && report && report.rows.length > 0 && (
        <div className="tb-scroll">
          <table className="tb-grid">
            <thead>
              <tr>
                <th className="tb-name-col">İşçi</th>
                {dayHeaders.map((h) => (
                  <th key={h.day} className={h.isRest ? 'tb-rest-col' : ''}>
                    <span className="tb-daynum">{h.day}</span>
                    <span className="tb-dow">{h.weekday}</span>
                  </th>
                ))}
                <th className="tb-total-col">İş</th>
                <th className="tb-total-col">Qayıb</th>
                <th className="tb-total-col">Saat</th>
                {hasNorm && <th className="tb-total-col" title="Sərbəst iş qrafikinin saat norması — yalnız müqayisə üçün">Norma</th>}
              </tr>
            </thead>
            <tbody>
              {rows.map((r) => (
                <tr key={r.employeeId}>
                  <th className="tb-name-col" scope="row">
                    <div className="tb-name">{r.employeeName}</div>
                    {r.position && <div className="tb-pos">{r.position}</div>}
                  </th>
                  {r.days.map((code, i) => {
                    // A day the person corrected themselves: the code, a star, and a word on hover.
                    const own = r.selfCorrectedDays?.includes(i + 1) === true
                    // «Elastik qrafik»: the hours worked instead of the code — «8», «0», «Açıq».
                    const flex = r.flexibleCells?.[i] ?? null
                    if (flex != null) {
                      return (
                        <td
                          key={i}
                          className={`${flexibleCellClass(flex)}${own ? ' tb-self' : ''}`}
                          title={flex === 'Açıq'
                            ? 'Sərbəst iş qrafiki — çıxış qeyd olunmayıb, düzəliş gözləyir'
                            : `Sərbəst iş qrafiki — ${flex.replace('.', ',')} saat işləyib`}
                          style={flex === 'Açıq' ? { fontSize: 10 } : undefined}
                        >
                          {flex.replace('.', ',')}{own && '*'}
                        </td>
                      )
                    }
                    return (
                      <td
                        key={i}
                        className={`${code ? CODE_CLASS[code] ?? '' : 'tb-future'}${own ? ' tb-self' : ''}`}
                        title={own ? 'Öz düzəlişi — işçi bu günü özü düzəldib' : undefined}
                      >
                        {code}{own && '*'}
                      </td>
                    )
                  })}
                  <td className="tb-total-col tb-total-work">{r.workedDays}</td>
                  <td className="tb-total-col tb-total-absent">{r.absentDays || ''}</td>
                  <td className="tb-total-col">{r.workedHours ? formatNumber(r.workedHours, 1, { trim: true }) : ''}</td>
                  {hasNorm && (
                    <td className="tb-total-col" title={r.normHours != null
                      ? `Fərq: ${formatNumber((r.workedHours ?? 0) - r.normHours, 1, { trim: true })} saat`
                      : r.weeklyNormHours != null ? 'Həftəlik norma — həftələr üzrə müqayisə cədvəlin altındadır' : undefined}>
                      {r.normHours != null
                        ? formatNumber(r.normHours, 1, { trim: true })
                        : r.weeklyNormHours != null ? `${formatNumber(r.weeklyNormHours, 1, { trim: true })}/həftə` : ''}
                    </td>
                  )}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {weekly.length > 0 && <WeeklyNorm rows={weekly} />}

      {report && (
        <div className="tb-legend">
          {/* «Elastik qrafik»: its cells are hours, so the legend says what the three kinds of cell mean. */}
          {report.rows.some((r) => r.flexibleCells) && (
            <span className="tb-legend-item">
              <span className="tb-legend-code tb-work">8</span>
              Sərbəst iş qrafiki: işlənmiş saat · «0» — giriş yoxdur · «Açıq» — çıxış qeyd olunmayıb
            </span>
          )}
          {report.legend.map((item) => (
            <span key={item.code} className="tb-legend-item">
              <span className={`tb-legend-code ${CODE_CLASS[item.code] ?? ''}`}>{item.code}</span>
              {item.label}
            </span>
          ))}
        </div>
      )}
    </div>
  )
}

/** «12.11» — the week's edges, day and month: a week often starts in the month before. */
function dayMonth(iso: string): string {
  return `${iso.slice(8, 10)}.${iso.slice(5, 7)}`
}

/**
 * «Elastik qrafik» with a weekly norm: each Monday-to-Sunday week's whole total against the norm as set. Never divided
 * into days — 40 hours is 40 hours however they were spread — and never a violation: a short week is shown, not judged.
 */
function WeeklyNorm({ rows }: { rows: TabelRow[] }) {
  return (
    <section className="tb-weekly" aria-labelledby="tb-weekly-h">
      <h3 id="tb-weekly-h" className="tb-weekly-title">Həftəlik norma — faktiki saat</h3>
      <p className="tb-weekly-note">Bazar ertəsindən bazara qədər ümumi saat. Norma yalnız müqayisə üçündür.</p>
      <div className="tb-scroll">
        <table className="tb-grid tb-weekly-grid">
          <thead>
            <tr>
              <th className="tb-name-col">İşçi</th>
              <th>Həftə</th>
              <th>Faktiki</th>
              <th>Norma</th>
              <th>Fərq</th>
              <th>Qeyd</th>
            </tr>
          </thead>
          <tbody>
            {rows.flatMap((r) => (r.normWeeks ?? []).map((w, i) => {
              const diff = w.workedHours - w.normHours
              const note = [!w.finished && 'həftə bitməyib', w.hasLeaveOrHoliday && 'məzuniyyət/bayram var'].filter(Boolean).join(', ')
              return (
                <tr key={`${r.employeeId}-${w.from}`}>
                  {i === 0 && <td className="tb-name-col" rowSpan={r.normWeeks!.length}><div className="tb-name">{r.employeeName}</div></td>}
                  <td>{dayMonth(w.from)} – {dayMonth(w.to)}</td>
                  <td>{formatNumber(w.workedHours, 1, { trim: true })}</td>
                  <td>{formatNumber(w.normHours, 1, { trim: true })}</td>
                  <td className={w.finished && diff < 0 ? 'tb-weekly-short' : undefined}>
                    {diff > 0 ? '+' : ''}{formatNumber(diff, 1, { trim: true })}
                  </td>
                  <td className="tb-weekly-flag">{note}</td>
                </tr>
              )
            }))}
          </tbody>
        </table>
      </div>
    </section>
  )
}
