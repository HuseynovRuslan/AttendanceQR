import type { AttendanceRecord } from '../api/attendance'
import { RecordBadge } from './StatusBadge'
import { fmtDate, fmtDuration, fmtTime } from '../lib/att'

/** One attendance record row — shared by Home, Statistics and history lists. */
export function HistoryRow({ r }: { r: AttendanceRecord }) {
  return (
    <div className="flex items-center justify-between gap-3 rounded-2xl border border-slate-100 bg-white p-4 shadow-sm">
      <div className="min-w-0">
        <div className="font-semibold">{fmtDate(r.attendanceDate)}</div>
        <div className="mt-0.5 text-sm text-slate-500">
          {fmtTime(r.checkInAtUtc)} – {fmtTime(r.checkOutAtUtc)}
          {r.checkInAtUtc && r.checkOutAtUtc && ` · ${fmtDuration(r.checkInAtUtc, r.checkOutAtUtc)}`}
        </div>
      </div>
      <RecordBadge r={r} />
    </div>
  )
}

export function EmptyCard({ text }: { text: string }) {
  return (
    <div className="rounded-2xl border border-slate-100 bg-white p-8 text-center text-slate-400 shadow-sm">
      {text}
    </div>
  )
}

export function SkeletonList({ rows = 3 }: { rows?: number }) {
  return (
    <div className="flex flex-col gap-2">
      {Array.from({ length: rows }).map((_, i) => (
        <div key={i} className="h-16 animate-pulse rounded-2xl bg-slate-100" />
      ))}
    </div>
  )
}
