import type { AttendanceRecord } from '../api/attendance'
import { RecordBadge } from './StatusBadge'
import { fmtDate, fmtDuration, fmtTime } from '../lib/format'

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
        {r.manualByName && (
          <div className="mt-1 inline-flex items-center gap-1 rounded-lg bg-amber-50 px-2 py-0.5 text-xs font-medium text-amber-700">
            Əl ilə daxil edilib · {r.manualByName}
          </div>
        )}
        {/* No poster scan at all — the whole day came from a «səyyar» visit. Said plainly, because
            until now such a day did not appear in this list AT ALL: the person worked it, the payroll
            counted it, and their own screen skipped the date. */}
        {r.isFieldDay && (
          <div className="mt-1 inline-flex items-center gap-1 rounded-lg bg-emerald-50 px-2 py-0.5 text-xs font-medium text-emerald-700">
            📍 Səyyar iş günü — QR olmadan qeydə alınıb
          </div>
        )}
        {r.closedByFieldVisit && (
          <div className="mt-1 inline-flex items-center gap-1 rounded-lg bg-sky-50 px-2 py-0.5 text-xs font-medium text-sky-700">
            📍 Ərazi çıxışı ilə bağlandı
          </div>
        )}
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
