import { useEffect, useRef, useState } from 'react'
import { archivedScans, QUEUE_CHANGED, scansFor, type QueuedScan } from '../lib/offlineQueue'
import { syncOfflineScans } from '../lib/offlineSync'
import { fmtTime } from '../lib/format'
import { useAuth } from '../auth/AuthContext'

/**
 * The scans this phone is still holding, said where the employee looks first — and the moment they
 * reach the server.
 *
 * Until now a scan saved with no signal showed one green card at the poster and then disappeared from
 * view: the home screen, opened again with no signal, read «Bugünkü qeydiniz yüklənmədi» exactly as it
 * had before the scan. People could not tell whether they had clocked in, so they scanned again, or
 * phoned a manager, who reset their PIN and removed their phone — which fixed nothing and logged them
 * out (Balaxanı warehouse, 27.10.2026). The queue itself was never the problem; nobody could see it.
 *
 * So this says how many are waiting and from when, tells them not to scan again, and offers «İndi
 * göndər» for the moment they find a signal. When the last one leaves the queue it says so — but only
 * when the server actually took it: a refused one is OfflineRejectBanner's to explain, and announcing
 * «çatdı» over it would be the same false comfort this replaces.
 */
export function PendingScansBanner() {
  const { employeeId } = useAuth()
  const [queued, setQueued] = useState<QueuedScan[]>([])
  const [sending, setSending] = useState(false)
  const [note, setNote] = useState<string | null>(null)
  // Set when every scan that was waiting has been TAKEN by the server; fades after a few seconds.
  const [arrived, setArrived] = useState(false)
  // The ids that were waiting at the last look — what "they have left" is judged against.
  const waiting = useRef<string[]>([])

  useEffect(() => {
    let alive = true
    const refresh = () => {
      void (async () => {
        const now = await scansFor(employeeId).catch(() => null)
        if (!alive || now === null) return
        const before = waiting.current
        waiting.current = now.map((q) => q.clientScanId)
        setQueued(now)
        const left = before.filter((id) => !waiting.current.includes(id))
        if (left.length === 0 || now.length > 0) return
        const kept = await archivedScans(employeeId).catch(() => [])
        const taken = left.every((id) => {
          const k = kept.find((s) => s.clientScanId === id)
          return !k || k.outcome === 'sent' || k.outcome === 'ignored'
        })
        if (alive && taken) setArrived(true)
      })()
    }
    refresh()
    window.addEventListener(QUEUE_CHANGED, refresh)
    window.addEventListener('online', refresh)
    return () => {
      alive = false
      window.removeEventListener(QUEUE_CHANGED, refresh)
      window.removeEventListener('online', refresh)
    }
  }, [employeeId])

  useEffect(() => {
    if (!arrived) return
    const t = window.setTimeout(() => setArrived(false), 8_000)
    return () => window.clearTimeout(t)
  }, [arrived])

  async function sendNow() {
    setSending(true)
    setNote(null)
    // The same drain the heartbeat runs; it refuses to run twice at once, so this can never send a
    // scan in parallel with it.
    await syncOfflineScans().catch(() => undefined)
    const left = await scansFor(employeeId).catch(() => null)
    setSending(false)
    if (left && left.length > 0)
      setNote(typeof navigator !== 'undefined' && navigator.onLine === false
        ? 'İnternet yoxdur. İnternet olanda yenidən basın.'
        : 'Hələ göndərilmədi — internet zəifdir. Bir azdan yenidən basın.')
  }

  if (queued.length === 0) {
    if (!arrived) return null
    return (
      <div className="rounded-3xl border border-emerald-200 bg-emerald-50 p-4" role="status">
        <div className="font-bold text-emerald-900">✓ Skanınız serverə çatdı</div>
        <p className="mt-1 text-sm text-emerald-800">Qeydə alındı — rəhbəriniz sizi lövhədə görür.</p>
      </div>
    )
  }

  const times = queued.map((q) => fmtTime(q.clientTimestampUtc, '')).filter(Boolean).join(', ')
  return (
    <div className="rounded-3xl border border-amber-200 bg-amber-50 p-4" role="status">
      <div className="font-bold text-amber-900">
        📴 {queued.length === 1 ? 'Skanınız telefonda saxlanılıb' : `${queued.length} skanınız telefonda saxlanılıb`}
      </div>
      <p className="mt-1 text-sm text-amber-900">
        {times ? `Saat ${times} · ` : ''}hələ serverə çatmayıb. <b>Yenidən skan etməyin</b> — internet olan
        kimi özü göndəriləcək.
      </p>
      <button
        type="button"
        onClick={() => void sendNow()}
        disabled={sending}
        className="mt-3 rounded-xl bg-amber-600 px-4 py-2 text-sm font-bold text-white disabled:opacity-60"
      >
        {sending ? 'Göndərilir…' : 'İndi göndər'}
      </button>
      {note && <p className="mt-2 text-xs font-semibold text-amber-800">{note}</p>}
    </div>
  )
}
