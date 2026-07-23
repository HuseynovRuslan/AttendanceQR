import { useEffect, useMemo, useRef, useState, type FormEvent } from 'react'
import {
  acknowledgeTask,
  completeTask,
  createTask,
  deleteTask,
  getTaskAccess,
  getTasks,
  type TaskAccess,
  type TaskItem,
  type TaskRecipient,
} from '../../api/tasks'
import { IconCheck, IconSend, IconTrash, IconX } from '../../components/icons'
import { fmtDate, fmtDateTime } from '../../lib/format'

const ERRORS: Record<string, string> = {
  TitleRequired: 'Başlıq boş ola bilməz',
  TitleTooLong: 'Başlıq çox uzundur',
  DueDateRequired: 'Son tarix (deadline) seçin',
  NoRecipients: 'Ən azı bir nəfər seçin',
  NotAllowedToAssign: 'Sizin tapşırıq vermə icazəniz yoxdur',
  RecipientNotAllowed: 'Seçilən şəxslərdən birinə tapşırıq vermə icazəniz yoxdur',
  EmployeeNotFound: 'İşçi tapılmadı',
  NotYourTask: 'Bu tapşırıq sizə aid deyil',
  NotFound: 'Tapşırıq tapılmadı',
}

type Filter = 'all' | 'incoming' | 'outgoing'

function StatusPill({ status }: { status: TaskItem['status'] }) {
  const done = status === 'Completed'
  return (
    <span
      className="tag"
      style={{
        background: done ? 'var(--leaf-bg)' : 'var(--amber-bg)',
        color: done ? 'var(--leaf-d)' : 'var(--amber)',
        fontWeight: 700,
      }}
    >
      {done ? 'Hazırdır' : 'Gözləyir'}
    </span>
  )
}

