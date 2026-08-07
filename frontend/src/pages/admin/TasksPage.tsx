import { useEffect, useMemo, useState } from 'react'
import { createTask, deleteTask, getTasks, toggleTask, toggleTaskImportant, type TaskRow } from '../../api/tasks'

/**
 * Shared "Tapşırıqlar" board for the operator team (Ruslan / Ənvər / Çingiz). One global list — whoever
 * adds a task, everyone allowlisted sees it. Redesigned to feel like Microsoft To Do: round check
 * circles, a star that pins important items to the top, and a quiet, spacious layout.
 */
export function TasksPage() {
  const [tasks, setTasks] = useState<TaskRow[]>([])
  const [title, setTitle] = useState('')
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)
  const [showDone, setShowDone] = useState(false)

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
    setTasks((ts) => ts.map((x) => (x.id === id ? { ...x, isDone: !x.isDone } : x)))
    await toggleTask(id)
    void load()
  }

  async function star(id: string) {
    setTasks((ts) => ts.map((x) => (x.id === id ? { ...x, isImportant: !x.isImportant } : x)))
    await toggleTaskImportant(id)
    void load()
  }

  async function remove(id: string) {
    setTasks((ts) => ts.filter((x) => x.id !== id))
    await deleteTask(id)
  }

  const open = useMemo(() => tasks.filter((t) => !t.isDone), [tasks])
  const done = useMemo(() => tasks.filter((t) => t.isDone), [tasks])
  const today = new Date().toLocaleDateString('az-AZ', { weekday: 'long', day: 'numeric', month: 'long' })

  return (
    <div className="td">
      <style>{TD_CSS}</style>

      <div className="td-head">
        <div className="td-title">Tapşırıqlar</div>
        <div className="td-sub">{today[0].toUpperCase() + today.slice(1)} · {open.length} açıq</div>
      </div>

      {/* Add box (Microsoft To Do's "＋ Tapşırıq əlavə et") */}
      <div className="td-add">
        <span className="td-plus">＋</span>
        <input
          className="td-add-input"
          placeholder="Tapşırıq əlavə et"
          value={title}
          onChange={(e) => setTitle(e.target.value)}
          onKeyDown={(e) => { if (e.key === 'Enter') void add() }}
        />
        {title.trim() && (
          <button className="td-add-btn" disabled={busy} onClick={() => void add()}>Əlavə et</button>
        )}
      </div>

      {loading ? (
        <div className="td-empty">Yüklənir…</div>
      ) : (
        <>
          {open.length === 0 ? (
            <div className="td-empty">
              <div style={{ fontSize: 30 }}>✓</div>
              <div style={{ fontWeight: 600, marginTop: 4 }}>Hər şey bitib</div>
              <div className="td-muted" style={{ fontSize: 13 }}>Açıq tapşırıq yoxdur.</div>
            </div>
          ) : (
            <div className="td-list">
              {open.map((t) => (
                <Row key={t.id} t={t} onToggle={toggle} onStar={star} onDelete={remove} />
              ))}
            </div>
          )}

          {done.length > 0 && (
            <div className="td-donewrap">
              <button className="td-done-h" onClick={() => setShowDone((s) => !s)}>
                <span className={`td-chev ${showDone ? 'open' : ''}`}>▸</span>
                Tamamlandı <span className="td-count">{done.length}</span>
              </button>
              {showDone && (
                <div className="td-list">
                  {done.map((t) => (
                    <Row key={t.id} t={t} onToggle={toggle} onStar={star} onDelete={remove} />
                  ))}
                </div>
              )}
            </div>
          )}
        </>
      )}
    </div>
  )
}

function Row({
  t, onToggle, onStar, onDelete,
}: {
  t: TaskRow
  onToggle: (id: string) => void
  onStar: (id: string) => void
  onDelete: (id: string) => void
}) {
  return (
    <div className={`td-row ${t.isDone ? 'done' : ''}`}>
      <button className={`td-check ${t.isDone ? 'on' : ''}`} onClick={() => onToggle(t.id)} aria-label="Tamamla">
        <svg viewBox="0 0 24 24" width="14" height="14"><path d="M5 12.5l4.5 4.5L19 7" fill="none" stroke="currentColor" strokeWidth="2.6" strokeLinecap="round" strokeLinejoin="round" /></svg>
      </button>

      <div className="td-body">
        <div className="td-text">{t.title}</div>
        <div className="td-meta">{t.by} · {new Date(t.at).toLocaleDateString('az-AZ')}</div>
      </div>

      <button className={`td-star ${t.isImportant ? 'on' : ''}`} onClick={() => onStar(t.id)} aria-label="Önəmli">
        <svg viewBox="0 0 24 24" width="18" height="18">
          <path d="M12 3.5l2.6 5.3 5.9.9-4.3 4.1 1 5.8L12 17l-5.2 2.7 1-5.8L3.5 9.7l5.9-.9L12 3.5z"
            fill={t.isImportant ? 'currentColor' : 'none'} stroke="currentColor" strokeWidth="1.6" strokeLinejoin="round" />
        </svg>
      </button>

      <button className="td-del" onClick={() => onDelete(t.id)} aria-label="Sil">
        <svg viewBox="0 0 24 24" width="16" height="16"><path d="M4 7h16M9 7V5h6v2M6 7l1 13h10l1-13" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" /></svg>
      </button>
    </div>
  )
}

