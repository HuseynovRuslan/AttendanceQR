import { useState } from 'react'
import { getToday, type DayAttendanceRow } from '../../api/admin'
import { usePolling } from '../../lib/usePolling'
import { StatusBadge } from '../../components/StatusBadge'

export function TodayPage() {
  const [rows, setRows] = useState<DayAttendanceRow[]>([])
  const [error, setError] = useState<string | null>(null)
  const [loadedOnce, setLoadedOnce] = useState(false)

  usePolling(async () => {
    const { status, data } = await getToday()
    if (status === 200 && Array.isArray(data)) {
      setRows(data)
      setError(null)
    } else if (status === 403) {
      setError('İcazəniz yoxdur')
    } else {
      setError('Məlumat yüklənmədi')
    }
    setLoadedOnce(true)
  }, 30_000)

  return (
    <div>
      <div className="flex items-center justify-between mb-4">
        <h1 className="text-2xl font-bold text-slate-800">Bugünkü davamiyyət</h1>
        <span className="text-xs text-slate-400">Hər 30 saniyədə yenilənir</span>
      </div>

      {error && <div className="bg-red-50 text-red-700 rounded-lg p-3 mb-3">{error}</div>}

      <div className="bg-white rounded-xl shadow overflow-x-auto">
        <table className="w-full text-sm">
          <thead className="bg-slate-50 text-slate-500 text-left">
            <tr>
              <th className="px-4 py-3 font-medium">İşçi</th>
              <th className="px-4 py-3 font-medium">Ərazi</th>
              <th className="px-4 py-3 font-medium">Status</th>
              <th className="px-4 py-3 font-medium">Giriş</th>
              <th className="px-4 py-3 font-medium">Çıxış</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-100">
            {rows.map((r) => (
              <tr key={r.employeeId}>
                <td className="px-4 py-3 font-medium text-slate-800">{r.employeeName}</td>
                <td className="px-4 py-3 text-slate-600">{r.locationName}</td>
                <td className="px-4 py-3">
                  <StatusBadge status={r.status} />
                </td>
                <td className="px-4 py-3 text-slate-600 tabular-nums">{fmtTime(r.checkInAtUtc)}</td>
                <td className="px-4 py-3 text-slate-600 tabular-nums">{fmtTime(r.checkOutAtUtc)}</td>
              </tr>
            ))}
            {loadedOnce && rows.length === 0 && !error && (
              <tr>
                <td colSpan={5} className="px-4 py-10 text-center text-slate-400">
                  Məlumat yoxdur
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  )
}

function fmtTime(iso: string | null): string {
  return iso ? new Date(iso).toLocaleTimeString('az-AZ', { hour: '2-digit', minute: '2-digit' }) : '—'
}