export function TasksPage() {
  const [rows, setRows] = useState<TaskItem[]>([])
  const [access, setAccess] = useState<TaskAccess | null>(null)

  const [title, setTitle] = useState('')
  const [dueDate, setDueDate] = useState('')
  const [description, setDescription] = useState('')
  const [recipientIds, setRecipientIds] = useState<string[]>([])
  const [search, setSearch] = useState('')
  const [pickerOpen, setPickerOpen] = useState(false)
  const pickerRef = useRef<HTMLDivElement>(null)

  const [filter, setFilter] = useState<Filter>('all')
  const [error, setError] = useState<string | null>(null)
  const [ok, setOk] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const [busyId, setBusyId] = useState<string | null>(null)

  async function refresh() {
    const [tasksRes, accessRes] = await Promise.all([getTasks(), getTaskAccess()])
    if (tasksRes.status === 200 && Array.isArray(tasksRes.data)) setRows(tasksRes.data)
    if (accessRes.status === 200 && accessRes.data) setAccess(accessRes.data)
  }

  useEffect(() => {
    void refresh()
  }, [])

  useEffect(() => {
    function onClickOutside(e: MouseEvent) {
      if (pickerRef.current && !pickerRef.current.contains(e.target as Node)) setPickerOpen(false)
    }
    document.addEventListener('mousedown', onClickOutside)
    return () => document.removeEventListener('mousedown', onClickOutside)
  }, [])

  const recipients = access?.recipients ?? []
  const byId = useMemo(() => {
    const m = new Map<string, TaskRecipient>()
    for (const r of recipients) m.set(r.id, r)
    return m
  }, [recipients])

  const matches = useMemo(() => {
    const q = search.trim().toLowerCase()
    return recipients
      .filter((r) => !recipientIds.includes(r.id))
      .filter((r) => (q ? r.name.toLowerCase().includes(q) : true))
      .slice(0, 8)
  }, [recipients, recipientIds, search])

  function addRecipient(id: string) {
    setRecipientIds((ids) => (ids.includes(id) ? ids : [...ids, id]))
    setSearch('')
    setPickerOpen(false)
  }
  function removeRecipient(id: string) {
    setRecipientIds((ids) => ids.filter((x) => x !== id))
  }

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    setOk(null)
    if (recipientIds.length === 0) {
      setError(ERRORS.NoRecipients)
      return
    }
    setSaving(true)
    const { status, data } = await createTask({
      assignedToEmployeeIds: recipientIds,
      title: title.trim(),
      description: description.trim() || null,
      dueDate,
    })
    setSaving(false)

    if (status === 200) {
      setOk('Tapşırıq göndərildi')
      setTitle('')
      setDueDate('')
      setDescription('')
      setRecipientIds([])
      setSearch('')
      await refresh()
    } else if (data && typeof data === 'object' && 'error' in data) {
      setError(ERRORS[(data as { error: string }).error] ?? 'Göndərilmədi')
    } else {
      setError('Göndərilmədi')
    }
  }

  async function onComplete(t: TaskItem) {
    if (!window.confirm(`"${t.title}" tapşırığı hazırdır kimi işarələnsin?`)) return
    setBusyId(t.id)
    const { status } = await completeTask(t.id)
    setBusyId(null)
    if (status === 200) await refresh()
    else setError('Əməliyyat alınmadı')
  }

  async function onAcknowledge(t: TaskItem) {
    setBusyId(t.id)
    const { status } = await acknowledgeTask(t.id)
    setBusyId(null)
    if (status === 200) await refresh()
    else setError('Əməliyyat alınmadı')
  }

  async function onDelete(t: TaskItem) {
    if (!window.confirm(`"${t.title}" tapşırığı silinsin?`)) return
    setBusyId(t.id)
    const { status } = await deleteTask(t.id)
    setBusyId(null)
    if (status === 200) await refresh()
    else setError('Silinmədi')
  }

  const visible = rows.filter((r) => (filter === 'all' ? true : r.direction === filter))
  const canAssign = access?.canAssign ?? false

  return (
    <div>
      <div className="fb" style={{ marginBottom: 16, background: 'var(--c50, #f6f8f4)', color: 'var(--c500)' }}>
        <span>
          {canAssign
            ? 'Deadline ilə tapşırıq yaradın və icazə verilmiş şəxslərə göndərin. Alan şəxs "Hazırdır" işarələdikdə sizə bildiriş gəlir.'
            : 'Sizə göndərilən tapşırıqlar burada görünür. Tamamlayanda "Hazırdır" düyməsini basın — tapşırığı verənə bildiriş gedəcək.'}
        </span>
      </div>

      {canAssign && (
        <form onSubmit={onSubmit} className="card card-pad" style={{ marginBottom: 16, maxWidth: 640 }}>
          <div style={{ fontWeight: 700, color: 'var(--c900)', marginBottom: 14 }}>Yeni tapşırıq</div>

          {error && (
            <div className="fb fb-err" style={{ marginBottom: 14 }}>
              <IconX />
              <span>{error}</span>
            </div>
          )}
          {ok && (
            <div className="fb fb-ok" style={{ marginBottom: 14 }}>
              <IconCheck />
              <span>{ok}</span>
            </div>
          )}

          <div className="form-row cols2">
            <div>
              <label className="form-label">Başlıq</label>
              <input
                className="inp"
                required
                maxLength={200}
                value={title}
                onChange={(e) => setTitle(e.target.value)}
                placeholder="məs. Anbarın yoxlanışı"
              />
            </div>
            <div>
              <label className="form-label">Son tarix (deadline)</label>
              <input className="inp" type="date" required value={dueDate} onChange={(e) => setDueDate(e.target.value)} />
            </div>
          </div>

          <div className="form-row">
            <label className="form-label">Ətraflı (istəyə bağlı)</label>
            <textarea
              className="inp"
              rows={3}
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              placeholder="Tapşırığın təfərrüatları"
            />
          </div>

          <div className="form-row">
            <label className="form-label">Kimə göndərilsin</label>

            {recipientIds.length > 0 && (
              <div className="chip-row" style={{ marginBottom: 8 }}>
                {recipientIds.map((id) => (
                  <span key={id} className="chip active" style={{ cursor: 'default' }}>
                    {byId.get(id)?.name ?? '—'}
                    <button
                      type="button"
                      onClick={() => removeRecipient(id)}
                      aria-label="Sil"
                      style={{ marginLeft: 6, border: 'none', background: 'transparent', cursor: 'pointer', color: 'inherit', fontWeight: 800 }}
                    >
                      ×
                    </button>
                  </span>
                ))}
              </div>
            )}

            <div ref={pickerRef} style={{ position: 'relative' }}>
              <input
                className="inp"
                value={search}
                onChange={(e) => {
                  setSearch(e.target.value)
                  setPickerOpen(true)
                }}
                onFocus={() => setPickerOpen(true)}
                placeholder="Ad yazıb axtarın…"
              />
              {pickerOpen && matches.length > 0 && (
                <div
                  className="card"
                  style={{ position: 'absolute', left: 0, right: 0, top: '105%', zIndex: 40, maxHeight: 260, overflowY: 'auto' }}
                >
                  {matches.map((r) => (
                    <div
                      key={r.id}
                      onMouseDown={(e) => {
                        e.preventDefault()
                        addRecipient(r.id)
                      }}
                      style={{ padding: '9px 12px', cursor: 'pointer', fontSize: 14, color: 'var(--c700)', borderBottom: '1px solid var(--c50)' }}
                    >
                      {r.name}
                    </div>
                  ))}
                </div>
              )}
              {pickerOpen && search.trim() !== '' && matches.length === 0 && (
                <div className="card" style={{ position: 'absolute', left: 0, right: 0, top: '105%', zIndex: 40 }}>
                  <div className="muted" style={{ padding: '9px 12px', fontSize: 13 }}>Uyğun şəxs tapılmadı</div>
                </div>
              )}
            </div>
          </div>

          <button type="submit" className="btn btn-primary" disabled={saving}>
            <IconSend />
            {saving ? 'Göndərilir…' : 'Tapşırıq göndər'}
          </button>
        </form>
      )}

      <div className="chip-row" style={{ marginBottom: 12 }}>
        <span className={`chip${filter === 'all' ? ' active' : ''}`} onClick={() => setFilter('all')}>Hamısı</span>
        <span className={`chip${filter === 'incoming' ? ' active' : ''}`} onClick={() => setFilter('incoming')}>
          Mənə göndərilən
        </span>
        <span className={`chip${filter === 'outgoing' ? ' active' : ''}`} onClick={() => setFilter('outgoing')}>
          Mənim göndərdiyim
        </span>
      </div>

      <div className="tbl-wrap">
        <table>
          <thead>
            <tr>
              <th>Tapşırıq</th>
              <th>Kim / Kimə</th>
              <th>Son tarix</th>
              <th>Status</th>
              <th style={{ textAlign: 'right' }}>Əməliyyat</th>
            </tr>
          </thead>
          <tbody>
            {visible.map((t) => (
              <tr key={t.id}>
                <td style={{ maxWidth: 320 }}>
                  <div style={{ fontWeight: 700, color: 'var(--c900)' }}>{t.title}</div>
                  {t.description && (
                    <div className="muted" style={{ fontSize: 12, marginTop: 2 }}>{t.description}</div>
                  )}
                  {t.status === 'Completed' && t.completedAtUtc && (
                    <div className="muted" style={{ fontSize: 11, marginTop: 2 }}>
                      Hazır oldu: {fmtDateTime(t.completedAtUtc)}
                    </div>
                  )}
                </td>
                <td>
                  {t.direction === 'incoming' ? (
                    <span>
                      <span className="muted" style={{ fontSize: 12 }}>Verən: </span>
                      {t.assignedByName}
                    </span>
                  ) : (
                    <span>
                      <span className="muted" style={{ fontSize: 12 }}>Alan: </span>
                      {t.assignedToName}
                    </span>
                  )}
                </td>
                <td className="mono">{t.dueDate ? fmtDate(t.dueDate) : '—'}</td>
                <td><StatusPill status={t.status} /></td>
                <td style={{ textAlign: 'right', whiteSpace: 'nowrap' }}>
                  {t.direction === 'incoming' && t.status === 'Pending' && (
                    <button className="btn btn-primary btn-sm" disabled={busyId === t.id} onClick={() => onComplete(t)}>
                      <IconCheck /> Hazırdır
                    </button>
                  )}
                  {t.direction === 'outgoing' && t.status === 'Completed' && !t.acknowledged && (
                    <button className="btn btn-sm" disabled={busyId === t.id} onClick={() => onAcknowledge(t)} style={{ marginRight: 6 }}>
                      <IconCheck /> Oxundu
                    </button>
                  )}
                  {t.direction === 'outgoing' && (
                    <button className="btn btn-danger btn-sm" disabled={busyId === t.id} onClick={() => onDelete(t)}>
                      <IconTrash /> Sil
                    </button>
                  )}
                  {t.direction === 'incoming' && t.status === 'Completed' && (
                    <span className="muted" style={{ fontSize: 12 }}>—</span>
                  )}
                </td>
              </tr>
            ))}
            {visible.length === 0 && (
              <tr>
                <td colSpan={5} className="muted" style={{ textAlign: 'center', padding: 28 }}>
                  Tapşırıq yoxdur
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  )
}
