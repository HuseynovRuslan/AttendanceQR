import { useEffect, useState } from 'react'
import { getMissedCheckoutStatus, type MissedCheckoutStatusResp } from '../api/attendance'
import { fmtDayMonth } from '../lib/format'

/**
 * Red, information-only alert for a day the employee forgot to scan out. Deliberately NOT actionable:
 * the employee does not report the time themselves — a manager/admin closes the open day from
 * /admin/open-records. It just keeps warning them (so it doesn't become a habit) until that happens.
 */
export function MissedCheckoutBanner() {
  const [status, setStatus] = useState<MissedCheckoutStatusResp | null>(null)

  useEffect(() => {
    void getMissedCheckoutStatus().then((r) => {
      if (r.status === 200 && r.data && 'openDay' in r.data) setStatus(r.data)
    })
  }, [])

  if (!status?.openDay) return null

  // Only for the day right after the miss (one day) — not forever on every open. Older un-closed days
  // are the admin's to close (/admin/open-records); the employee isn't nagged about them anymore.
  const yesterday = new Date(Date.now() - 86_400_000).toISOString().slice(0, 10)
  if (status.openDay.attendanceDate !== yesterday) return null

  return (
    <div className="rounded-3xl border-2 border-red-300 bg-red-50 p-4">
      <div className="flex items-center gap-3">
        <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-red-100 text-xl">⚠️</span>
        <div className="min-w-0 flex-1">
          <div className="font-bold text-red-800">Çıxış qeyd olunmayıb</div>
          <div className="mt-0.5 text-sm text-red-700">
            {fmtDayMonth(status.openDay.attendanceDate)} — çıxış etməyi unutmusunuz. Növbəti dəfə çıxışı skan etməyi
            unutmayın.
          </div>
        </div>
      </div>
    </div>
  )
}
