import { useEffect, useState } from 'react'
import { createPosition, getPositions } from '../api/positions'

/**
 * Picks a job title from the company's catalogue instead of typing one.
 *
 * Typing produced "Layihə Rəhəri", "Layihə rəhbəri" and "Layihə Meneceri" for a single job, and
 * everything that groups by position then treated them as three roles. A new title can still be added
 * here — refusing that would just push people back to a free-text workaround — but it goes into the
 * catalogue, so the next person picks it rather than retyping it slightly differently.
 */
export function PositionSelect({
  value,
  onChange,
}: {
  value: string
  onChange: (value: string) => void
}) {
  const [names, setNames] = useState<string[]>([])
  const [adding, setAdding] = useState(false)
  const [draft, setDraft] = useState('')
  const [err, setErr] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    void getPositions().then(({ status, data }) => {
      if (status === 200 && Array.isArray(data)) setNames(data.map((p) => p.name))
    })
  }, [])

  async function add() {
    const name = draft.trim()
    if (!name) return
    setBusy(true)
    setErr(null)
    const { status, data } = await createPosition(name)
    setBusy(false)
    if (status === 200 || status === 409) {
      // 409 means someone already added it — the goal was to end up on that title either way.
      if (!names.includes(name)) setNames((prev) => [...prev, name].sort((a, b) => a.localeCompare(b, 'az')))
      onChange(name)
      setAdding(false)
      setDraft('')
    } else {
      setErr(data && 'error' in data && data.error === 'NameRequired' ? 'Ad boş ola bilməz' : 'Əlavə edilmədi')
    }
  }

  if (adding) {
    return (
      <div>
        <div style={{ display: 'flex', gap: 8 }}>
          <input
            className="inp"
            autoFocus
            value={draft}
            placeholder="Yeni vəzifənin adı"
            onChange={(e) => setDraft(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter') { e.preventDefault(); void add() }
              if (e.key === 'Escape') setAdding(false)
            }}
          />
          <button className="btn btn-sm btn-primary" type="button" disabled={busy} onClick={() => void add()}>
            Əlavə et
          </button>
          <button className="btn btn-sm" type="button" onClick={() => setAdding(false)}>Ləğv</button>
        </div>
        {err && <div className="muted" style={{ fontSize: 11, marginTop: 4, color: 'var(--clay)' }}>{err}</div>}
      </div>
    )
  }

  return (
    <div style={{ display: 'flex', gap: 8 }}>
      <select className="inp" value={value} onChange={(e) => onChange(e.target.value)}>
        <option value="">— seçilməyib —</option>
        {/* An employee may already sit on a title the catalogue doesn't have (bulk import). Showing it
            keeps the field honest instead of silently resetting their position on the next save. */}
        {value && !names.includes(value) && <option value={value}>{value}</option>}
        {names.map((n) => <option key={n} value={n}>{n}</option>)}
      </select>
      <button className="btn btn-sm" type="button" onClick={() => { setAdding(true); setDraft('') }}>
        + Yeni
      </button>
    </div>
  )
}
