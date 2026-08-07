import { useEffect, useMemo, useRef, useState } from 'react'
import {
  createTask, deleteTask, getTasks, toggleTask, toggleTaskImportant,
  renameTask, setTaskDue, reorderTasks, type TaskRow,
} from '../../api/tasks'

type Filter = 'today' | 'important' | 'planned' | 'all'

const LISTS: { key: Filter; label: string; icon: string }[] = [
  { key: 'today', label: 'Bugün', icon: '☀️' },
  { key: 'important', label: 'Önəmli', icon: '⭐' },
  { key: 'planned', label: 'Planlı', icon: '🗓️' },
  { key: 'all', label: 'Bütün tapşırıqlar', icon: '📋' },
]

const iso = (d: Date) => d.toISOString().slice(0, 10)
function dueInfo(due: string | null): { label: string; tone: 'today' | 'over' | 'soon' | 'future' } | null {
  if (!due) return null
  const today = iso(new Date())
  const tmrw = iso(new Date(Date.now() + 864e5))
  if (due < today) return { label: 'Gecikib', tone: 'over' }
  if (due === today) return { label: 'Bu gün', tone: 'today' }
  if (due === tmrw) return { label: 'Sabah', tone: 'soon' }
  return { label: new Date(due + 'T00:00:00').toLocaleDateString('az-AZ', { day: 'numeric', month: 'short' }), tone: 'future' }
}

