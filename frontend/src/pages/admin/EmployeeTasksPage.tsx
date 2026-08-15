import { useEffect, useState } from 'react'
import {
  approveTask, assignTask, cancelTask, getTaskBoard, getTaskPhoto, rejectTask, type EmployeeTask,
} from '../../api/employeeTasks'
import { getEmployees, type AdminEmployee } from '../../api/admin'
import { fmtDate, fmtDateTime } from '../../lib/format'

/**
 * «İşçi tapşırıqları» — assign a job to a worker, then see it come back done.
 *
 * The board leads with what needs the manager's attention (Done, waiting to be accepted) rather than
 * with everything ever assigned: a list sorted by creation date is a log, and a log is not something
 * anyone opens twice.
 */
const STATUS_META: Record<string, { label: string; cls: string }> = {
  Assigned: { label: 'Gözləyir', cls: 'bg-slate-100 text-slate-600' },
  Done: { label: 'Hazırdır — təsdiq gözləyir', cls: 'bg-amber-100 text-amber-800' },
  Approved: { label: 'Qəbul edildi', cls: 'bg-green-100 text-green-700' },
  Cancelled: { label: 'Ləğv edilib', cls: 'bg-slate-100 text-slate-400' },
}

export function EmployeeTasksPage() {
  const [tasks, setTasks] = useState<EmployeeTask[]>([])
  const [employees, setEmployees] = useState<AdminEmployee[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [showForm, setShowForm] = useState(false)
  const [photoUrl, setPhotoUrl] = useState<string | null>(null)

  // The assign form.
  const [toEmployee, setToEmployee] = useState('')
  const [title, setTitle] = useState('')
  const [description, setDescription] = useState('')
  const [dueDate, setDueDate] = useState('')
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    void load()
  }, [])

  async function load() {
    setLoading(true)
    const [b, e] = await Promise.all([getTaskBoard(), getEmployees()])
    if (b.status === 200 && Array.isArray(b.data)) setTasks(b.data)
    else if (b.status === 403) setError('İcazəniz yoxdur')
    // Only plain employees can be assigned to — the server enforces this too, but offering a name
    // it will refuse is a worse experience than not offering it.
    if (e.status === 200 && Array.isArray(e.data)) setEmployees(e.data.filter((x) => x.role === 'Employee' && x.isActive))
    setLoading(false)
  }

  async function submit() {
    if (!toEmployee || !title.trim()) return
    setSaving(true)
    const { status, data } = await assignTask(toEmployee, title.trim(), description.trim() || null, dueDate || null)
    setSaving(false)
    if (status === 200 && data && 'id' in data) {
      setShowForm(false)
      setTitle(''); setDescription(''); setDueDate(''); setToEmployee('')
      void load()
      return
    }
    setError(status === 403 ? 'Bu işçiyə tapşırıq verə bilməzsiniz' : 'Tapşırıq yaradılmadı')
  }

  async function act(fn: () => Promise<{ status: number }>) {
    const { status } = await fn()
    if (status === 200) void load()
    else setError('Əməliyyat alınmadı')
  }

  async function showPhoto(id: string) {
    const { status, data } = await getTaskPhoto(id)
    if (status === 200 && data && 'url' in data && data.url) setPhotoUrl(data.url)
  }

  // Waiting-on-the-manager first, then open work, then the rest.
  const order: Record<string, number> = { Done: 0, Assigned: 1, Approved: 2, Cancelled: 3 }
  const sorted = [...tasks].sort((a, b) => (order[a.status] ?? 9) - (order[b.status] ?? 9))
  const waiting = tasks.filter((t) => t.status === 'Done').length
  const overdue = tasks.filter((t) => t.overdue).length

  return (
    <div>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-end', gap: 12, marginBottom: 14, flexWrap: 'wrap' }}>
        <div>
          <h1 style={{ fontSize: 18, fontWeight: 800, color: 'var(--c900)' }}>İşçi tapşırıqları</h1>
          <div className="muted" style={{ fontSize: 13 }}>
            İşçiyə tapşırıq verin — o, telefonundan «Hazırdır» deyəcək.
            {waiting > 0 && ` · ${waiting} təsdiq gözləyir`}
            {overdue > 0 && ` · ${overdue} gecikib`}
          </div>
        </div>
        <button className="btn" onClick={() => setShowForm((v) => !v)}>
          {showForm ? 'Bağla' : '+ Tapşırıq ver'}
        </button>
      </div>

      {showForm && (
        <div className="card" style={{ marginBottom: 16, display: 'grid', gap: 10 }}>
          <div>
            <label className="form-label">İşçi</label>
            <select className="inp" value={toEmployee} onChange={(e) => setToEmployee(e.target.value)}>
              <option value="">— seçin —</option>
              {employees.map((e) => (
                <option key={e.id} value={e.id}>{e.fullName}{e.locationName ? ` · ${e.locationName}` : ''}</option>
              ))}
            </select>
          </div>
          <div>
            <label className="form-label">Tapşırıq</label>
            <input className="inp" value={title} onChange={(e) => setTitle(e.target.value)} placeholder="Məs. Üçüncü mərtəbəni yığışdır" />
          </div>
          <div>
            <label className="form-label">İzah (istəyə bağlı)</label>
            <textarea className="inp" rows={2} value={description} onChange={(e) => setDescription(e.target.value)} />
          </div>
          <div>
            <label className="form-label">Son tarix (istəyə bağlı)</label>
            <input className="inp" type="date" value={dueDate} onChange={(e) => setDueDate(e.target.value)} />
          </div>
          <button className="btn" disabled={saving || !toEmployee || !title.trim()} onClick={() => void submit()}>
            {saving ? 'Göndərilir…' : 'Tapşırığı göndər'}
          </button>
        </div>
      )}

      {error && <div className="card" style={{ marginBottom: 12, color: 'var(--red)' }}>{error}</div>}
      {loading && <div className="muted">Yüklənir…</div>}

      {!loading && sorted.length === 0 && (
        <div className="card">Hələ tapşırıq yoxdur. «+ Tapşırıq ver» ilə başlayın.</div>
      )}

      <div style={{ display: 'grid', gap: 10 }}>
        {sorted.map((t) => {
          const meta = STATUS_META[t.status] ?? STATUS_META.Assigned
          return (
            <div key={t.id} className="card" style={{ display: 'grid', gap: 8 }}>
              <div style={{ display: 'flex', justifyContent: 'space-between', gap: 10, flexWrap: 'wrap' }}>
                <div style={{ minWidth: 0 }}>
                  <div style={{ fontWeight: 700, color: 'var(--c900)' }}>{t.title}</div>
                  <div className="muted" style={{ fontSize: 12 }}>
                    {t.employeeName ?? '—'}
                    {t.dueDate && ` · son tarix ${fmtDate(t.dueDate)}`}
                    {t.assignedByName && ` · təyin etdi: ${t.assignedByName}`}
                  </div>
                </div>
                <div style={{ display: 'flex', gap: 6, alignItems: 'flex-start' }}>
                  {t.overdue && <span className="pill" style={{ background: '#FEE2E2', color: '#B91C1C' }}>Gecikib</span>}
                  <span className={`pill ${meta.cls}`}>{meta.label}</span>
                </div>
              </div>

              {t.description && <div style={{ fontSize: 13, color: 'var(--c700)' }}>{t.description}</div>}
              {t.workerNote && (
                <div style={{ fontSize: 13, background: 'var(--c50)', padding: 8, borderRadius: 8 }}>
                  💬 İşçinin qeydi: {t.workerNote}
                </div>
              )}
              {t.doneAtUtc && <div className="muted" style={{ fontSize: 12 }}>Bitirdi: {fmtDateTime(t.doneAtUtc)}</div>}

              <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
                {t.hasPhoto && (
                  <button className="btn-sm" onClick={() => void showPhoto(t.id)}>📷 Şəkil</button>
                )}
                {t.status === 'Done' && (
                  <>
                    <button className="btn-sm" onClick={() => void act(() => approveTask(t.id))}>✓ Qəbul et</button>
                    <button
                      className="btn-sm"
                      onClick={() => {
                        const note = window.prompt('Niyə geri qaytarılır? (işçi bunu görəcək)')
                        if (note !== null) void act(() => rejectTask(t.id, note.trim() || null))
                      }}
                    >
                      ↩ Geri qaytar
                    </button>
                  </>
                )}
                {t.status === 'Assigned' && (
                  <button className="btn-sm" onClick={() => void act(() => cancelTask(t.id))}>Ləğv et</button>
                )}
              </div>
            </div>
          )
        })}
      </div>

      {photoUrl && (
        <div
          onClick={() => setPhotoUrl(null)}
          style={{ position: 'fixed', inset: 0, background: 'rgba(0,0,0,.7)', display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 50, padding: 20 }}
        >
          <img src={photoUrl} alt="Tapşırıq şəkli" style={{ maxWidth: '100%', maxHeight: '100%', borderRadius: 12 }} />
        </div>
      )}
    </div>
  )
}
