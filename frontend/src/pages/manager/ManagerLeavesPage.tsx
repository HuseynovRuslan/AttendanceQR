import { useEffect, useState } from 'react'
import { LeaveForm } from '../../components/LeaveForm'
import {
  createManagerLeave,
  deleteManagerLeave,
  getLeaveSubjects,
  getManagerLeaves,
  type LeaveSubject,
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
  const [staff, setStaff] = useState<LeaveSubject[]>([])
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)

  async function load() {
    setLoading(true)
    const [l, e] = await Promise.all([getManagerLeaves(), getLeaveSubjects()])
    if (l.status === 200 && Array.isArray(l.data)) setLeaves(l.data)
    if (e.status === 200 && Array.isArray(e.data)) setStaff(e.data)
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
      {/* The same form the admin uses — one component, so the two cannot drift apart again. The list
          holds the manager's own row and their colleagues' at the same branches on purpose: a holiday
          of a manager's own, or of the other manager in a two-manager company, used to need an admin,
          and until one entered it the days counted as Qayıb against them. */}
      <LeaveForm
        people={staff.map((x) => ({
          id: x.id,
          // Say which row is which: an unexplained manager or admin in a staff picker reads as a bug.
          fullName: x.isSelf ? `${x.fullName} (özüm)` : x.isColleague ? `${x.fullName} (həmkar)` : x.fullName,
        }))}
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
