import { useEffect, useState } from 'react'
import { getMyAttendance, getMySummary, type AttendanceRecord } from '../api/attendance'
import { useAuth } from '../auth/AuthContext'
import { EmptyCard, HistoryRow, SkeletonList } from '../components/employeeBits'

interface Totals {
  workDays: number
  lateCount: number
  absentDays: number
  incompleteDays: number
  totalWorkedHours: number
  overtimeHours: number
}

function ymd(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

export function StatsPage() {
  const { employeeId } = useAuth()
  const [records, setRecords] = useState<AttendanceRecord[]>([])
  const [totals, setTotals] = useState<Totals | null>(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    void load()
  }, [])

  async function load() {
    setLoading(true)
    const now = new Date()
    const from = ymd(new Date(now.getFullYear(), now.getMonth(), 1))
    const to = ymd(now)
    const [a, s] = await Promise.all([getMyAttendance(), getMySummary(from, to)])
    if (a.status === 200 && Array.isArray(a.data)) {
      setRecords([...a.data].sort((x, y) => (x.attendanceDate < y.attendanceDate ? 1 : -1)))
    }
    // This screen is PERSONAL, but /reports/summary is role-scoped: for an admin it returns the whole
    // company (rows for everyone + a company-wide `totals`). Reading `totals` there showed the whole
    // company as "your" hours — identical and meaningless on every look. Take the caller's OWN row
    // instead; for a plain employee that is the only row, so this is correct for both.
    if (s.status === 200 && s.data && 'rows' in s.data) {
      const mine = s.data.rows.find((r) => r.employeeId === employeeId)
      setTotals(mine ? { ...mine } : null)
    }
    setLoading(false)
  }

  return (
    <div className="flex flex-col gap-4">
      <div>
        <h1 className="text-xl font-extrabold">Statistika</h1>
        <p className="text-sm text-slate-500">Bu ayın iş saatları və fəaliyyətiniz.</p>
      </div>

      <div className="grid grid-cols-2 gap-3">
        <Kpi tone="blue" label="Aylıq işlənmiş saat" value={totals ? Math.round(totals.totalWorkedHours) : '—'} unit="saat" />
        <Kpi tone="green" label="Aylıq işlənmiş gün" value={totals ? totals.workDays : '—'} unit="gün" />
      </div>

      {totals && (
        <div className="grid grid-cols-2 gap-3">
          <Chip label="Qayıb" value={totals.absentDays} tone="red" />
          <Chip label="Əlavə saat" value={Math.round(totals.overtimeHours)} tone="slate" />
        </div>
      )}

      <div>
        <h2 className="mb-2 px-1 font-bold">Skan tarixçəsi</h2>
        {loading ? (
          <SkeletonList rows={5} />
        ) : records.length === 0 ? (
          <EmptyCard text="Hələ qeyd yoxdur" />
        ) : (
          <div className="flex flex-col gap-2">
            {records.map((r) => (
              <HistoryRow key={r.recordId} r={r} />
            ))}
          </div>
        )}
      </div>
    </div>
  )
}

function Kpi({ tone, label, value, unit }: { tone: 'blue' | 'green'; label: string; value: number | string; unit: string }) {
  const bg = tone === 'blue' ? 'bg-blue-500' : 'bg-green-500'
  return (
    <div className={`rounded-3xl ${bg} p-5 text-white shadow-sm`}>
      <div className="text-sm font-semibold opacity-90">{label}</div>
      <div className="mt-2 text-4xl font-extrabold leading-none">{value}</div>
      <div className="mt-1 text-sm opacity-90">{unit}</div>
    </div>
  )
}

function Chip({ label, value, tone }: { label: string; value: number; tone: 'amber' | 'red' | 'slate' }) {
  const cls = {
    amber: 'bg-amber-50 text-amber-700 border-amber-200',
    red: 'bg-red-50 text-red-700 border-red-200',
    slate: 'bg-slate-50 text-slate-700 border-slate-200',
  }[tone]
  return (
    <div className={`rounded-2xl border p-3 text-center ${cls}`}>
      <div className="text-2xl font-extrabold leading-none">{value}</div>
      <div className="mt-1 text-xs font-semibold">{label}</div>
    </div>
  )
}
