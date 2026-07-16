import { useEffect, useState } from 'react'
import { EmployeeLink } from '../../components/EmployeeLink'
import { adminUpdateRecord, getOpenRecords, type OpenRecord } from '../../api/attendance'
import { IconCheck, IconClock, IconX } from '../../components/icons'

// datetime-local <-> ISO, matching EmployeesPage. The input reads/writes the admin's local time; the
// wire is always UTC ISO.
function toLocalInput(d: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`
}

// Default check-out: the check-in's own date at 18:00 local — a plausible end of a work day, which
// the admin can override. There is no per-employee schedule to derive a truer value from yet.
function defaultCheckout(checkInIso: string): string {
  const d = new Date(checkInIso)
  d.setHours(18, 0, 0, 0)
  return toLocalInput(d)
}

function fmtDate(dateStr: string): string {
  const [y, m, d] = dateStr.split('-')
  return `${d}.${m}.${y}`
}

function fmtTime(iso: string): string {
  return new Date(iso).toLocaleTimeString('az-AZ', { hour: '2-digit', minute: '2-digit' })
}

export function OpenRecordsPage() {
  const [rows, setRows] = useState<OpenRecord[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busyId, setBusyId] = useState<string | null>(null)
  // Per-row chosen check-out, keyed by recordId; seeded lazily from defaultCheckout.
  const [times, setTimes] = useState<Record<string, string>>({})

  useEffect(() => {
    void load()
  }, [])

  async function load() {
    const { status, data } = await getOpenRecords()
    if (status === 200 && Array.isArray(data)) {
      setRows(data)
      setError(null)
    } else if (status === 403) {
      setError('İcazəniz yoxdur')
    } else {
      setError('Yüklənmədi')
    }
  }

  function timeFor(r: OpenRecord): string {
    return times[r.recordId] ?? defaultCheckout(r.checkInAtUtc)
  }

  async function close(r: OpenRecord) {
    const local = timeFor(r)
    const iso = new Date(local).toISOString()
    if (new Date(iso) < new Date(r.checkInAtUtc)) {
      setError('Çıxış girişdən əvvəl ola bilməz')
      return
    }
    if (!window.confirm(`${r.employeeName} · ${fmtDate(r.attendanceDate)} — çıxış vaxtı ${fmtTime(iso)} kimi təyin edilsin?`))
      return
    setBusyId(r.recordId)
    setError(null)
    const { status } = await adminUpdateRecord(r.recordId, undefined, iso)
    if (status === 200) {
      setRows((prev) => (prev ?? []).filter((x) => x.recordId !== r.recordId))
    } else {
      setError('Bağlanmadı')
    }
    setBusyId(null)
  }

  const count = rows?.length ?? 0
  const empty = rows !== null && count === 0

  return (
    <div>
      <div style={{ marginBottom: 14 }}>
        <h1 style={{ fontSize: 18, fontWeight: 800, color: 'var(--c900)' }}>Çıxışı unudulan günlər</h1>
        <div className="muted" style={{ fontSize: 13 }}>
          Giriş edib çıxış etməyən günlər. Çıxış təyin edilməsə, həmin gün hesabatda 0 saat sayılır.
        </div>
      </div>

      {error && (
        <div className="fb fb-err" style={{ marginBottom: 12 }}>
          <IconX />
          <span>{error}</span>
        </div>
      )}

      {count > 0 && (
        <div className="card card-pad" style={{ marginBottom: 14, borderColor: '#fde68a', background: '#fffbeb' }}>
          <div style={{ fontWeight: 700, color: '#92400e' }}>
            ⚠️ {count} gün çıxış gözləyir
          </div>
          <div style={{ fontSize: 13, color: 'var(--c600)', marginTop: 2 }}>
            Hər sətir üçün çıxış vaxtını yoxlayıb «Bağla» düyməsinə basın.
          </div>
        </div>
      )}

      <div className="tbl-wrap">
        <table>
          <thead>
            <tr>
              <th>İşçi</th>
              <th>Tarix</th>
              <th>Giriş</th>
              <th>Çıxış vaxtı</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {(rows ?? []).map((r) => (
              <tr key={r.recordId}>
                <td>
                  <div style={{ fontWeight: 700, color: 'var(--c900)' }}><EmployeeLink id={r.employeeId} name={r.employeeName} /></div>
                  <div className="muted" style={{ fontSize: 12 }}>{r.locationName}</div>
                </td>
                <td className="mono">{fmtDate(r.attendanceDate)}</td>
                <td className="mono" style={{ display: 'flex', alignItems: 'center', gap: 4 }}>
                  <IconClock /> {fmtTime(r.checkInAtUtc)}
                </td>
                <td>
                  <input
                    className="inp"
                    type="datetime-local"
                    value={timeFor(r)}
                    onChange={(e) => setTimes((prev) => ({ ...prev, [r.recordId]: e.target.value }))}
                  />
                </td>
                <td style={{ textAlign: 'right' }}>
                  <button className="btn btn-primary btn-sm" disabled={busyId === r.recordId} onClick={() => close(r)}>
                    <IconCheck /> Bağla
                  </button>
                </td>
              </tr>
            ))}
            {empty && !error && (
              <tr>
                <td colSpan={5} className="muted" style={{ textAlign: 'center', padding: 28 }}>
                  Çıxışı unudulan gün yoxdur 🎉
                </td>
              </tr>
            )}
            {rows === null && !error && (
              <tr>
                <td colSpan={5} className="muted" style={{ textAlign: 'center', padding: 28 }}>
                  Yüklənir…
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  )
}
