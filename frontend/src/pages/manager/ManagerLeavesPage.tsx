import { useEffect, useState } from 'react'
import { LeaveForm } from '../../components/LeaveForm'
import {
  createManagerLeave,
  deleteManagerLeave,
  getManagerEmployees,
  getManagerLeaves,
  type ManagerEmployee,
  type ManagerLeave,
} from '../../api/manager'
import './manager.css'

const TYPES = [
  { value: 'Vacation', label: 'Məzuniyyət' },
  { value: 'Sick', label: 'Xəstəlik' },
  { value: 'Unpaid', label: 'Ödənişsiz' },
  { value: 'Permission', label: 'İcazə' },
  { value: 'Rest', label: 'İstirahət' },
  { value: 'BusinessTrip', label: 'Ezamiyyət' },
]
const TYPE_LABEL: Record<string, string> = Object.fromEntries(TYPES.map((t) => [t.value, t.label]))
const fmt = (iso: string) => iso.split('-').reverse().join('.')

/** Leave and permission for a manager's own staff — scoped server-side to their branches. */
export function ManagerLeavesPage() {
  const [leaves, setLeaves] = useState<ManagerLeave[]>([])
  const [staff, setStaff] = useState<ManagerEmployee[]>([])
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)

  async function load() {
    setLoading(true)
    const [l, e] = await Promise.all([getManagerLeaves(), getManagerEmployees(true)])
    if (l.status === 200 && Array.isArray(l.data)) setLeaves(l.data)
    if (e.status === 200 && Array.isArray(e.data)) setStaff(e.data.filter((x) => x.isActive))
    setLoading(false)
  }

  useEffect(() => { void load() }, [])

  async function remove(l: ManagerLeave) {
    if (!window.confirm(`${l.employeeName} — ${fmt(l.fromDate)}–${fmt(l.toDate)} silinsin?`)) return
    const { status } = await deleteManagerLeave(l.id)
    if (status === 200) void load()
  }

  return (
    <div>
      {/* The same form the admin uses — one component, so the two cannot drift apart again. The
          manager's own row is in the list on purpose: a holiday of their own used to need an admin,
          and until one entered it the day counted as Qayıb against them. */}
      <LeaveForm
        people={staff.map((x) => ({ id: x.id, fullName: x.fullName }))}
        busy={busy}
        onSubmit={async (input) => {
          setBusy(true)
          const { status, data } = await createManagerLeave({
            employeeIds: input.employeeIds,
            fromDate: input.fromDate,
            toDate: input.toDate,
            type: input.type,
            note: input.note || null,
          })
          setBusy(false)
          void load()
          if (status === 200 && data && 'created' in data) return data
          if (data && 'skipped' in data && data.skipped) return { created: [], skipped: data.skipped }
          return null
        }}
      />

      {loading && <div className="card card-pad muted">Yüklənir…</div>}
      {!loading && leaves.length === 0 && <div className="card card-pad muted" style={{ textAlign: 'center' }}>Qeyd yoxdur.</div>}
      <div className="mgr-list">
        {leaves.map((l) => (
          <div className="mgr-item" key={l.id}>
            <div className="mgr-main">
              <div className="mgr-name">{l.employeeName}</div>
              <div className="mgr-meta">
                {TYPE_LABEL[l.type] ?? l.type}<span className="dot">·</span>{fmt(l.fromDate)} – {fmt(l.toDate)}
                {l.note ? <><span className="dot">·</span>{l.note}</> : ''}
              </div>
            </div>
            <div className="mgr-side">
              <div className="mgr-actions">
                <button className="btn btn-sm btn-danger" onClick={() => void remove(l)}>Sil</button>
              </div>
            </div>
          </div>
        ))}
      </div>
    </div>
  )
}
