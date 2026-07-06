import { useEffect, useState, type FormEvent } from 'react'
import { addLeave, deleteLeave, getLeaves, type LeaveRecord, type LeaveType } from '../../api/leaves'
import { getEmployees, type AdminEmployee } from '../../api/admin'
import { IconCheck, IconTrash, IconX } from '../../components/icons'

const ERRORS: Record<string, string> = {
  DateRangeInvalid: 'Bitmə tarixi başlanğıcdan əvvəl ola bilməz',
  DateRangeTooLong: 'Tarix aralığı bir ildən çox ola bilməz',
  EmployeeNotFound: 'İşçi tapılmadı',
}

const TYPE_LABELS: Record<LeaveType, string> = {
  Vacation: 'Məzuniyyət',
  Sick: 'Xəstəlik',
  Unpaid: 'Ödənişsiz',
  Permission: 'İcazə',
}

export function LeavesPage() {
  const [rows, setRows] = useState<LeaveRecord[]>([])
  const [employees, setEmployees] = useState<AdminEmployee[]>([])
  const [employeeId, setEmployeeId] = useState('')
  const [fromDate, setFromDate] = useState('')
  const [toDate, setToDate] = useState('')
  const [type, setType] = useState<LeaveType>('Vacation')
  const [note, setNote] = useState('')
  const [filterType, setFilterType] = useState<LeaveType | ''>('')
  const [error, setError] = useState<string | null>(null)
  const [ok, setOk] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const [deletingId, setDeletingId] = useState<string | null>(null)

  async function refresh() {
    const [leavesRes, empsRes] = await Promise.all([getLeaves(), getEmployees()])
    if (leavesRes.status === 200 && Array.isArray(leavesRes.data)) setRows(leavesRes.data)
    if (empsRes.status === 200 && Array.isArray(empsRes.data)) setEmployees(empsRes.data)
  }

  useEffect(() => {
    void refresh()
  }, [])

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    setOk(null)
    setSaving(true)
    const { status, data } = await addLeave(employeeId, fromDate, toDate, type, note.trim())
    setSaving(false)

    if (status === 200) {
      setOk('Qeyd əlavə olundu')
      setEmployeeId('')
      setFromDate('')
      setToDate('')
      setType('Vacation')
      setNote('')
      await refresh()
    } else if (data && typeof data === 'object' && 'error' in data) {
      setError(ERRORS[(data as { error: string }).error] ?? 'Yadda saxlanmadı')
    } else {
      setError('Yadda saxlanmadı')
    }
  }

  async function onDelete(l: LeaveRecord) {
    if (!window.confirm(`${l.employeeName} — ${TYPE_LABELS[l.type]} (${fmtDate(l.fromDate)}–${fmtDate(l.toDate)}) silinsin?`)) return
    setError(null)
    setOk(null)
    setDeletingId(l.id)
    const { status } = await deleteLeave(l.id)
    setDeletingId(null)
    if (status === 200) await refresh()
    else setError('Silinmədi')
  }

  const visible = filterType ? rows.filter((r) => r.type === filterType) : rows

  return (
    <div>
      <div className="fb" style={{ marginBottom: 16, background: 'var(--c50, #f6f8f4)', color: 'var(--c500)' }}>
        <span>
          Bu aralıqdakı günlərdə işçi <b>"Qayıb"</b> sayılmır — Məzuniyyət/Xəstəlik/Ödənişsiz
          "Məzuniyyət", İcazə isə "İcazə" statusu göstərir. Həmin gündə giriş edilsə, yenə normal
          işlənmiş kimi sayılır.
        </span>
      </div>

      <form onSubmit={onSubmit} className="card card-pad" style={{ marginBottom: 16, maxWidth: 640 }}>
        <div style={{ fontWeight: 700, color: 'var(--c900)', marginBottom: 14 }}>Yeni qeyd</div>

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
          <label className="form-label">İşçi</label>
          <select className="inp" required value={employeeId} onChange={(e) => setEmployeeId(e.target.value)}>
            <option value="">Seçin</option>
            {employees.map((e) => (
              <option key={e.id} value={e.id}>{e.fullName}</option>
            ))}
          </select>
        </div>

        <div className="form-row cols2">
          <div>
            <label className="form-label">Başlanğıc tarixi</label>
            <input className="inp" type="date" required value={fromDate} onChange={(e) => setFromDate(e.target.value)} />
          </div>
          <div>
            <label className="form-label">Bitmə tarixi</label>
            <input className="inp" type="date" required value={toDate} onChange={(e) => setToDate(e.target.value)} />
          </div>
        </div>

        <div className="form-row cols2">
          <div>
            <label className="form-label">Növ</label>
            <select className="inp" value={type} onChange={(e) => setType(e.target.value as LeaveType)}>
              {(Object.keys(TYPE_LABELS) as LeaveType[]).map((t) => (
                <option key={t} value={t}>{TYPE_LABELS[t]}</option>
              ))}
            </select>
          </div>
          <div>
            <label className="form-label">Qeyd (istəyə bağlı)</label>
            <input className="inp" value={note} onChange={(e) => setNote(e.target.value)} placeholder="məs. Ailə vəziyyəti" />
          </div>
        </div>

        <button type="submit" className="btn btn-primary" disabled={saving}>
          <IconCheck />
          {saving ? 'Yadda saxlanır…' : 'Əlavə et'}
        </button>
      </form>

      <div className="chip-row" style={{ marginBottom: 12 }}>
        <span className={`chip${!filterType ? ' active' : ''}`} onClick={() => setFilterType('')}>Hamısı</span>
        {(Object.keys(TYPE_LABELS) as LeaveType[]).map((t) => (
          <span key={t} className={`chip${filterType === t ? ' active' : ''}`} onClick={() => setFilterType(t)}>
            {TYPE_LABELS[t]}
          </span>
        ))}
      </div>

      <div className="tbl-wrap">
        <table>
          <thead>
            <tr>
              <th>İşçi</th>
              <th>Növ</th>
              <th>Tarix aralığı</th>
              <th>Qeyd</th>
              <th style={{ textAlign: 'right' }}>Əməliyyat</th>
            </tr>
          </thead>
          <tbody>
            {visible.map((l) => (
              <tr key={l.id}>
                <td style={{ fontWeight: 700, color: 'var(--c900)' }}>{l.employeeName}</td>
                <td>{TYPE_LABELS[l.type]}</td>
                <td className="mono">{fmtDate(l.fromDate)}–{fmtDate(l.toDate)}</td>
                <td>{l.note ?? '—'}</td>
                <td style={{ textAlign: 'right' }}>
                  <button className="btn btn-danger btn-sm" disabled={deletingId === l.id} onClick={() => onDelete(l)}>
                    <IconTrash /> Sil
                  </button>
                </td>
              </tr>
            ))}
            {visible.length === 0 && (
              <tr>
                <td colSpan={5} className="muted" style={{ textAlign: 'center', padding: 28 }}>
                  Qeyd yoxdur
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  )
}

function fmtDate(dateStr: string): string {
  const [y, m, d] = dateStr.split('-')
  return `${d}.${m}.${y}`
}
