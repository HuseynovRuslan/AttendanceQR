import { useEffect, useState } from 'react'
import {
  downloadTabelExcel,
  getMyLocations,
  getTabel,
  type LocationDto,
  type TabelReport,
} from '../../api/admin'
import { IconDownload } from '../../components/icons'
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
  'B': 'tb-holiday',
  'H': 'tb-off',
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
          <option value="">Bütün filiallar</option>
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
        {report && <span className="tb-scope"> · {report.scopeLabel}</span>}
      </div>

      {loading && <div className="card card-pad muted">Yüklənir…</div>}

      {!loading && report && report.rows.length === 0 && (
        <div className="card card-pad muted" style={{ textAlign: 'center' }}>Bu ay üçün işçi tapılmadı.</div>
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
              </tr>
            </thead>
            <tbody>
              {report.rows.map((r) => (
                <tr key={r.employeeId}>
                  <th className="tb-name-col" scope="row">
                    <div className="tb-name">{r.employeeName}</div>
                    {r.position && <div className="tb-pos">{r.position}</div>}
                  </th>
                  {r.days.map((code, i) => (
                    <td key={i} className={code ? CODE_CLASS[code] ?? '' : 'tb-future'}>{code}</td>
                  ))}
                  <td className="tb-total-col tb-total-work">{r.workedDays}</td>
                  <td className="tb-total-col tb-total-absent">{r.absentDays || ''}</td>
                  <td className="tb-total-col">{r.workedHours || ''}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {report && (
        <div className="tb-legend">
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
