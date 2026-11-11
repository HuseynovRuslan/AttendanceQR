import { useState } from 'react'
import { Link } from 'react-router-dom'
import { IconEye, IconX } from '../../../components/icons'
import type { Employee } from './model'
import { RequestSheet } from './RequestSheet'
import { useProto } from './store'
import { Pips, StatusMark, typeLabel, useSheet, when } from './ui'

/** One person: what the company knows about them, and every request made about them. */
export function EmployeeSheet({ employee: e, canAct, onClose }: { employee: Employee; canAct: boolean; onClose: () => void }) {
  const { world: w } = useProto()
  const [asking, setAsking] = useState(false)
  const ref = useSheet(onClose)
  const branch = w.branches.find((b) => b.id === e.branchId)
  const history = w.requests.filter((r) => r.employeeId === e.id)

  if (asking)
    return <RequestSheet presetBranchId={e.branchId} presetEmployeeId={e.id} onClose={() => setAsking(false)} />

  return (
    <div className="apx-sheet-back" onMouseDown={(ev) => ev.target === ev.currentTarget && onClose()}>
      <div className="apx-sheet" role="dialog" aria-modal="true" aria-labelledby="apx-emp-title" ref={ref} tabIndex={-1}>
        <div className="apx-sheet-h">
          <div>
            <h2 id="apx-emp-title">{e.name}</h2>
            <p>{e.position}, {branch?.name}</p>
          </div>
          <button type="button" className="apx-btn apx-btn-ghost apx-btn-sm" onClick={onClose} aria-label="Bağla"><IconX /></button>
        </div>
        <div className="apx-sheet-b">
          <dl className="apx-facts">
            <div><dt>Status</dt><dd>{e.active ? 'Aktiv' : 'Deaktiv'}</dd></div>
            <div><dt>Növbə</dt><dd>{e.shift}</dd></div>
            <div><dt>Telefon</dt><dd>{e.phone || '—'}</dd></div>
            <div><dt>Davamiyyət tarixçəsi</dt><dd>{e.hasHistory ? 'Var' : 'Yoxdur'}</dd></div>
          </dl>
          {e.hasHistory && (
            <p className="apx-quiet" style={{ margin: 0 }}>
              Tarixçəsi olan işçi silinmir. İşdən çıxanda deaktiv edilir, keçmiş günləri və tabeli olduğu kimi qalır.
            </p>
          )}
          <div>
            <h3 className="apx-sec-t">Bu işçi haqqında müraciətlər</h3>
            {history.length === 0 ? (
              <p className="apx-quiet" style={{ margin: 0 }}>Hələ müraciət olmayıb.</p>
            ) : (
              <div className="apx-panel" style={{ overflow: 'hidden' }}>
                {history.map((r) => (
                  <Link key={r.id} to={`../muracietler?id=${r.id}`} className="apx-item" style={{ textDecoration: 'none' }} onClick={onClose}>
                    <span className="apx-item-t">{typeLabel(r.type)}</span>
                    <span className="apx-item-r"><StatusMark status={r.status} /></span>
                    <span className="apx-item-s">{when(r.createdAt)}</span>
                    <Pips r={r} />
                  </Link>
                ))}
              </div>
            )}
          </div>
        </div>
        <div className="apx-sheet-f">
          {canAct ? (
            <button type="button" className="apx-btn apx-btn-go" onClick={() => setAsking(true)}>Bu işçi üçün müraciət yarat</button>
          ) : (
            <span className="apx-readonly"><IconEye aria-hidden /> Yalnız baxış</span>
          )}
        </div>
      </div>
    </div>
  )
}
