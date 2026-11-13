import { useEffect, useRef, useState } from 'react'
import { getPlanChanges, type PlanChange } from '../../../api/shiftPlan'
import { IconX } from '../../../components/icons'
import { ShiftLabel } from './PreviewSummary'

/**
 * «Tarixçə» — a branch's changes, newest first: who, when, and for every day the shift before and after. A change
 * can be undone from here for thirty days; an undone change says so and by whom.
 */
export function HistoryPanel(props: {
  locationId: string
  reloadKey: number
  busy: boolean
  onClose: () => void
  onUndo: (change: PlanChange) => void
}) {
  const { locationId, reloadKey, busy, onClose, onUndo } = props
  const [changes, setChanges] = useState<PlanChange[] | null>(null)
  const [failed, setFailed] = useState(false)
  const [open, setOpen] = useState<string | null>(null)
  const panel = useRef<HTMLDivElement>(null)

  useEffect(() => {
    let live = true
    setFailed(false)
    void getPlanChanges(locationId).then(({ status, data }) => {
      if (!live) return
      if (status === 200 && Array.isArray(data)) setChanges(data)
      else setFailed(true)
    })
    return () => { live = false }
  }, [locationId, reloadKey])

  useEffect(() => {
    panel.current?.querySelector<HTMLButtonElement>('button')?.focus()
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose() }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  return (
    <div className="sp-overlay" role="presentation" onMouseDown={(e) => { if (e.target === e.currentTarget) onClose() }}>
      <div ref={panel} className="sp-drawer" role="dialog" aria-modal="true" aria-labelledby="sp-history-title">
        <header className="sp-drawerhead">
          <h2 id="sp-history-title">Dəyişikliklərin tarixçəsi</h2>
          <button type="button" className="btn btn-sm btn-ghost" aria-label="Bağla" onClick={onClose}><IconX /></button>
        </header>
        <div className="sp-drawerbody">
          {failed && <p className="sp-note sp-note-warn">Tarixçə yüklənmədi.</p>}
          {!failed && changes === null && <p className="muted">Yüklənir…</p>}
          {changes && changes.length === 0 && <p className="muted">Bu filialda hələ növbə dəyişikliyi olmayıb.</p>}
          {changes && changes.length > 0 && (
            <ol className="sp-history">
              {changes.map((c) => (
                <li key={c.id} className={c.undoneAt ? 'sp-history-undone' : undefined}>
                  <div className="sp-historyhead">
                    <span className="sp-historywhen">{c.at.slice(8, 10)}.{c.at.slice(5, 7)} · {c.at.slice(11)}</span>
                    <span className="sp-historywho">{c.by ?? '—'}</span>
                  </div>
                  <p className="sp-historywhat">{c.summary}</p>
                  {c.undoneAt && <p className="sp-note">Geri alınıb: {c.undoneBy ?? '—'}, {c.undoneAt.slice(8, 10)}.{c.undoneAt.slice(5, 7)} {c.undoneAt.slice(11)}</p>}
                  <div className="sp-historyacts">
                    <button type="button" className="btn-link" aria-expanded={open === c.id} onClick={() => setOpen(open === c.id ? null : c.id)}>
                      {open === c.id ? 'Gizlət' : `Günlərə bax (${c.items.length + c.more})`}
                    </button>
                    {c.undoable && <button type="button" className="btn btn-sm" disabled={busy} onClick={() => onUndo(c)}>Geri al</button>}
                  </div>
                  {open === c.id && (
                    <table className="sp-historytable">
                      <thead><tr><th scope="col">İşçi</th><th scope="col">Gün</th><th scope="col">Əvvəl</th><th scope="col">Sonra</th></tr></thead>
                      <tbody>
                        {c.items.map((i, n) => (
                          <tr key={n}>
                            <td>{i.employee}</td>
                            <td className="sp-nowrap">{i.standing ? `${short(i.date)}-dən` : short(i.date)}</td>
                            <td className="sp-before"><ShiftLabel text={i.before} /></td>
                            <td className="sp-after"><ShiftLabel text={i.after} /></td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  )}
                  {open === c.id && c.more > 0 && <p className="sp-note">və daha {c.more} gün</p>}
                </li>
              ))}
            </ol>
          )}
        </div>
      </div>
    </div>
  )
}

/** «12.11» — the change's own heading already says the year; a column of «12 noyabr» squeezes the shifts. */
function short(date: string): string {
  return `${date.slice(8, 10)}.${date.slice(5, 7)}`
}
