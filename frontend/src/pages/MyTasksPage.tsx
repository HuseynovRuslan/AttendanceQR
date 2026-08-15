import { useEffect, useState } from 'react'
import { SubPageHeader } from '../components/SubPageHeader'
import { completeTask, getMyTasks, type EmployeeTask } from '../api/employeeTasks'
import { fmtDate } from '../lib/format'

/**
 * «Tapşırıqlarım» — the jobs a manager gave this worker, and the one button that closes them.
 *
 * Built for a cleaner or a driver holding a phone in one hand: the open work is at the top, the
 * finished work is out of the way, and «Hazırdır» is a single large button. The note and the photo
 * are offered but never demanded — a task must close in one tap when the worker has nothing to add.
 */
function dueLabel(t: EmployeeTask): { text: string; cls: string } | null {
  if (!t.dueDate || t.status !== 'Assigned') return null
  if (t.overdue) return { text: `Gecikib · ${fmtDate(t.dueDate)}`, cls: 'bg-red-100 text-red-700' }
  const today = new Date().toISOString().slice(0, 10)
  if (t.dueDate === today) return { text: 'Bu gün', cls: 'bg-amber-100 text-amber-800' }
  return { text: fmtDate(t.dueDate), cls: 'bg-slate-100 text-slate-600' }
}

export function MyTasksPage() {
  const [tasks, setTasks] = useState<EmployeeTask[]>([])
  const [loading, setLoading] = useState(true)
  const [openId, setOpenId] = useState<string | null>(null)
  const [note, setNote] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    void load()
  }, [])

  async function load() {
    setLoading(true)
    const r = await getMyTasks()
    if (r.status === 200 && Array.isArray(r.data)) setTasks(r.data)
    setLoading(false)
  }

  async function finish(id: string) {
    setBusy(true)
    setError(null)
    const { status, data } = await completeTask(id, note.trim() || null, null)
    setBusy(false)
    if (status === 200 && data && 'id' in data) {
      setTasks((ts) => ts.map((t) => (t.id === id ? data : t)))
      setOpenId(null)
      setNote('')
      return
    }
    setError('Göndərilmədi — internetinizi yoxlayıb yenidən cəhd edin.')
  }

  const open = tasks.filter((t) => t.status === 'Assigned')
  const finished = tasks.filter((t) => t.status !== 'Assigned')

  return (
    <div className="min-h-screen bg-slate-50">
      <SubPageHeader title="Tapşırıqlarım" />
      <main className="mx-auto max-w-md space-y-4 p-4 pb-16">
        {loading && <div className="text-center text-sm text-slate-400">Yüklənir…</div>}

        {!loading && open.length === 0 && (
          <div className="rounded-3xl border border-slate-100 bg-white p-6 text-center shadow-sm">
            <div className="text-4xl">✅</div>
            <div className="mt-2 font-bold text-slate-900">Açıq tapşırığınız yoxdur</div>
            <p className="mt-1 text-sm text-slate-500">Yeni tapşırıq veriləndə burada görünəcək.</p>
          </div>
        )}

        {open.map((t) => {
          const due = dueLabel(t)
          return (
            <div
              key={t.id}
              className={`rounded-3xl border bg-white p-5 shadow-sm ${t.overdue ? 'border-red-200' : 'border-slate-100'}`}
            >
              <div className="flex items-start justify-between gap-3">
                <div className="min-w-0">
                  <div className="font-bold text-slate-900">{t.title}</div>
                  {t.description && <p className="mt-1 text-sm text-slate-600">{t.description}</p>}
                </div>
                {due && (
                  <span className={`shrink-0 rounded-lg px-2 py-1 text-xs font-bold ${due.cls}`}>{due.text}</span>
                )}
              </div>

              {/* Sent back by the manager — the reason belongs on the task they must redo, not in a
                  notification they already swiped away. */}
              {t.rejectionNote && (
                <div className="mt-3 rounded-2xl bg-amber-50 p-3 text-sm text-amber-900">
                  ↩️ Geri qaytarıldı: {t.rejectionNote}
                </div>
              )}

              {openId === t.id ? (
                <div className="mt-4">
                  <textarea
                    value={note}
                    onChange={(e) => setNote(e.target.value)}
                    placeholder="Qeyd (istəyə bağlı)…"
                    rows={2}
                    className="w-full rounded-2xl border border-slate-200 bg-slate-50 p-3 text-sm outline-none focus:border-blue-400"
                  />
                  <button
                    onClick={() => void finish(t.id)}
                    disabled={busy}
                    className="mt-2 w-full rounded-2xl bg-green-600 py-3.5 text-base font-bold text-white disabled:opacity-50"
                  >
                    {busy ? 'Göndərilir…' : 'Təsdiqlə — Hazırdır'}
                  </button>
                  <button
                    onClick={() => { setOpenId(null); setNote('') }}
                    className="mt-1 w-full py-2 text-sm font-semibold text-slate-400"
                  >
                    Ləğv et
                  </button>
                </div>
              ) : (
                <button
                  onClick={() => { setOpenId(t.id); setNote('') }}
                  className="mt-4 w-full rounded-2xl bg-green-600 py-3.5 text-base font-bold text-white transition active:scale-[0.99]"
                >
                  Hazırdır ✓
                </button>
              )}
            </div>
          )
        })}

        {error && <div className="rounded-2xl bg-red-50 p-3 text-sm text-red-700">{error}</div>}

        {finished.length > 0 && (
          <>
            <div className="pt-2 text-xs font-bold uppercase tracking-wide text-slate-400">Bitmiş</div>
            {finished.map((t) => (
              <div key={t.id} className="rounded-2xl border border-slate-100 bg-white p-4 shadow-sm">
                <div className="flex items-center justify-between gap-2">
                  <span className="min-w-0 truncate text-sm font-semibold text-slate-500 line-through">{t.title}</span>
                  <span className="shrink-0 text-xs font-bold text-green-600">
                    {t.status === 'Approved' ? 'Qəbul edildi ✓' : 'Gözləyir'}
                  </span>
                </div>
              </div>
            ))}
          </>
        )}
      </main>
    </div>
  )
}
