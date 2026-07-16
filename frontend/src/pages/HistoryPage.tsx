import { useEffect, useState } from 'react'
import { getMyAttendance, getMySummary, type AttendanceRecord } from '../api/attendance'
import { STATUS_MAP } from '../components/StatusBadge'
import { EmployeeNav } from '../components/EmployeeNav'
import { fmtDate, fmtDuration, fmtTime } from '../lib/format'

const STATUS_TONE: Record<string, string> = {
  OnTime: 'bg-green-500/20 text-green-400',
  Late: 'bg-amber-500/20 text-amber-400',
  Absent: 'bg-red-500/20 text-red-400',
  Incomplete: 'bg-blue-500/20 text-blue-400',
}

export function HistoryPage() {
  const [records, setRecords] = useState<AttendanceRecord[]>([])
  const [summary, setSummary] = useState<{ workDays: number; lateCount: number; absentDays: number } | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    void load()
  }, [])

  async function load() {
    setLoading(true)
    setError(null)
    const to = new Date().toISOString().slice(0, 10)
    const from = new Date(Date.now() - 30 * 24 * 60 * 60 * 1000).toISOString().slice(0, 10)

    const [attRes, sumRes] = await Promise.all([getMyAttendance(), getMySummary(from, to)])

    if (attRes.status === 200 && Array.isArray(attRes.data)) {
      const recent = attRes.data.filter((r) => r.attendanceDate >= from).sort((a, b) => (a.attendanceDate < b.attendanceDate ? 1 : -1))
      setRecords(recent)
    } else {
      setError('Tarixçə yüklənmədi')
    }

    if (sumRes.status === 200 && sumRes.data && 'totals' in sumRes.data) {
      setSummary(sumRes.data.totals)
    }

    setLoading(false)
  }

  return (
    <div className="min-h-screen flex flex-col bg-slate-900 text-white">
      <EmployeeNav title="Tarixçəm" />

      <main className="flex-1 p-4 flex flex-col items-center gap-4">
        <div className="w-full max-w-md">
          {summary && (
            <div className="bg-slate-800 rounded-2xl p-4 mb-4 text-center">
              <p className="text-lg">
                Son 30 gün: <b>{summary.workDays}</b> gün işlədiniz, <b>{summary.absentDays}</b> qayıb
              </p>
            </div>
          )}

          {error && <p className="text-red-400 text-center mb-4">{error}</p>}
          {loading && <p className="text-slate-400 text-center">Yüklənir…</p>}

          {!loading && records.length === 0 && !error && (
            <p className="text-slate-400 text-center">Hələ qeyd yoxdur</p>
          )}

          <div className="flex flex-col gap-2">
            {records.map((r) => (
              <div key={r.recordId} className="bg-slate-800 rounded-xl p-4 flex items-center justify-between gap-3">
                <div>
                  <div className="font-semibold">{fmtDate(r.attendanceDate)}</div>
                  <div className="text-sm text-slate-400 mt-1">
                    {fmtTime(r.checkInAtUtc)} – {fmtTime(r.checkOutAtUtc)}
                    {r.checkInAtUtc && r.checkOutAtUtc && ` · ${fmtDuration(r.checkInAtUtc, r.checkOutAtUtc)}`}
                  </div>
                </div>
                <span className={`px-3 py-1 rounded-full text-sm font-semibold whitespace-nowrap ${STATUS_TONE[r.status] ?? 'bg-slate-700 text-slate-300'}`}>
                  {STATUS_MAP[r.status]?.label ?? r.status}
                </span>
              </div>
            ))}
          </div>
        </div>
      </main>
    </div>
  )
}

