import { useEffect, useMemo, useRef, useState, type FormEvent } from 'react'
import { getEmployees, type AdminEmployee } from '../../api/admin'
import { getTaskPermissions, setTaskRecipients, type TaskGiver } from '../../api/taskPermissions'
import { IconCheck, IconSend, IconTrash, IconX } from '../../components/icons'

export function TaskPermissionsPage() {
  const [employees, setEmployees] = useState<AdminEmployee[]>([])
  const [givers, setGivers] = useState<TaskGiver[]>([])

  const [giverId, setGiverId] = useState('')
  const [recipientIds, setRecipientIds] = useState<string[]>([])
  const [search, setSearch] = useState('')
  const [pickerOpen, setPickerOpen] = useState(false)
  const pickerRef = useRef<HTMLDivElement>(null)

  const [error, setError] = useState<string | null>(null)
  const [ok, setOk] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  async function refresh() {
    const [empsRes, permsRes] = await Promise.all([getEmployees(), getTaskPermissions()])
    if (empsRes.status === 200 && Array.isArray(empsRes.data)) setEmployees(empsRes.data)
    if (permsRes.status === 200 && Array.isArray(permsRes.data)) setGivers(permsRes.data)
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

  const active = useMemo(() => employees.filter((e) => e.isActive), [employees])
  const nameById = useMemo(() => {
    const m = new Map<string, string>()
    for (const e of employees) m.set(e.id, e.fullName)
    return m
  }, [employees])

  // Candidate recipients: active employees except the giver and the already-picked ones.
  const matches = useMemo(() => {
    const q = search.trim().toLowerCase()
    return active
      .filter((e) => e.id !== giverId && !recipientIds.includes(e.id))
      .filter((e) => (q ? e.fullName.toLowerCase().includes(q) : true))
      .slice(0, 8)
  }, [active, giverId, recipientIds, search])

  function addRecipient(id: string) {
    setRecipientIds((ids) => (ids.includes(id) ? ids : [...ids, id]))
    setSearch('')
    setPickerOpen(false)
  }
  function removeRecipient(id: string) {
    setRecipientIds((ids) => ids.filter((x) => x !== id))
  }

  // Load an existing giver's recipient set into the form for editing.
  function edit(g: TaskGiver) {
    setGiverId(g.assignerId)
    setRecipientIds(g.recipients.map((r) => r.id))
    setError(null)
    setOk(null)
    window.scrollTo({ top: 0, behavior: 'smooth' })
  }

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    setOk(null)
    if (!giverId) {
      setError('Tapşırıq verəcək şəxsi seçin')
      return
    }
    setSaving(true)
    const { status } = await setTaskRecipients(giverId, recipientIds)
    setSaving(false)
    if (status === 200) {
      setOk('Yadda saxlanıldı')
      setGiverId('')
      setRecipientIds([])
      setSearch('')
      await refresh()
    } else {
      setError('Yadda saxlanmadı')
    }
  }

  async function onClear(g: TaskGiver) {
    if (!window.confirm(`${g.assignerName} artıq heç kimə tapşırıq verə bilməsin?`)) return
    const { status } = await setTaskRecipients(g.assignerId, [])
    if (status === 200) await refresh()
  }

  return (
    <div>
      <div className="fb" style={{ marginBottom: 16, background: 'var(--c50, #f6f8f4)', color: 'var(--c500)' }}>
        <span>
          Burada təyin edirsiniz ki, <b>kim</b> tapşırıq verə bilər və <b>kimlərə</b>. "Tapşırıqlar" bölməsi
          yalnız tapşırıq verən və tapşırıq alan şəxslərdə görünür — hamıya yox.
        </span>
      </div>

      <form onSubmit={onSubmit} className="card card-pad" style={{ marginBottom: 16, maxWidth: 640 }}>
        <div style={{ fontWeight: 700, color: 'var(--c900)', marginBottom: 14 }}>İcazə təyin et</div>

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

        <div className="form-row">
          <label className="form-label">Tapşırıq verəcək şəxs</label>
          <select className="inp" required value={giverId} onChange={(e) => { setGiverId(e.target.value); setRecipientIds([]) }}>
            <option value="">Seçin</option>
            {active.map((e) => (
              <option key={e.id} value={e.id}>
                {e.fullName}
                {e.locationName ? ` — ${e.locationName}` : ''}
              </option>
            ))}
          </select>
        </div>

        <div className="form-row">
          <label className="form-label">Bu şəxs kimlərə tapşırıq verə bilər</label>

          {recipientIds.length > 0 && (
            <div className="chip-row" style={{ marginBottom: 8 }}>
              {recipientIds.map((id) => (
                <span key={id} className="chip active" style={{ cursor: 'default' }}>
                  {nameById.get(id) ?? '—'}
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
              onChange={(e) => { setSearch(e.target.value); setPickerOpen(true) }}
              onFocus={() => setPickerOpen(true)}
              placeholder="Ad yazıb axtarın…"
              disabled={!giverId}
            />
            {pickerOpen && giverId && matches.length > 0 && (
              <div className="card" style={{ position: 'absolute', left: 0, right: 0, top: '105%', zIndex: 40, maxHeight: 260, overflowY: 'auto' }}>
                {matches.map((e) => (
                  <div
                    key={e.id}
                    onMouseDown={(ev) => { ev.preventDefault(); addRecipient(e.id) }}
                    style={{ padding: '9px 12px', cursor: 'pointer', fontSize: 14, color: 'var(--c700)', borderBottom: '1px solid var(--c50)' }}
                  >
                    {e.fullName}
                    {e.locationName ? <span className="muted"> — {e.locationName}</span> : null}
                  </div>
                ))}
              </div>
            )}
          </div>
        </div>

        <button type="submit" className="btn btn-primary" disabled={saving}>
          <IconSend />
          {saving ? 'Yadda saxlanır…' : 'Yadda saxla'}
        </button>
      </form>

      <div className="tbl-wrap">
        <table>
          <thead>
            <tr>
              <th>Tapşırıq verən</th>
              <th>Kimlərə verə bilər</th>
              <th style={{ textAlign: 'right' }}>Əməliyyat</th>
            </tr>
          </thead>
          <tbody>
            {givers.map((g) => (
              <tr key={g.assignerId}>
                <td style={{ fontWeight: 700, color: 'var(--c900)' }}>{g.assignerName}</td>
                <td>{g.recipients.map((r) => r.name).join(', ') || '—'}</td>
                <td style={{ textAlign: 'right', whiteSpace: 'nowrap' }}>
                  <button className="btn btn-sm" onClick={() => edit(g)} style={{ marginRight: 6 }}>Redaktə</button>
                  <button className="btn btn-danger btn-sm" onClick={() => onClear(g)}>
                    <IconTrash /> Ləğv et
                  </button>
                </td>
              </tr>
            ))}
            {givers.length === 0 && (
              <tr>
                <td colSpan={3} className="muted" style={{ textAlign: 'center', padding: 28 }}>
                  Hələ heç kimə icazə verilməyib
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  )
}
