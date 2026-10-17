import { useCallback, useEffect, useState } from 'react'
import {
  accessErrorText, getPersonAccess, isAccessError, revokeAssignment, type PersonAccess,
} from '../api/access'
import { AssignRoleDrawer } from './AssignRoleDrawer'

/**
 * «Hüquqlar» on a person's card: what they hold and where, what it adds up to, and — for whoever may —
 * giving a role or taking one away right there. The quickest daily path: open the person, «Rol ver», pick
 * «Tabelçi» and two branches. Effective from the person's next request; nobody is signed out.
 *
 * Today's rows (from the employee form's role and branches) are shown as such and cannot be taken away
 * here — they follow the form.
 */
export function PersonAccessCard({ employeeId, fullName }: { employeeId: string; fullName: string }) {
  const [data, setData] = useState<PersonAccess | null>(null)
  const [hidden, setHidden] = useState(false)
  const [assigning, setAssigning] = useState(false)
  const [showAll, setShowAll] = useState(false)
  const [busyId, setBusyId] = useState<string | null>(null)
  const [message, setMessage] = useState<{ ok: boolean; text: string } | null>(null)

  const load = useCallback(async () => {
    const { status, data: d } = await getPersonAccess(employeeId)
    if (status === 200 && !isAccessError(d)) setData(d)
    else setHidden(true) // not this caller's to see — the card simply isn't there
  }, [employeeId])

  useEffect(() => {
    void load()
  }, [load])

  async function revoke(id: string, label: string) {
    if (!window.confirm(`${fullName}: «${label}» geri alınsın? Növbəti açılışdan qüvvədədir.`)) return
    setBusyId(id)
    setMessage(null)
    const { status, data: d } = await revokeAssignment(id)
    setBusyId(null)
    if (status === 200) {
      setMessage({ ok: true, text: `«${label}» geri alındı.` })
      void load()
    } else {
      setMessage({ ok: false, text: accessErrorText(d) })
    }
  }

  if (hidden || !data) return null

  const manual = data.assignments.filter((a) => !a.system)
  const system = data.assignments.filter((a) => a.system)
  const permissions = showAll ? data.permissions : data.permissions.slice(0, 8)

  return (
    <div className="card card-pad">
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 12, marginBottom: 10 }}>
        <div className="card-title" style={{ margin: 0 }}>Hüquqlar</div>
        {!data.isOperator && (
          <button type="button" className="btn btn-sm btn-primary" onClick={() => { setMessage(null); setAssigning(true) }}>
            Rol ver
          </button>
        )}
      </div>

      {message && <div className={`fb ${message.ok ? 'fb-ok' : 'fb-err'}`} style={{ marginBottom: 10 }}>{message.text}</div>}
      {data.isOperator && <p className="muted">Platforma operatoru — hüquqları buradan dəyişmir.</p>}

      {data.assignments.length === 0 ? (
        <p className="muted" style={{ margin: 0 }}>Panel hüququ yoxdur — yalnız öz qeydlərini görür.</p>
      ) : (
        <div className="acc-list">
          {[...manual, ...system].map((a) => (
            <div key={a.id} className="acc-row">
              <div className="acc-row-main">
                <div className="acc-row-meta">
                  <span className="acc-badge role">{a.roleName}</span>
                  <span className="acc-badge area">{a.area}</span>
                  {a.system && <span className="acc-badge sys" title="İşçi formasındakı vəzifə və filiallardan gəlir">işçi formasından</span>}
                </div>
                {!a.system && a.createdByName && (
                  <span className="acc-row-meta">verən: {a.createdByName}</span>
                )}
              </div>
              {a.canRevoke && (
                <button type="button" className="btn btn-sm" disabled={busyId === a.id} onClick={() => void revoke(a.id, a.roleName ?? 'rol')}>
                  Geri al
                </button>
              )}
            </div>
          ))}
        </div>
      )}

      {data.permissions.length > 0 && (
        <details style={{ marginTop: 12 }}>
          <summary className="muted" style={{ cursor: 'pointer', fontSize: 13 }}>
            Nə edə bilər — {data.permissions.length} hüquq
          </summary>
          <div className="acc-list" style={{ marginTop: 6 }}>
            {permissions.map((p) => (
              <div key={p.key} className="acc-row" style={{ padding: '7px 2px' }}>
                <span style={{ fontSize: 13 }}>{p.label}</span>
                <span className="acc-row-meta" style={{ justifyContent: 'flex-end' }}>
                  {p.company ? 'bütün şirkət' : p.branches.length <= 3 ? p.branches.join(', ') : `${p.branches.length} filial`}
                </span>
              </div>
            ))}
          </div>
          {data.permissions.length > 8 && (
            <button type="button" className="btn btn-sm btn-ghost" onClick={() => setShowAll(!showAll)}>
              {showAll ? 'Qısalt' : `Hamısı (${data.permissions.length})`}
            </button>
          )}
        </details>
      )}

      {assigning && (
        <AssignRoleDrawer
          person={{ id: employeeId, fullName }}
          onClose={() => setAssigning(false)}
          onDone={(text) => {
            setAssigning(false)
            setMessage({ ok: true, text })
            void load()
          }}
        />
      )}
    </div>
  )
}
