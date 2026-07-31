import { useEffect, useState } from 'react'
import { createTask, deleteTask, getTasks, toggleTask, type TaskRow } from '../../api/tasks'
import { IconTrash } from '../../components/icons'

/** Shared "Tapşırıqlar" board for the operator team (Ruslan / Ənvər / Çingiz). One global list —
 *  whoever adds a task, everyone allowlisted sees it. Access is gated server-side by an id allowlist. */
export function TasksPage() {
  const [tasks, setTasks] = useState<TaskRow[]>([])
  const [title, setTitle] = useState('')
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)

  async function load() {
    const { status, data } = await getTasks()
    if (status === 200 && Array.isArray(data)) setTasks(data)
    setLoading(false)
  }
  useEffect(() => { void load() }, [])

  async function add() {
    const t = title.trim()
    if (!t || busy) return
    setBusy(true)
    const { status } = await createTask(t)
    setBusy(false)
    if (status === 200) { setTitle(''); void load() }
  }

  async function toggle(id: string) {
    setTasks((ts) => ts.map((x) => (x.id === id ? { ...x, isDone: !x.isDone } : x))) // optimistic
    await toggleTask(id)
    void load()
  }

  async function remove(id: string) {
    setTasks((ts) => ts.filter((x) => x.id !== id))
    await deleteTask(id)
  }

  const open = tasks.filter((t) => !t.isDone)
  const done = tasks.filter((t) => t.isDone)

  return (
    <div className="page">
      <div className="card">
        <div style={{ display: 'flex', gap: 8 }}>
          <input
            className="inp"
            style={{ flex: 1 }}
            placeholder="Yeni tapşırıq yaz…"
            value={title}
            onChange={(e) => setTitle(e.target.value)}
            onKeyDown={(e) => { if (e.key === 'Enter') void add() }}
          />
          <button className="btn btn-primary" disabled={busy || !title.trim()} onClick={() => void add()}>
            Əlavə et
          </button>
        </div>
      </div>

      {loading ? (
        <div className="muted" style={{ padding: 16 }}>Yüklənir…</div>
      ) : (
        <>
          <div className="card">
            {open.length === 0 ? (
              <div className="muted" style={{ padding: 8 }}>Açıq tapşırıq yoxdur 🎉</div>
            ) : (
              open.map((t) => <Row key={t.id} t={t} onToggle={toggle} onDelete={remove} />)
            )}
          </div>

          {done.length > 0 && (
            <div className="card">
              <div className="muted" style={{ marginBottom: 6, fontSize: 13, fontWeight: 600 }}>
                Bitmiş ({done.length})
              </div>
              {done.map((t) => <Row key={t.id} t={t} onToggle={toggle} onDelete={remove} />)}
            </div>
          )}
        </>
      )}
    </div>
  )
}

function Row({ t, onToggle, onDelete }: { t: TaskRow; onToggle: (id: string) => void; onDelete: (id: string) => void }) {
  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: 12, padding: '10px 2px', borderBottom: '1px solid var(--line)' }}>
      <input
        type="checkbox"
        checked={t.isDone}
        onChange={() => onToggle(t.id)}
        style={{ width: 20, height: 20, flexShrink: 0, cursor: 'pointer' }}
      />
      <div style={{ flex: 1, minWidth: 0, textDecoration: t.isDone ? 'line-through' : 'none', opacity: t.isDone ? 0.55 : 1 }}>
        <div style={{ wordBreak: 'break-word' }}>{t.title}</div>
        <div className="muted" style={{ fontSize: 12, marginTop: 2 }}>
          {t.by} · {new Date(t.at).toLocaleDateString('az-AZ')}
        </div>
      </div>
      <button className="btn btn-sm" onClick={() => onDelete(t.id)} title="Sil" style={{ flexShrink: 0 }}>
        <IconTrash />
      </button>
    </div>
  )
}
