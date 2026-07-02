import { useEffect, useState, type ReactNode } from 'react'
import {
  downloadReportExcel,
  getMyLocations,
  getSummary,
  type AttendanceReport,
  type LocationDto,
} from '../../api/admin'

const todayIso = () => new Date().toISOString().slice(0, 10)
const daysAgoIso = (n: number) => new Date(Date.now() - n * 86_400_000).toISOString().slice(0, 10)

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
      <h1 className="text-2xl font-bold text-slate-800 mb-4">Hesabat</h1>

      <div className="bg-white rounded-xl shadow p-4 mb-4 flex flex-wrap items-end gap-3">
        <Field label="Başlanğıc">
          <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} className="rounded-lg border border-slate-300 px-3 py-2 focus:outline-none focus:ring-2 focus:ring-blue-500" />
        </Field>
        <Field label="Son">
          <input type="date" value={to} onChange={(e) => setTo(e.target.value)} className="rounded-lg border border-slate-300 px-3 py-2 focus:outline-none focus:ring-2 focus:ring-blue-500" />
        </Field>
        <Field label="Ərazi">
          <select value={locationId} onChange={(e) => setLocationId(e.target.value)} className="rounded-lg border border-slate-300 px-3 py-2 focus:outline-none focus:ring-2 focus:ring-blue-500">
            <option value="">Hamısı</option>
            {locations.map((l) => (
              <option key={l.id} value={l.id}>
                {l.name}
              </option>
            ))}
          </select>
        </Field>
        <button
          onClick={load}
          disabled={loading}
          className="bg-blue-600 hover:bg-blue-700 disabled:opacity-50 text-white rounded-lg px-4 py-2 font-medium"
        >
          {loading ? 'Yüklənir…' : 'Yüklə'}
        </button>
        <button
          onClick={onExport}
          disabled={exporting || !report}
          className="bg-green-600 hover:bg-green-700 disabled:opacity-50 text-white rounded-lg px-4 py-2 font-medium"
        >
          {exporting ? 'Hazırlanır…' : '⬇ Excel export'}
        </button>
      </div>

      {error && <div className="bg-red-50 text-red-700 rounded-lg p-3 mb-3">{error}</div>}

      {report && (
        <div className="bg-white rounded-xl shadow overflow-x-auto">
          <table className="w-full text-sm">
            <thead className="bg-slate-50 text-slate-500 text-left">
              <tr>
                <th className="px-4 py-3 font-medium">İşçi</th>
                <th className="px-4 py-3 font-medium">Ərazi</th>
                <th className="px-4 py-3 font-medium text-right">İş günləri</th>
                <th className="px-4 py-3 font-medium text-right">Gecikmə</th>
                <th className="px-4 py-3 font-medium text-right">Qaib</th>
                <th className="px-4 py-3 font-medium text-right">Ümumi saat</th>
                <th className="px-4 py-3 font-medium text-right">Overtime</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {report.rows.map((r) => (
                <tr key={r.employeeId}>
                  <td className="px-4 py-3 font-medium text-slate-800">{r.employeeName}</td>
                  <td className="px-4 py-3 text-slate-600">{r.locationName}</td>
                  <td className="px-4 py-3 text-right tabular-nums">{r.workDays}</td>
                  <td className="px-4 py-3 text-right tabular-nums">{r.lateCount}</td>
                  <td className="px-4 py-3 text-right tabular-nums">{r.absentDays}</td>
                  <td className="px-4 py-3 text-right tabular-nums">{r.totalWorkedHours}</td>
                  <td className="px-4 py-3 text-right tabular-nums">{r.overtimeHours}</td>
                </tr>
              ))}
              {report.rows.length === 0 && (
                <tr>
                  <td colSpan={7} className="px-4 py-10 text-center text-slate-400">
                    Bu aralıqda məlumat yoxdur
                  </td>
                </tr>
              )}
            </tbody>
            {report.rows.length > 0 && (
              <tfoot className="bg-slate-50 font-semibold text-slate-800">
                <tr>
                  <td className="px-4 py-3" colSpan={2}>
                    CƏM
                  </td>
                  <td className="px-4 py-3 text-right tabular-nums">{report.totals.workDays}</td>
                  <td className="px-4 py-3 text-right tabular-nums">{report.totals.lateCount}</td>
                  <td className="px-4 py-3 text-right tabular-nums">{report.totals.absentDays}</td>
                  <td className="px-4 py-3 text-right tabular-nums">{report.totals.totalWorkedHours}</td>
                  <td className="px-4 py-3 text-right tabular-nums">{report.totals.overtimeHours}</td>
                </tr>
              </tfoot>
            )}
          </table>
        </div>
      )}
    </div>
  )
}

function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <label className="flex flex-col gap-1">
      <span className="text-xs font-medium text-slate-500">{label}</span>
      {children}
    </label>
  )
}
