import { useEffect, useState } from 'react'
import { useAuth } from '../auth/AuthContext'
import { archivedScans, QUEUE_CHANGED, scansFor, type ArchivedScan, type QueuedScan } from '../lib/offlineQueue'
import { fmtDateTime } from '../lib/format'
import { REJECT_CODE_TEXT } from '../lib/offlineRejects'

/**
 * Every scan this phone ever took with no connection — still waiting, sent, or refused — and what
 * became of it.
 *
 * The owner's rule (2026-09-18): an offline scan is never deleted. Kept is only half of it; the other
 * half is that the person who scanned can SEE it. «I scanned that morning» used to be a claim; with
 * this it is a line on their own phone, with the time and the server's answer, that they can show a
 * manager. Renders nothing for anybody who has never scanned offline — which is almost everybody.
 */
export function OfflineScanHistory() {
  const { employeeId } = useAuth()
  const [pending, setPending] = useState<QueuedScan[]>([])
  const [settled, setSettled] = useState<ArchivedScan[]>([])
  const [all, setAll] = useState(false)

  useEffect(() => {
    let alive = true
    const refresh = () => {
      void Promise.all([
        scansFor(employeeId).catch(() => [] as QueuedScan[]),
        archivedScans(employeeId).catch(() => [] as ArchivedScan[]),
      ]).then(([p, s]) => {
        if (!alive) return
        setPending(p)
        setSettled(s)
      })
    }
    refresh()
    window.addEventListener(QUEUE_CHANGED, refresh)
    return () => {
      alive = false
      window.removeEventListener(QUEUE_CHANGED, refresh)
    }
  }, [employeeId])

  if (pending.length === 0 && settled.length === 0) return null

  const rows: { id: string; at: string; text: string; tone: 'wait' | 'ok' | 'muted' | 'bad' }[] = [
    ...[...pending]
      .sort((a, b) => (a.clientTimestampUtc < b.clientTimestampUtc ? 1 : -1))
      .map((p) => ({ id: p.clientScanId, at: p.clientTimestampUtc, text: '📴 Göndərilməyi gözləyir', tone: 'wait' as const })),
    ...settled.map((s) => ({ id: s.clientScanId, at: s.clientTimestampUtc, ...verdict(s) })),
  ]
  const shown = all ? rows : rows.slice(0, 5)

  return (
    <div className="rounded-3xl border border-slate-100 bg-white p-5 shadow-sm">
      <div className="font-bold">📴 Oflayn skanlarım</div>
      <p className="mt-1 text-sm text-slate-500">
        İnternetsiz etdiyiniz skanlar telefonda saxlanılır və silinmir.
      </p>
      <ul className="mt-3 divide-y divide-slate-100">
        {shown.map((r) => (
          <li key={r.id} className="flex items-center justify-between gap-3 py-2.5 text-sm">
            <span className="shrink-0 font-semibold tabular-nums text-slate-700">{fmtDateTime(r.at)}</span>
            <span className={`text-right ${
              r.tone === 'ok' ? 'text-green-700' : r.tone === 'bad' ? 'text-red-700' : r.tone === 'wait' ? 'text-amber-700' : 'text-slate-500'
            }`}>
              {r.text}
            </span>
          </li>
        ))}
      </ul>
      {rows.length > 5 && (
        <button onClick={() => setAll((v) => !v)} className="mt-2 text-sm font-semibold text-blue-600">
          {all ? 'Daha az göstər' : `Hamısı (${rows.length})`}
        </button>
      )}
    </div>
  )
}

/** What became of one scan, in the words the person needs. */
function verdict(s: ArchivedScan): { text: string; tone: 'ok' | 'muted' | 'bad' } {
  switch (s.outcome) {
    case 'sent':
      return {
        text: s.action === 'CheckIn' ? '✓ Giriş kimi qeydə alındı' : s.action === 'CheckOut' ? '✓ Çıxış kimi qeydə alındı' : '✓ Göndərildi',
        tone: 'ok',
      }
    case 'ignored':
      return {
        text: s.code === 'ConfirmEarlyCheckOut' || s.code === 'TooSoonToCheckOut'
          ? 'Girişdən az sonra idi — çıxış sayılmadı'
          : 'Artıq qeydə alınmışdı',
        tone: 'muted',
      }
    case 'rejected':
      return { text: `✕ Qəbul edilmədi${s.code && REJECT_CODE_TEXT[s.code] ? ` — ${REJECT_CODE_TEXT[s.code]}` : ''}`, tone: 'bad' }
    case 'expired':
      return { text: '✕ Çox gec — rəhbərinizə bu sətri göstərin', tone: 'bad' }
  }
}