export function TasksPage() {
  const [tasks, setTasks] = useState<TaskRow[]>([])
  const [filter, setFilter] = useState<Filter>('all')
  const [title, setTitle] = useState('')
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)
  const [showDone, setShowDone] = useState(false)
  const [editing, setEditing] = useState<{ id: string; text: string } | null>(null)
  const [menu, setMenu] = useState<{ id: string; x: number; y: number } | null>(null)
  const dragId = useRef<string | null>(null)
  const dateRef = useRef<HTMLInputElement>(null)

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
    await toggleTask(id); void load()
  }
  async function star(id: string) {
    setTasks((ts) => ts.map((x) => (x.id === id ? { ...x, isImportant: !x.isImportant } : x)))
    await toggleTaskImportant(id); void load()
  }
  async function remove(id: string) {
    setTasks((ts) => ts.filter((x) => x.id !== id)); setMenu(null)
    await deleteTask(id)
  }
  async function due(id: string, d: string | null) {
    setTasks((ts) => ts.map((x) => (x.id === id ? { ...x, dueDate: d } : x))); setMenu(null)
    await setTaskDue(id, d); void load()
  }
  async function commitEdit() {
    if (!editing) return
    const text = editing.text.trim()
    const cur = tasks.find((t) => t.id === editing.id)
    setEditing(null)
    if (text && cur && text !== cur.title) {
      setTasks((ts) => ts.map((x) => (x.id === cur.id ? { ...x, title: text } : x)))
      await renameTask(cur.id, text)
    }
  }

  // ---- drag reorder (only meaningful in the manual "Bütün tapşırıqlar" list) ----
  async function onDrop(targetId: string) {
    const from = dragId.current
    dragId.current = null
    if (!from || from === targetId) return
    const openIds = tasks.filter((t) => !t.isDone).map((t) => t.id)
    const a = openIds.indexOf(from), b = openIds.indexOf(targetId)
    if (a < 0 || b < 0) return
    openIds.splice(b, 0, ...openIds.splice(a, 1))
    // optimistic reorder
    const byId = new Map(tasks.map((t) => [t.id, t]))
    setTasks([...openIds.map((id) => byId.get(id)!), ...tasks.filter((t) => t.isDone)])
    await reorderTasks(openIds)
  }

  const openAll = useMemo(() => tasks.filter((t) => !t.isDone), [tasks])
  const doneAll = useMemo(() => tasks.filter((t) => t.isDone), [tasks])
  const today = iso(new Date())
  const match = (t: TaskRow) =>
    filter === 'today' ? t.dueDate === today
      : filter === 'important' ? t.isImportant
        : filter === 'planned' ? !!t.dueDate
          : true
  const open = openAll.filter(match)
  const done = doneAll.filter(match)
  const listMeta = LISTS.find((l) => l.key === filter)!
  const counts = {
    today: openAll.filter((t) => t.dueDate === today).length,
    important: openAll.filter((t) => t.isImportant).length,
    planned: openAll.filter((t) => t.dueDate).length,
    all: openAll.length,
  }

  return (
    <div className="tdx" onClick={() => menu && setMenu(null)}>
      <style>{CSS}</style>

      <aside className="tdx-side">
        {LISTS.map((l) => (
          <button key={l.key} className={`tdx-nav ${filter === l.key ? 'on' : ''}`} onClick={() => setFilter(l.key)}>
            <span className="tdx-ico">{l.icon}</span>
            <span className="tdx-nav-l">{l.label}</span>
            {counts[l.key] > 0 && <span className="tdx-badge">{counts[l.key]}</span>}
          </button>
        ))}
      </aside>

      <main className="tdx-main">
        <div className="tdx-h">
          <span className="tdx-ico-lg">{listMeta.icon}</span>
          <div>
            <div className="tdx-title">{listMeta.label}</div>
            <div className="tdx-date">{new Date().toLocaleDateString('az-AZ', { weekday: 'long', day: 'numeric', month: 'long' }).replace(/^./, (c) => c.toUpperCase())}</div>
          </div>
        </div>

        <div className="tdx-add">
          <span className="tdx-plus">＋</span>
          <input className="tdx-add-in" placeholder="Tapşırıq əlavə et" value={title}
            onChange={(e) => setTitle(e.target.value)} onKeyDown={(e) => { if (e.key === 'Enter') void add() }} />
          {title.trim() && <button className="tdx-add-btn" disabled={busy} onClick={() => void add()}>Əlavə et</button>}
        </div>

        {loading ? (
          <div className="tdx-empty">Yüklənir…</div>
        ) : open.length === 0 && done.length === 0 ? (
          <div className="tdx-empty"><div style={{ fontSize: 30 }}>{listMeta.icon}</div><div style={{ fontWeight: 600, marginTop: 6 }}>Boşdur</div><div className="tdx-mut" style={{ fontSize: 13 }}>Bu siyahıda tapşırıq yoxdur.</div></div>
        ) : (
          <>
            <div className="tdx-list">
              {open.map((t) => (
                <Row key={t.id} t={t}
                  editing={editing?.id === t.id ? editing.text : null}
                  onEditStart={() => setEditing({ id: t.id, text: t.title })}
                  onEditChange={(v) => setEditing({ id: t.id, text: v })}
                  onEditCommit={commitEdit}
                  onToggle={toggle} onStar={star}
                  onMenu={(x, y) => setMenu({ id: t.id, x, y })}
                  draggable={filter === 'all'}
                  onDragStart={() => (dragId.current = t.id)}
                  onDropRow={() => void onDrop(t.id)}
                />
              ))}
            </div>

            {done.length > 0 && (
              <div className="tdx-donewrap">
                <button className="tdx-done-h" onClick={() => setShowDone((s) => !s)}>
                  <span className={`tdx-chev ${showDone ? 'open' : ''}`}>▸</span> Tamamlandı <span className="tdx-mut">{done.length}</span>
                </button>
                {showDone && (
                  <div className="tdx-list">
                    {done.map((t) => (
                      <Row key={t.id} t={t} editing={null}
                        onEditStart={() => {}} onEditChange={() => {}} onEditCommit={() => {}}
                        onToggle={toggle} onStar={star} onMenu={(x, y) => setMenu({ id: t.id, x, y })}
                        draggable={false} onDragStart={() => {}} onDropRow={() => {}} />
                    ))}
                  </div>
                )}
              </div>
            )}
          </>
        )}
      </main>

      {menu && (() => {
        const t = tasks.find((x) => x.id === menu.id)
        if (!t) return null
        return (
          <div className="tdx-menu" style={{ top: menu.y, left: menu.x }} onClick={(e) => e.stopPropagation()}>
            <button className="tdx-mi" onClick={() => { void star(t.id); setMenu(null) }}>
              <span>⭐</span> {t.isImportant ? 'Önəmlidən çıxar' : 'Önəmli et'}
            </button>
            <button className="tdx-mi" onClick={() => { void toggle(t.id); setMenu(null) }}>
              <span>✓</span> {t.isDone ? 'Bərpa et' : 'Tamamla'}
            </button>
            <div className="tdx-sep" />
            <button className="tdx-mi" onClick={() => void due(t.id, today)}><span>📅</span> Bu gün</button>
            <button className="tdx-mi" onClick={() => void due(t.id, iso(new Date(Date.now() + 864e5)))}><span>➡️</span> Sabah</button>
            {/* A bare date <input> won't open its picker on a label click, so trigger it programmatically.
                showPicker() needs a user gesture (this click) and a not-display:none input (it's kept
                on-screen but transparent). */}
            <button
              className="tdx-mi"
              onClick={(e) => {
                e.stopPropagation()
                const el = dateRef.current
                if (!el) return
                el.value = t.dueDate ?? ''
                const anyEl = el as HTMLInputElement & { showPicker?: () => void }
                if (typeof anyEl.showPicker === 'function') {
                  try { anyEl.showPicker() } catch { el.focus() }
                } else {
                  el.focus()
                }
              }}
            >
              <span>🗓️</span> Tarix seç
            </button>
            <input
              ref={dateRef}
              type="date"
              onChange={(e) => { if (e.target.value) void due(t.id, e.target.value) }}
              style={{ position: 'absolute', left: 12, bottom: 8, width: 1, height: 1, opacity: 0, pointerEvents: 'none' }}
            />
            {t.dueDate && <button className="tdx-mi" onClick={() => void due(t.id, null)}><span>✖️</span> Vaxtı sil</button>}
            <div className="tdx-sep" />
            <button className="tdx-mi danger" onClick={() => void remove(t.id)}><span>🗑️</span> Sil</button>
          </div>
        )
      })()}
    </div>
  )
}

