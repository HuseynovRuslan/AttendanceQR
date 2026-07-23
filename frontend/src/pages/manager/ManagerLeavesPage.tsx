import { useEffect, useState } from 'react'
import {
  createManagerLeave,
  deleteManagerLeave,
  getManagerEmployees,
  getManagerLeaves,
  type ManagerEmployee,
  type ManagerLeave,
} from '../../api/manager'
import { IconX } from '../../components/icons'
import './manager.css'

const TYPES = [
  { value: 'Vacation', label: 'Məzuniyyət' },
  { value: 'Sick', label: 'Xəstəlik' },
  { value: 'Unpaid', label: 'Ödənişsiz' },
  { value: 'Permission', label: 'İcazə' },
  { value: 'Rest', label: 'İstirahət' },
]
const TYPE_LABEL: Record<string, string> = Object.fromEntries(TYPES.map((t) => [t.value, t.label]))
const fmt = (iso: string) => iso.split('-').reverse().join('.')

/** Leave and permission for a manager's own staff — scoped server-side to their branches. */
export function ManagerLeavesPage() {
  const [leaves, setLeaves] = useState<ManagerLeave[]>([])
  const [staff, setStaff] = useState<ManagerEmployee[]>([])
  const [loading, setLoading] = useState(true)
  const [employeeId, setEmployeeId] = useState('')
  const [type, setType] = useState('Vacation')
  const [fromDate, setFromDate] = useState('')
  const [toDate, setToDate] = useState('')
  const [note, setNote] = useState('')
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState<string | null>(null)

  async function load() {
    setLoading(true)
    const [l, e] = await Promise.all([getManagerLeaves(), getManagerEmployees()])
    if (l.status === 200 && Array.isArray(l.data)) setLeaves(l.data)
    if (e.status === 200 && Array.isArray(e.data)) setStaff(e.data.filter((x) => x.isActive))
    setLoading(false)
  }

  useEffect(() => { void load() }, [])

  async function add() {
    if (!employeeId || !fromDate || !toDate) { setErr('İşçi və tarixləri seçin'); return }
    setBusy(true)
    setErr(null)
    const { status, data } = await createManagerLeave({ employeeId, fromDate, toDate, type, note: note || null })
    setBusy(false)
    if (status === 200 && data && 'id' in data) {
      setEmployeeId(''); setFromDate(''); setToDate(''); setNote('')
      void load()
    } else {
      const code = data && 'error' in data ? data.error : ''
      setErr(code === 'DateRangeInvalid' ? 'Bitmə tarixi başlanğıcdan əvvəl ola bilməz'
        : code === 'EmployeeNotManaged' ? 'Bu işçi sizə aid deyil'
        : 'Əlavə edilmədi')
    }
  }

  async function remove(l: ManagerLeave) {
    if (!window.confirm(`${l.employeeName} — ${fmt(l.fromDate)}–${fmt(l.toDate)} silinsin?`)) return
    const { status } = await deleteManagerLeave(l.id)
    if (status === 200) void load()
  }

  return (
    <div>
      <div className="card card-pad" style={{ marginBottom: 16 }}>
        <div className="card-title">Yeni məzuniyyət / icazə</div>
        <div className="form-row cols2">
          <div>
            <label className="form-label">İşçi</label>
            <select className="inp" value={employeeId} onChange={(e) => setEmployeeId(e.target.value)}>
              <option value="">— seçin —</option>
              {staff.map((s) => <option key={s.id} value={s.id}>{s.fullName}</option>)}
            </select>
          </div>
          <div>
            <label className="form-label">Növ</label>
            <select className="inp" value={type} onChange={(e) => setType(e.target.value)}>
              {TYPES.map((t) => <option key={t.value} value={t.value}>{t.label}</option>)}
            </select>
          </div>
        </div>
        <div className="form-row cols2">
          <div>
            <label className="form-label">Başlanğıc</label>
            <input className="inp" type="date" value={fromDate} onChange={(e) => setFromDate(e.target.value)} />
          </div>
          <div>
            <label className="form-label">Bitmə</label>
            <input className="inp" type="date" value={toDate} onChange={(e) => setToDate(e.target.value)} />
          </div>
        </div>
        <div>
          <label className="form-label">Qeyd (istəyə bağlı)</label>
          <input className="inp" value={note} onChange={(e) => setNote(e.target.value)} />
        </div>
        {err && <div className="fb fb-err" style={{ marginTop: 10 }}><IconX /><span>{err}</span></div>}
        <button className="btn btn-primary" style={{ marginTop: 12 }} disabled={busy} onClick={() => void add()}>
          {busy ? 'Əlavə edilir…' : 'Əlavə et'}
        </button>
      </div>

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
