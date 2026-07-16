import { useEffect, useState } from 'react'
import { EmployeeLink } from '../../components/EmployeeLink'
import {
  downloadReportExcel,
  getMyLocations,
  getSummary,
  type AttendanceReport,
  type LocationDto,
} from '../../api/admin'
import { IconDownload, IconX } from '../../components/icons'

const todayIso = () => new Date().toISOString().slice(0, 10)
const daysAgoIso = (n: number) => new Date(Date.now() - n * 86_400_000).toISOString().slice(0, 10)

// Worked/overtime come as decimal hours (e.g. 0.32 = ~19 min, 8.53 = 8 s 32 dəq). A bare "0.32" reads
// like minutes and confuses everyone — show real "saat/dəq" instead.
function fmtHM(hours: number): string {
  const totalMin = Math.round((hours || 0) * 60)
  if (totalMin === 0) return '—'
  const h = Math.floor(totalMin / 60)
  const m = totalMin % 60
  if (h === 0) return `${m} dəq`
  return m === 0 ? `${h} saat` : `${h} saat ${m} dəq`
}

export function ReportsPage() {
  const [from, setFrom] = useState(daysAgoIso(29))
  const [to, setTo] = useState(todayIso())
  const [locationId, setLocationId] = useState('')
  const [locations, setLocations] = useState<LocationDto[]>([])
  const [report, setReport] = useState<AttendanceReport | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)
  const [exporting, setExporting] = useState(false)

  useEffect(() => {
    getMyLocations().then(({ status, data }) => {
      if (status === 200 && Array.isArray(data)) setLocations(data)
    })
  }, [])

  async function load() {
    setError(null)
    setLoading(true)
    setReport(null)
    const { status, data } = await getSummary(from, to, locationId || undefined)
    if (status === 200 && data && 'rows' in data) setReport(data)
    else if (status === 403) setError('İcazəniz yoxdur')
    else setError('Hesabat yüklənmədi')
    setLoading(false)
  }

  // eslint-disable-next-line react-hooks/exhaustive-deps
  useEffect(() => {
    void load()
  }, [])

  async function onExport() {
    setError(null)
    setExporting(true)
    try {
      await downloadReportExcel(from, to, locationId || undefined)
    } catch {
      setError('Export alınmadı')
    } finally {
      setExporting(false)
    }
  }

  return (
    <div>
      <div className="card card-pad" style={{ marginBottom: 16 }}>
        <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'flex-end', gap: 12 }}>
          <div>
            <label className="form-label">Başlanğıc</label>
            <input className="inp" type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
          </div>
          <div>
            <label className="form-label">Son</label>
            <input className="inp" type="date" value={to} onChange={(e) => setTo(e.target.value)} />
          </div>
          <div style={{ minWidth: 180 }}>
            <label className="form-label">Filial</label>
            <select className="inp" value={locationId} onChange={(e) => setLocationId(e.target.value)}>
              <option value="">Hamısı</option>
              {locations.map((l) => (
                <option key={l.id} value={l.id}>
                  {l.name}
                </option>
              ))}
            </select>
          </div>
          <button className="btn btn-primary" onClick={load} disabled={loading}>
            {loading ? 'Yüklənir…' : 'Yüklə'}
          </button>
          <button className="btn" onClick={onExport} disabled={exporting || !report}>
            <IconDownload />
            {exporting ? 'Hazırlanır…' : 'Excel export'}
          </button>
        </div>
      </div>

      {error && (
        <div className="fb fb-err" style={{ marginBottom: 12 }}>
          <IconX />
          <span>{error}</span>
        </div>
      )}

      {report && (
        <div className="tbl-wrap tbl-center-nums">
          <table>
            <thead>
              <tr>
                <th>İşçi</th>
                <th>Filial</th>
                <th className="num">İş günləri</th>
                <th className="num">Qayıb</th>
                <th className="num">Məzuniyyət</th>
                <th className="num">İcazə</th>
                <th className="num">Ümumi saat</th>
                <th className="num">Overtime</th>
              </tr>
            </thead>
            <tbody>
              {report.rows.map((r) => (
                <tr key={r.employeeId}>
                  <td style={{ fontWeight: 700, color: 'var(--c900)' }}><EmployeeLink id={r.employeeId} name={r.employeeName} /></td>
                  <td>{r.locationName}</td>
                  <td className="num mono">{r.workDays}</td>
                  <td className="num mono">{r.absentDays}</td>
                  <td className="num mono">{r.leaveDays}</td>
                  <td className="num mono">{r.permissionDays}</td>
                  <td className="num mono">{fmtHM(r.totalWorkedHours)}</td>
                  <td className="num mono">{fmtHM(r.overtimeHours)}</td>
                </tr>
              ))}
              {report.rows.length === 0 && (
                <tr>
                  <td colSpan={9} className="muted" style={{ textAlign: 'center', padding: 28 }}>
                    Bu aralıqda məlumat yoxdur
                  </td>
                </tr>
              )}
            </tbody>
            {report.rows.length > 0 && (
              <tfoot>
                <tr>
                  <td colSpan={2}>CƏM</td>
                  <td className="num mono">{report.totals.workDays}</td>
                  <td className="num mono">{report.totals.absentDays}</td>
                  <td className="num mono">{report.totals.leaveDays}</td>
                  <td className="num mono">{report.totals.permissionDays}</td>
                  <td className="num mono">{fmtHM(report.totals.totalWorkedHours)}</td>
                  <td className="num mono">{fmtHM(report.totals.overtimeHours)}</td>
                </tr>
              </tfoot>
            )}
          </table>
        </div>
      )}
    </div>
  )
}
