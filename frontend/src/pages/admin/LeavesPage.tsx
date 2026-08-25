import { useEffect, useState } from 'react'
import { LeaveForm } from '../../components/LeaveForm'
import { EmployeeLink } from '../../components/EmployeeLink'
import { addLeave, deleteLeave, getLeaves, type LeaveRecord, type LeaveType } from '../../api/leaves'
import { getEmployees, type AdminEmployee } from '../../api/admin'
import { IconTrash, IconX } from '../../components/icons'
import { fmtDate } from '../../lib/format'

const TYPE_LABELS: Record<LeaveType, string> = {
  Vacation: 'Məzuniyyət',
  Sick: 'Xəstəlik',
  Unpaid: 'Ödənişsiz',
  Permission: 'İcazə',
  Rest: 'İstirahət',
  BusinessTrip: 'Ezamiyyət',
}

export function LeavesPage() {
  const [rows, setRows] = useState<LeaveRecord[]>([])
  const [employees, setEmployees] = useState<AdminEmployee[]>([])
  const [filterType, setFilterType] = useState<LeaveType | ''>('')
  const [error, setError] = useState<string | null>(null)
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

  async function onDelete(l: LeaveRecord) {
    if (!window.confirm(`${l.employeeName} — ${TYPE_LABELS[l.type]} (${fmtDate(l.fromDate)}–${fmtDate(l.toDate)}) silinsin?`)) return
    setError(null)
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
          Bu aralıqdakı günlərdə işçi <b>"Qayıb"</b> sayılmır — İstirahət tabeldə istirahət (H) kimi, Məzuniyyət/Xəstəlik/Ödənişsiz
          "Məzuniyyət", İcazə isə "İcazə" statusu göstərir. Həmin gündə giriş edilsə, yenə normal
          işlənmiş kimi sayılır.
        </span>
      </div>

      {error && (
        <div className="fb fb-err" style={{ marginBottom: 12 }}>
          <IconX />
          <span>{error}</span>
        </div>
      )}

      {/* One form for both surfaces — see LeaveForm for why the employee field is a list and why the
          defaults are what they are. The admin posts to the admin endpoint; the server re-checks. */}
      <LeaveForm
        people={employees.map((e) => ({ id: e.id, fullName: e.fullName, locationName: e.locationName }))}
        busy={saving}
        onSubmit={async (input) => {
          setSaving(true)
          const { status, data } = await addLeave({
            employeeIds: input.employeeIds,
            fromDate: input.fromDate,
            toDate: input.toDate,
            type: input.type as LeaveType,
            note: input.note,
          })
          setSaving(false)
          await refresh()
          if (status === 200 && data && 'created' in data) return data
          // Everybody in the batch was already off over these dates: the server refuses the whole
          // thing but still names them, which is the part worth showing.
          if (data && 'skipped' in data && data.skipped) return { created: [], skipped: data.skipped }
          return null
        }}
      />

      <div className="chip-row" style={{ marginBottom: 12 }}>
        <span className={`chip${!filterType ? ' active' : ''}`} onClick={() => setFilterType('')}>Hamısı</span>
        {(Object.keys(TYPE_LABELS) as LeaveType[]).map((t) => (
          <span key={t} className={`chip${filterType === t ? ' active' : ''}`} onClick={() => setFilterType(t)}>
            {TYPE_LABELS[t]}
          </span>
        ))}
      </div>

      <div className="tbl-wrap tbl-cards">
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
                <td data-label="İşçi" style={{ fontWeight: 700, color: 'var(--c900)' }}><EmployeeLink id={l.employeeId} name={l.employeeName} /></td>
                <td data-label="Növ">{TYPE_LABELS[l.type]}</td>
                <td data-label="Tarix" className="mono">{fmtDate(l.fromDate)}–{fmtDate(l.toDate)}</td>
                <td data-label="Qeyd">{l.note ?? '—'}</td>
                <td data-label="">
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

