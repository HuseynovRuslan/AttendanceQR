import { useEffect, useMemo, useRef, useState } from 'react'
import { useAuth } from '../../auth/AuthContext'
import {
  createTask, deleteTask, getTasks, toggleTask, toggleTaskImportant,
  renameTask, setTaskDue, reorderTasks, type TaskRow, assignTask, getAssignable, type Assignable } from '../../api/tasks'

type Filter = 'mine' | 'today' | 'important' | 'planned' | 'all'

const LISTS: { key: Filter; label: string; icon: string }[] = [
  // "Mine" first: on a board a whole team shares, the first question anyone opens it with is what
  // they themselves are on the hook for.
  { key: 'mine', label: 'Mənə aid', icon: '👤' },
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

const initialsOf = (name: string) =>
  name.split(' ').filter(Boolean).map((w) => w[0]).slice(0, 2).join('').toUpperCase()

export function TasksPage() {
  const { employeeId } = useAuth()
  // Who a task can be handed to. Loaded once — a company's admins and managers are a short list that
  // does not change while the board is open.
  const [people, setPeople] = useState<Assignable[]>([])
  const [tasks, setTasks] = useState<TaskRow[]>([])
  const [filter, setFilter] = useState<Filter>('all')
  const [title, setTitle] = useState('')
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)
  const [showDone, setShowDone] = useState(false)
  // The selected task drives the detail panel — the visible, tappable home of every per-task action.
  // The right-click menu still exists as a shortcut, but nothing lives ONLY there any more: a menu
  // behind right-click is invisible on touch and undiscovered on desktop.
  const [sel, setSel] = useState<string | null>(null)
  const [draftTitle, setDraftTitle] = useState('')
  // Person search in the panel. The assignable list is admins+managers, small today — but a big
  // tenant could have dozens, and a picker must not grow with the company.
  const [personQ, setPersonQ] = useState('')
  const [menu, setMenu] = useState<{ id: string; x: number; y: number } | null>(null)
  const dragId = useRef<string | null>(null)
  const dateRef = useRef<HTMLInputElement>(null)
  const detDateRef = useRef<HTMLInputElement>(null)

  async function load() {
    const { status, data } = await getTasks()
    if (status === 200 && Array.isArray(data)) setTasks(data)
    setLoading(false)
  }
  useEffect(() => { void load() }, [])

  useEffect(() => {
    void getAssignable().then((r) => {
      if (r.status === 200 && Array.isArray(r.data)) setPeople(r.data)
    })
  }, [])

  const selTask = sel ? tasks.find((t) => t.id === sel) ?? null : null
  // The panel's title field follows whichever task is open, without clobbering keystrokes: only a
  // CHANGE of selection re-seeds the draft.
  useEffect(() => { if (selTask) { setDraftTitle(selTask.title); setPersonQ('') } }, [sel]) // eslint-disable-line react-hooks/exhaustive-deps

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
    if (sel === id) setSel(null)
    await deleteTask(id)
  }
  async function assign(id: string, who: string | null) {
    const person = who ? people.find((p) => p.id === who) ?? null : null
    setTasks((ts) => ts.map((x) => (x.id === id
      ? { ...x, assignedToEmployeeId: person?.id ?? null, assignedToName: person?.name ?? null }
      : x)))
    setMenu(null)
    await assignTask(id, who)
  }

  async function due(id: string, d: string | null) {
    setTasks((ts) => ts.map((x) => (x.id === id ? { ...x, dueDate: d } : x))); setMenu(null)
    await setTaskDue(id, d); void load()
  }
  async function commitTitle() {
    if (!selTask) return
    const text = draftTitle.trim()
    if (!text || text === selTask.title) { setDraftTitle(selTask.title); return }
    setTasks((ts) => ts.map((x) => (x.id === selTask.id ? { ...x, title: text } : x)))
    await renameTask(selTask.id, text)
  }

  function openDatePicker(ref: React.RefObject<HTMLInputElement | null>, current: string | null) {
    const el = ref.current
    if (!el) return
    el.value = current ?? ''
    // showPicker() needs a user gesture and a not-display:none input (kept on-screen but transparent).
    const anyEl = el as HTMLInputElement & { showPicker?: () => void }
    if (typeof anyEl.showPicker === 'function') {
      try { anyEl.showPicker() } catch { el.focus() }
    } else {
      el.focus()
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
    filter === 'mine' ? t.assignedToEmployeeId === employeeId
      : filter === 'today' ? t.dueDate === today
        : filter === 'important' ? t.isImportant
          : filter === 'planned' ? !!t.dueDate
            : true
  const open = openAll.filter(match)
  const done = doneAll.filter(match)
  const listMeta = LISTS.find((l) => l.key === filter)!
  const counts = {
    mine: openAll.filter((t) => t.assignedToEmployeeId === employeeId).length,
    today: openAll.filter((t) => t.dueDate === today).length,
    important: openAll.filter((t) => t.isImportant).length,
    planned: openAll.filter((t) => t.dueDate).length,
    all: openAll.length,
  }
  const selDue = selTask ? dueInfo(selTask.dueDate) : null
  // Sorted so the two rows that matter are visible without scrolling: the current assignee, then me.
  // Azerbaijani casing (İ/i, I/ı) needs the locale-aware lowercase or "İlqar" never matches "ilqar".
  const az = (s: string) => s.toLocaleLowerCase('az')
  const shownPeople = selTask
    ? [...people]
        .sort((a, b) => {
          const rank = (p: Assignable) => (p.id === selTask.assignedToEmployeeId ? 0 : p.id === employeeId ? 1 : 2)
          return rank(a) - rank(b)
        })
        .filter((p) => !personQ.trim() || az(p.name).includes(az(personQ.trim())))
    : []

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
                <Row key={t.id} t={t} selected={sel === t.id}
                  onOpen={() => setSel(t.id)}
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
                      <Row key={t.id} t={t} selected={sel === t.id}
                        onOpen={() => setSel(t.id)}
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

      {/* ---- Detail panel: desktop = third column, phone = bottom sheet. Everything a task has,
           visible and tappable — this is the primary UI; the context menu is only a shortcut. ---- */}
      {selTask && (
        <>
          <div className="tdx-ovl" onClick={() => setSel(null)} />
          <aside className="tdx-det" onClick={(e) => e.stopPropagation()}>
            <div className="tdx-det-head">
              <button className={`tdx-check ${selTask.isDone ? 'on' : ''}`} onClick={() => void toggle(selTask.id)} aria-label="Tamamla">
                <svg viewBox="0 0 24 24" width="14" height="14"><path d="M5 12.5l4.5 4.5L19 7" fill="none" stroke="currentColor" strokeWidth="2.6" strokeLinecap="round" strokeLinejoin="round" /></svg>
              </button>
              <input className="tdx-det-title" value={draftTitle}
                onChange={(e) => setDraftTitle(e.target.value)}
                onBlur={() => void commitTitle()}
                onKeyDown={(e) => { if (e.key === 'Enter') (e.target as HTMLInputElement).blur() }} />
              <button className={`tdx-star ${selTask.isImportant ? 'on' : ''}`} onClick={() => void star(selTask.id)} aria-label="Önəmli">
                <svg viewBox="0 0 24 24" width="18" height="18"><path d="M12 3.5l2.6 5.3 5.9.9-4.3 4.1 1 5.8L12 17l-5.2 2.7 1-5.8L3.5 9.7l5.9-.9L12 3.5z" fill={selTask.isImportant ? 'currentColor' : 'none'} stroke="currentColor" strokeWidth="1.6" strokeLinejoin="round" /></svg>
              </button>
              <button className="tdx-det-x" onClick={() => setSel(null)} aria-label="Bağla">✕</button>
            </div>

            <div className="tdx-det-sec">
              <div className="tdx-det-lab">Kimə təyin et</div>
              {/* The search appears once the list stops fitting at a glance; below it the list itself
                  scrolls inside a fixed height. A hundred managers cost the same pixels as five. */}
              {people.length > 6 && (
                <input className="tdx-psearch" placeholder="Ad axtar…" value={personQ}
                  onChange={(e) => setPersonQ(e.target.value)} />
              )}
              <div className="tdx-plist">
                {shownPeople.map((p) => {
                  const on = selTask.assignedToEmployeeId === p.id
                  return (
                    /* Tapping the current assignee un-assigns — one control, both directions. */
                    <button key={p.id} className={`tdx-p ${on ? 'on' : ''}`} onClick={() => void assign(selTask.id, on ? null : p.id)}>
                      <span className="tdx-av">{initialsOf(p.name)}</span>
                      <span className="tdx-p-n">{p.name}{p.id === employeeId ? ' (mən)' : ''}</span>
                      {on && <span className="tdx-p-on">✓</span>}
                    </button>
                  )
                })}
                {shownPeople.length === 0 && <div className="tdx-mut" style={{ fontSize: 12.5, padding: '6px 8px' }}>Tapılmadı</div>}
              </div>
            </div>

            <div className="tdx-det-sec">
              <div className="tdx-det-lab">Son tarix</div>
              {selDue && (
                <div className={`tdx-det-due ${selDue.tone}`}>
                  📅 {selDue.label}
                  <button className="tdx-det-due-x" onClick={() => void due(selTask.id, null)} aria-label="Vaxtı sil">✕</button>
                </div>
              )}
              <div className="tdx-chips">
                <button className="tdx-chip" onClick={() => void due(selTask.id, today)}>Bu gün</button>
                <button className="tdx-chip" onClick={() => void due(selTask.id, iso(new Date(Date.now() + 864e5)))}>Sabah</button>
                <button className="tdx-chip" onClick={() => openDatePicker(detDateRef, selTask.dueDate)}>🗓️ Tarix seç</button>
              </div>
              <input ref={detDateRef} type="date"
                onChange={(e) => { if (e.target.value) void due(selTask.id, e.target.value) }}
                style={{ position: 'absolute', left: 16, bottom: 8, width: 1, height: 1, opacity: 0, pointerEvents: 'none' }} />
            </div>

            <div className="tdx-det-foot">
              <span className="tdx-mut">{selTask.by} əlavə edib</span>
              <button className="tdx-det-del" onClick={() => void remove(selTask.id)}>🗑️ Sil</button>
            </div>
          </aside>
        </>
      )}

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
            {/* The menu never lists people — with a hundred managers it would scroll off the screen.
                "Özümə götür" covers the overwhelmingly common case in one tap; everyone else lives in
                the panel's searchable picker. */}
            {employeeId && t.assignedToEmployeeId !== employeeId && (
              <button className="tdx-mi" onClick={() => void assign(t.id, employeeId)}><span>🙋</span> Özümə götür</button>
            )}
            <button className="tdx-mi" onClick={() => { setSel(t.id); setMenu(null) }}><span>👤</span> Kimə təyin et…</button>
            {t.assignedToEmployeeId && (
              <button className="tdx-mi" onClick={() => void assign(t.id, null)}><span>✕</span> Təyinatı götür</button>
            )}
            <div className="tdx-sep" />
            <button className="tdx-mi" onClick={() => void due(t.id, today)}><span>📅</span> Bu gün</button>
            <button className="tdx-mi" onClick={() => void due(t.id, iso(new Date(Date.now() + 864e5)))}><span>➡️</span> Sabah</button>
            <button className="tdx-mi" onClick={(e) => { e.stopPropagation(); openDatePicker(dateRef, t.dueDate) }}>
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
  t, selected, onOpen, onToggle, onStar, onMenu, draggable, onDragStart, onDropRow,
}: {
  t: TaskRow
  selected: boolean
  onOpen: () => void
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
      className={`tdx-row ${t.isDone ? 'done' : ''} ${selected ? 'sel' : ''}`}
      draggable={draggable}
      onDragStart={onDragStart}
      onDragOver={(e) => draggable && e.preventDefault()}
      onDrop={onDropRow}
      onContextMenu={(e) => { e.preventDefault(); onMenu(e.clientX, e.clientY) }}
    >
      {draggable && <span className="tdx-grip" title="Sürüklə">≡</span>}
      <button className={`tdx-check ${t.isDone ? 'on' : ''}`} onClick={() => onToggle(t.id)} aria-label="Tamamla">
        <svg viewBox="0 0 24 24" width="14" height="14"><path d="M5 12.5l4.5 4.5L19 7" fill="none" stroke="currentColor" strokeWidth="2.6" strokeLinecap="round" strokeLinejoin="round" /></svg>
      </button>
      <div className="tdx-body" onClick={onOpen}>
        <div className="tdx-text">{t.title}</div>
        <div className="tdx-meta">
          {d && <span className={`tdx-due ${d.tone}`}>📅 {d.label}</span>}
          {/* Who it is FOR reads before who added it — on a shared board that is the question. */}
          {t.assignedToName && <span className="tdx-who">👤 {t.assignedToName}</span>}
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

.tdx-who{ font-size:11px; font-weight:700; color:var(--blue, #2E74B5); background:var(--blue-bg, #EAF2FB); border-radius:6px; padding:2px 7px; }
.tdx-list{ background:var(--white); border:1px solid var(--c100); border-radius:12px; overflow:hidden; box-shadow:var(--sh-sm); }
.tdx-row{ display:flex; align-items:center; gap:11px; padding:11px 12px 11px 8px; border-bottom:1px solid var(--c50); transition:background .12s; }
.tdx-row:last-child{ border-bottom:none; }
.tdx-row:hover{ background:var(--sand); }
.tdx-row.sel{ background:color-mix(in srgb, var(--leaf) 8%, transparent); }
.tdx-row:hover .tdx-grip{ opacity:.5; }
.tdx-grip{ flex:none; width:14px; text-align:center; color:var(--c400); cursor:grab; opacity:0; transition:opacity .12s; user-select:none; font-size:15px; }
.tdx-check{ flex:none; width:22px; height:22px; border-radius:50%; border:1.6px solid var(--c400); background:transparent; display:grid; place-items:center; cursor:pointer; color:transparent; transition:all .15s; padding:0; }
.tdx-check:hover{ border-color:var(--leaf); color:var(--leaf); }
.tdx-check.on{ background:var(--leaf); border-color:var(--leaf); color:#fff; }
.tdx-body{ flex:1; min-width:0; cursor:pointer; }
.tdx-text{ font-size:15px; color:var(--c900); word-break:break-word; line-height:1.3; }
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

/* ---- detail panel ---- */
.tdx-det{ flex:none; width:272px; background:var(--white); border:1px solid var(--c100); border-radius:var(--r-lg); padding:14px; box-shadow:var(--sh-sm); position:sticky; top:12px; }
.tdx-det-head{ display:flex; align-items:center; gap:9px; }
.tdx-det-title{ flex:1; min-width:0; border:none; outline:none; background:transparent; font-size:15px; font-weight:700; color:var(--c900); font-family:inherit; padding:2px 0; border-bottom:1.5px solid transparent; }
.tdx-det-title:focus{ border-bottom-color:var(--leaf); }
.tdx-det-x{ flex:none; border:none; background:transparent; color:var(--c400); font-size:14px; cursor:pointer; padding:4px 6px; border-radius:6px; }
.tdx-det-x:hover{ background:var(--sand); color:var(--c700); }
.tdx-det-sec{ margin-top:16px; position:relative; }
.tdx-det-lab{ font-size:11px; font-weight:700; text-transform:uppercase; letter-spacing:.06em; color:var(--c400); margin-bottom:6px; }
.tdx-psearch{ width:100%; border:1px solid var(--c100); border-radius:8px; padding:7px 10px; font-size:13px; font-family:inherit; color:var(--c900); background:var(--white); outline:none; margin-bottom:6px; }
.tdx-psearch:focus{ border-color:var(--leaf); }
.tdx-psearch::placeholder{ color:var(--c400); }
.tdx-plist{ max-height:236px; overflow-y:auto; }
.tdx-p{ display:flex; align-items:center; gap:9px; width:100%; border:none; background:transparent; border-radius:8px; padding:6px 8px; cursor:pointer; font-size:13.5px; color:var(--c800,var(--c900)); text-align:left; }
.tdx-p:hover{ background:var(--sand); }
.tdx-p.on{ background:var(--blue-bg,#EAF2FB); font-weight:700; }
.tdx-p-n{ flex:1; min-width:0; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
.tdx-p-on{ color:var(--blue,#2E74B5); font-weight:800; }
.tdx-av{ flex:none; width:26px; height:26px; border-radius:50%; background:var(--blue-bg,#EAF2FB); color:var(--blue,#2E74B5); display:grid; place-items:center; font-size:10.5px; font-weight:800; }
.tdx-p.on .tdx-av{ background:var(--blue,#2E74B5); color:#fff; }
.tdx-det-due{ display:inline-flex; align-items:center; gap:7px; font-size:12.5px; font-weight:700; padding:4px 9px; border-radius:7px; background:var(--sand); color:var(--c700); margin-bottom:8px; }
.tdx-det-due.over{ background:rgba(200,60,40,.12); color:var(--clay,#c83c28); }
.tdx-det-due.today{ background:color-mix(in srgb, var(--leaf) 16%, transparent); color:var(--leaf-d, var(--leaf)); }
.tdx-det-due-x{ border:none; background:transparent; color:inherit; cursor:pointer; font-size:11px; padding:0 2px; opacity:.7; }
.tdx-det-due-x:hover{ opacity:1; }
.tdx-chips{ display:flex; flex-wrap:wrap; gap:6px; }
.tdx-chip{ border:1px solid var(--c100); background:var(--white); border-radius:8px; padding:6px 10px; font-size:12.5px; font-weight:600; color:var(--c700); cursor:pointer; }
.tdx-chip:hover{ background:var(--sand); }
.tdx-det-foot{ display:flex; align-items:center; justify-content:space-between; margin-top:18px; padding-top:10px; border-top:1px solid var(--c100); font-size:12px; }
.tdx-det-del{ border:none; background:transparent; color:var(--clay,#c83c28); font-weight:700; font-size:12.5px; cursor:pointer; padding:4px 8px; border-radius:7px; }
.tdx-det-del:hover{ background:rgba(200,60,40,.1); }
.tdx-ovl{ display:none; }

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
  /* The panel becomes a bottom sheet — a third column has nowhere to live on a phone. */
  .tdx-ovl{ display:block; position:fixed; inset:0; background:rgba(15,20,10,.4); z-index:1290; }
  .tdx-det{ position:fixed; left:0; right:0; bottom:0; top:auto; width:auto; z-index:1300; border-radius:18px 18px 0 0; max-height:78vh; overflow:auto; padding-bottom:max(14px, env(safe-area-inset-bottom)); }
}
`