function Row({
  t, editing, onEditStart, onEditChange, onEditCommit, onToggle, onStar, onMenu, draggable, onDragStart, onDropRow,
}: {
  t: TaskRow
  editing: string | null
  onEditStart: () => void
  onEditChange: (v: string) => void
  onEditCommit: () => void
  onToggle: (id: string) => void
  onStar: (id: string) => void
  onMenu: (x: number, y: number) => void
  draggable: boolean
  onDragStart: () => void
  onDropRow: () => void
}) {
  const d = dueInfo(t.dueDate)
  return (
    <div
      className={`tdx-row ${t.isDone ? 'done' : ''}`}
      draggable={draggable && editing === null}
      onDragStart={onDragStart}
      onDragOver={(e) => draggable && e.preventDefault()}
      onDrop={onDropRow}
      onContextMenu={(e) => { e.preventDefault(); onMenu(e.clientX, e.clientY) }}
    >
      {draggable && <span className="tdx-grip" title="Sürüklə">≡</span>}
      <button className={`tdx-check ${t.isDone ? 'on' : ''}`} onClick={() => onToggle(t.id)} aria-label="Tamamla">
        <svg viewBox="0 0 24 24" width="14" height="14"><path d="M5 12.5l4.5 4.5L19 7" fill="none" stroke="currentColor" strokeWidth="2.6" strokeLinecap="round" strokeLinejoin="round" /></svg>
      </button>
      <div className="tdx-body">
        {editing !== null ? (
          <input className="tdx-edit" autoFocus value={editing}
            onChange={(e) => onEditChange(e.target.value)} onBlur={onEditCommit}
            onKeyDown={(e) => { if (e.key === 'Enter') onEditCommit(); if (e.key === 'Escape') onEditCommit() }} />
        ) : (
          <div className="tdx-text" onClick={onEditStart} title="Redaktə et">{t.title}</div>
        )}
        <div className="tdx-meta">
          {d && <span className={`tdx-due ${d.tone}`}>📅 {d.label}</span>}
          <span className="tdx-mut">{t.by}</span>
        </div>
      </div>
      <button className={`tdx-star ${t.isImportant ? 'on' : ''}`} onClick={() => onStar(t.id)} aria-label="Önəmli">
        <svg viewBox="0 0 24 24" width="18" height="18"><path d="M12 3.5l2.6 5.3 5.9.9-4.3 4.1 1 5.8L12 17l-5.2 2.7 1-5.8L3.5 9.7l5.9-.9L12 3.5z" fill={t.isImportant ? 'currentColor' : 'none'} stroke="currentColor" strokeWidth="1.6" strokeLinejoin="round" /></svg>
      </button>
    </div>
  )
}