const TD_CSS = `
.td { max-width: 640px; margin: 0 auto; --td-accent:#2564cf; --td-gold:#f5b800;
      --td-bg:#fff; --td-text:#1b1f24; --td-muted:#6b7480; --td-line:#eceef1; --td-row:#fff; --td-hover:#f5f7fa; }
@media (prefers-color-scheme: dark){ .td{ --td-bg:#1c2230; --td-text:#e8ecf2; --td-muted:#9aa6b5; --td-line:#2b3342; --td-row:#232b3a; --td-hover:#2a3344; } }
:root[data-theme=dark] .td{ --td-bg:#1c2230; --td-text:#e8ecf2; --td-muted:#9aa6b5; --td-line:#2b3342; --td-row:#232b3a; --td-hover:#2a3344; }
:root[data-theme=light] .td{ --td-bg:#fff; --td-text:#1b1f24; --td-muted:#6b7480; --td-line:#eceef1; --td-row:#fff; --td-hover:#f5f7fa; }
.td-muted{ color:var(--td-muted); }

.td-head{ margin-bottom:16px; }
.td-title{ font-family:'Manrope',sans-serif; font-weight:800; font-size:26px; color:var(--td-text); letter-spacing:-.01em; }
.td-sub{ color:var(--td-muted); font-size:13.5px; margin-top:2px; }

.td-add{ display:flex; align-items:center; gap:10px; background:var(--td-row); border:1px solid var(--td-line);
         border-radius:12px; padding:0 12px; height:52px; box-shadow:0 1px 2px rgba(0,0,0,.03); margin-bottom:14px; }
.td-plus{ color:var(--td-accent); font-size:20px; font-weight:700; line-height:1; }
.td-add-input{ flex:1; border:none; outline:none; background:transparent; font-size:15px; color:var(--td-text); font-family:inherit; }
.td-add-input::placeholder{ color:var(--td-muted); }
.td-add-btn{ border:none; background:var(--td-accent); color:#fff; font-weight:700; font-size:13px; border-radius:8px; padding:7px 14px; cursor:pointer; }

.td-list{ background:var(--td-row); border:1px solid var(--td-line); border-radius:12px; overflow:hidden; box-shadow:0 1px 2px rgba(0,0,0,.03); }
.td-row{ display:flex; align-items:center; gap:12px; padding:12px 14px; border-bottom:1px solid var(--td-line); transition:background .12s; }
.td-row:last-child{ border-bottom:none; }
.td-row:hover{ background:var(--td-hover); }
.td-row:hover .td-del{ opacity:.6; }

.td-check{ flex:none; width:22px; height:22px; border-radius:50%; border:1.6px solid var(--td-muted); background:transparent;
           display:grid; place-items:center; cursor:pointer; color:transparent; transition:all .15s; padding:0; }
.td-check:hover{ border-color:var(--td-accent); color:var(--td-accent); }
.td-check.on{ background:var(--td-accent); border-color:var(--td-accent); color:#fff; }

.td-body{ flex:1; min-width:0; }
.td-text{ font-size:15px; color:var(--td-text); word-break:break-word; line-height:1.3; }
.td-meta{ font-size:11.5px; color:var(--td-muted); margin-top:2px; }
.td-row.done .td-text{ text-decoration:line-through; color:var(--td-muted); }

.td-star{ flex:none; border:none; background:transparent; color:var(--td-muted); cursor:pointer; padding:4px; border-radius:6px; display:grid; place-items:center; transition:color .12s; }
.td-star:hover{ color:var(--td-gold); }
.td-star.on{ color:var(--td-gold); }

.td-del{ flex:none; border:none; background:transparent; color:var(--td-muted); cursor:pointer; padding:4px; border-radius:6px; opacity:0; transition:opacity .12s,color .12s; display:grid; place-items:center; }
.td-del:hover{ opacity:1 !important; color:#e5484d; }

.td-empty{ text-align:center; color:var(--td-muted); padding:40px 16px; background:var(--td-row); border:1px solid var(--td-line); border-radius:12px; }

.td-donewrap{ margin-top:16px; }
.td-done-h{ display:flex; align-items:center; gap:8px; border:none; background:transparent; color:var(--td-text); font-weight:700; font-size:13.5px; cursor:pointer; padding:6px 4px; margin-bottom:6px; }
.td-chev{ display:inline-block; transition:transform .15s; color:var(--td-muted); }
.td-chev.open{ transform:rotate(90deg); }
.td-count{ color:var(--td-muted); font-weight:600; }
`
