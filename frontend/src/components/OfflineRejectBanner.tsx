import { useEffect, useState } from 'react'
import { clearRejects, readRejects, REJECTS_CHANGED, type OfflineReject } from '../lib/offlineRejects'
import { fmtDateTime } from '../lib/format'

/**
 * Tells the employee that a scan they believe was saved did NOT become a record.
 *
 * This is the visible half of the offline queue's honesty: at tap time, with no connection, the app
 * shows a green "yadda saxlanıldı" card, because from the employee's side the day IS recorded. If the
 * replay is then refused — or the item sat past the window in which it could be replayed on the right
 * day — the record is gone. Without this they simply appear absent, and the first anyone hears of it
 * is a payslip argument at the end of the month.
 *
 * Deliberately dismissible and non-blocking: it cannot fix anything by itself, and the same event is
 * already on the admin's Problems screen. Its job is to reach the person in time to say something.
 */
const WHY: Record<OfflineReject['kind'], string> = {
  OfflineRejected: 'Server qəbul etmədi',
  OfflineExpired: 'Çox gec göndərildi',
}

/** The server codes an employee can actually act on; anything else stays generic on purpose. */
const CODE_TEXT: Record<string, string> = {
  OutsideRadius: 'skan iş yerindən kənarda olub',
  AlreadyCompleted: 'həmin gün artıq bağlanıb',
  TokenExpired: 'QR kod yenilənib',
  EmployeeNotFoundOrInactive: 'hesab aktiv deyil',
}

export function OfflineRejectBanner() {
  const [items, setItems] = useState<OfflineReject[]>(() => readRejects())

  useEffect(() => {
    const refresh = () => setItems(readRejects())
    window.addEventListener(REJECTS_CHANGED, refresh)
    return () => window.removeEventListener(REJECTS_CHANGED, refresh)
  }, [])

  if (items.length === 0) return null

  return (
    <div className="rounded-3xl border border-red-200 bg-red-50 p-4">
      <div className="font-bold text-red-900">
        ⚠️ {items.length === 1 ? 'Bir skanınız qeydə düşmədi' : `${items.length} skanınız qeydə düşmədi`}
      </div>
      <p className="mt-1 text-sm text-red-800">
        Oflayn saxlanmışdı, amma göndəriləndə qəbul edilmədi. Rəhbərinizə deyin — həmin gün əl ilə
        düzəldilə bilər.
      </p>
      <ul className="mt-2 flex flex-col gap-1">
        {items.map((r) => (
          <li key={r.atMs} className="text-sm text-red-800">
            <span className="font-semibold">{fmtDateTime(r.scanAtIso)}</span> — {WHY[r.kind]}
            {r.code && CODE_TEXT[r.code] ? ` (${CODE_TEXT[r.code]})` : ''}
          </li>
        ))}
      </ul>
      <button
        onClick={() => clearRejects()}
        className="mt-3 rounded-xl border border-red-200 bg-white px-3 py-1.5 text-sm font-semibold text-red-700"
      >
        Anladım
      </button>
    </div>
  )
}