const CSS = `
.tdx{ display:flex; gap:18px; align-items:flex-start; }
.tdx-mut{ color:var(--c400); }

.tdx-side{ flex:none; width:210px; background:var(--white); border:1px solid var(--c100); border-radius:var(--r-lg); padding:8px; box-shadow:var(--sh-sm); }
.tdx-nav{ display:flex; align-items:center; gap:10px; width:100%; text-align:left; border:none; background:transparent; border-radius:8px; padding:9px 10px; font-size:14px; font-weight:600; color:var(--c700); cursor:pointer; }
.tdx-nav:hover{ background:var(--sand); }
.tdx-nav.on{ background:color-mix(in srgb, var(--leaf) 14%, transparent); color:var(--leaf-d, var(--leaf)); }
.tdx-ico{ font-size:16px; width:20px; text-align:center; }
.tdx-nav-l{ flex:1; }
.tdx-badge{ font-size:12px; font-weight:700; color:var(--c400); }

.tdx-main{ flex:1; min-width:0; }
.tdx-h{ display:flex; align-items:center; gap:12px; margin-bottom:14px; }
.tdx-ico-lg{ font-size:26px; }
.tdx-title{ font-family:'Manrope',sans-serif; font-weight:800; font-size:24px; color:var(--c900); letter-spacing:-.01em; }
.tdx-date{ font-size:13px; color:var(--c400); margin-top:1px; }

.tdx-add{ display:flex; align-items:center; gap:10px; background:var(--white); border:1px solid var(--c100); border-radius:12px; padding:0 12px; height:50px; box-shadow:var(--sh-sm); margin-bottom:14px; }
.tdx-plus{ color:var(--leaf-d, var(--leaf)); font-size:19px; font-weight:700; }
.tdx-add-in{ flex:1; border:none; outline:none; background:transparent; font-size:15px; color:var(--c900); font-family:inherit; }
.tdx-add-in::placeholder{ color:var(--c400); }
.tdx-add-btn{ border:none; background:var(--leaf); color:#fff; font-weight:700; font-size:13px; border-radius:8px; padding:7px 14px; cursor:pointer; }

.tdx-list{ background:var(--white); border:1px solid var(--c100); border-radius:12px; overflow:hidden; box-shadow:var(--sh-sm); }
.tdx-row{ display:flex; align-items:center; gap:11px; padding:11px 12px 11px 8px; border-bottom:1px solid var(--c50); transition:background .12s; }
.tdx-row:last-child{ border-bottom:none; }
.tdx-row:hover{ background:var(--sand); }
.tdx-row:hover .tdx-grip{ opacity:.5; }
.tdx-grip{ flex:none; width:14px; text-align:center; color:var(--c400); cursor:grab; opacity:0; transition:opacity .12s; user-select:none; font-size:15px; }
.tdx-check{ flex:none; width:22px; height:22px; border-radius:50%; border:1.6px solid var(--c400); background:transparent; display:grid; place-items:center; cursor:pointer; color:transparent; transition:all .15s; padding:0; }
.tdx-check:hover{ border-color:var(--leaf); color:var(--leaf); }
.tdx-check.on{ background:var(--leaf); border-color:var(--leaf); color:#fff; }
.tdx-body{ flex:1; min-width:0; }
.tdx-text{ font-size:15px; color:var(--c900); word-break:break-word; line-height:1.3; cursor:text; }
.tdx-edit{ width:100%; border:1px solid var(--leaf); border-radius:6px; padding:4px 8px; font-size:15px; font-family:inherit; outline:none; color:var(--c900); background:var(--white); }
.tdx-meta{ display:flex; align-items:center; gap:8px; margin-top:3px; font-size:11.5px; }
.tdx-row.done .tdx-text{ text-decoration:line-through; color:var(--c400); }
.tdx-due{ font-weight:600; padding:1px 6px; border-radius:5px; background:var(--sand); color:var(--c500); }
.tdx-due.today{ background:color-mix(in srgb, var(--leaf) 16%, transparent); color:var(--leaf-d, var(--leaf)); }
.tdx-due.over{ background:rgba(200,60,40,.12); color:var(--clay,#c83c28); }
.tdx-due.soon{ color:var(--c700); }
.tdx-star{ flex:none; border:none; background:transparent; color:var(--c400); cursor:pointer; padding:4px; border-radius:6px; display:grid; place-items:center; }
.tdx-star:hover{ color:#f5b800; }
.tdx-star.on{ color:#f5b800; }

.tdx-empty{ text-align:center; color:var(--c400); padding:40px 16px; background:var(--white); border:1px solid var(--c100); border-radius:12px; }
.tdx-donewrap{ margin-top:16px; }
.tdx-done-h{ display:flex; align-items:center; gap:8px; border:none; background:transparent; color:var(--c900); font-weight:700; font-size:13.5px; cursor:pointer; padding:6px 4px; margin-bottom:6px; }
.tdx-chev{ display:inline-block; transition:transform .15s; color:var(--c400); }
.tdx-chev.open{ transform:rotate(90deg); }

.tdx-menu{ position:fixed; z-index:1200; min-width:210px; background:var(--white); border:1px solid var(--c100); border-radius:10px; box-shadow:0 12px 34px rgba(0,0,0,.18); padding:6px; }
.tdx-mi{ display:flex; align-items:center; gap:10px; width:100%; text-align:left; border:none; background:transparent; border-radius:7px; padding:8px 10px; font-size:13.5px; color:var(--c800,var(--c900)); cursor:pointer; position:relative; }
.tdx-mi:hover{ background:var(--sand); }
.tdx-mi span{ width:18px; text-align:center; }
.tdx-mi.danger{ color:var(--clay,#c83c28); }
.tdx-date-in{ position:absolute; inset:0; opacity:0; cursor:pointer; }
.tdx-sep{ height:1px; background:var(--c100); margin:5px 6px; }

@media (max-width:760px){
  .tdx{ flex-direction:column; }
  .tdx-side{ width:100%; display:flex; gap:6px; overflow-x:auto; padding:6px; }
  .tdx-nav{ width:auto; white-space:nowrap; }
  .tdx-nav-l{ flex:none; }
}
`
