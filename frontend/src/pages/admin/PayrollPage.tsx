import { useEffect, useState } from 'react'
import { EmployeeLink } from '../../components/EmployeeLink'
import {
  downloadPayrollExcel,
  getMyLocations,
  getPayroll,
  type LocationDto,
  type PayrollReport,
} from '../../api/admin'
import { IconDownload, IconX } from '../../components/icons'

function localDateISO(d: Date): string {
  const p = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`
}
const firstOfMonthISO = () => {
  const d = new Date()
  return localDateISO(new Date(d.getFullYear(), d.getMonth(), 1))
}
const todayISO = () => localDateISO(new Date())

// AZN with two decimals; blank when the salary isn't set (so we never print a misleading 0 ₼).
function azn(n: number | null | undefined): string {
  if (n == null) return '—'
  return n.toLocaleString('az-AZ', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
}

export function PayrollPage() {
  const [from, setFrom] = useState(firstOfMonthISO())
  const [to, setTo] = useState(todayISO())
  const [locationId, setLocationId] = useState('')
  const [locations, setLocations] = useState<LocationDto[]>([])
  const [report, setReport] = useState<PayrollReport | null>(null)
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
    const { status, data } = await getPayroll(from, to, locationId || undefined)
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
      await downloadPayrollExcel(from, to, locationId || undefined)
    } catch {
      setError('Export alınmadı')
    } finally {
      setExporting(false)
    }
  }

  const noSalarySet = report && report.rows.length > 0 && report.rows.every((r) => r.monthlySalary == null)

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
        <p style={{ fontSize: 12, color: 'var(--c500)', marginTop: 10, marginBottom: 0 }}>
          Ödəniləcək = Aylıq maaş − (gündəlik × icazəsiz qayıb). Məzuniyyət və icazə çıxılmır. Overtime
          saatları ayrıca göstərilir, avtomatik pula çevrilmir.
        </p>
      </div>

      {error && (
        <div className="fb fb-err" style={{ marginBottom: 12 }}>
          <IconX />
          <span>{error}</span>
        </div>
      )}

      {noSalarySet && (
        <div className="fb fb-warn" style={{ marginBottom: 12 }}>
          <span>
            Heç bir işçidə aylıq maaş təyin edilməyib. İşçilər səhifəsindən hər işçiyə "Aylıq maaş"
            yazın — sonra bu cədvəl pulu hesablayacaq.
          </span>
        </div>
      )}

      {report && (
        <div className="tbl-wrap tbl-center-nums">
          <table>
            <thead>
              <tr>
                <th>İşçi</th>
                <th>Filial</th>
                <th className="num">Aylıq maaş</th>
                <th className="num">İş günü</th>
                <th className="num">Gəlib</th>
                <th className="num">Qayıb</th>
                <th className="num">Məz./İcazə</th>
                <th className="num">Çıxılan</th>
                <th className="num">Ödəniləcək</th>
              </tr>
            </thead>
            <tbody>
              {report.rows.map((r) => (
                <tr key={r.employeeId} style={{ opacity: r.monthlySalary == null ? 0.6 : 1 }}>
                  <td data-label="İşçi" style={{ fontWeight: 700, color: 'var(--c900)' }}>
                    <EmployeeLink id={r.employeeId} name={r.employeeName} />
                  </td>
                  <td data-label="Filial">{r.locationName}</td>
                  <td data-label="Aylıq maaş" className="num mono">{azn(r.monthlySalary)}</td>
                  <td data-label="İş günü" className="num mono">{r.scheduledDays}</td>
                  <td data-label="Gəlib" className="num mono">{r.workDays}</td>
                  <td data-label="Qayıb" className="num mono" style={{ color: r.absentDays > 0 ? 'var(--clay)' : undefined }}>
                    {r.absentDays}
                  </td>
                  <td data-label="Məz./İcazə" className="num mono">{r.leaveDays + r.permissionDays}</td>
                  <td data-label="Çıxılan" className="num mono" style={{ color: r.deduction > 0 ? 'var(--clay)' : undefined }}>
                    {r.monthlySalary == null ? '—' : azn(r.deduction)}
                  </td>
                  <td data-label="Ödəniləcək" className="num mono" style={{ fontWeight: 700, color: 'var(--c900)' }}>
                    {r.monthlySalary == null ? '—' : azn(r.payable)}
                  </td>
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
                  <td colSpan={2}>CƏMİ</td>
                  <td className="num mono">{azn(report.totalMonthlySalary)}</td>
                  <td className="num" colSpan={3} />
                  <td className="num" />
                  <td className="num mono">{azn(report.totalDeduction)}</td>
                  <td className="num mono" style={{ fontWeight: 800 }}>{azn(report.totalPayable)}</td>
                </tr>
              </tfoot>
            )}
          </table>
        </div>
      )}
    </div>
  )
}
