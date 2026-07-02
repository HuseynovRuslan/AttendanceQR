import { useState } from 'react'
import { getToday, type DayAttendanceRow } from '../../api/admin'
import { usePolling } from '../../lib/usePolling'
import { StatusBadge } from '../../components/StatusBadge'
import { IconX } from '../../components/icons'

export function TodayPage() {
  const [rows, setRows] = useState<DayAttendanceRow[]>([])
  const [error, setError] = useState<string | null>(null)
  const [loadedOnce, setLoadedOnce] = useState(false)

  usePolling(async () => {
    const { status, data } = await getToday()
    if (status === 200 && Array.isArray(data)) {
      setRows(data)
      setError(null)
    } else if (status === 403) {
      setError('İcazəniz yoxdur')
    } else {
      setError('Məlumat yüklənmədi')
    }
    setLoadedOnce(true)
  }, 30_000)

  const counts = { present: 0, late: 0, absent: 0, incomplete: 0 }
  for (const r of rows) {
    if (r.status === 'OnTime') counts.present++
    else if (r.status === 'Late') counts.late++
    else if (r.status === 'Absent') counts.absent++
    else counts.incomplete++
  }

  return (
    <div>
      <div className="stat-grid">
        <div className="stat-card leaf">
          <div className="stat-lbl">Davamda</div>
          <div className="stat-val">{counts.present}</div>
        </div>
        <div className="stat-card amber">
          <div className="stat-lbl">Gecikmə</div>
          <div className="stat-val">{counts.late}</div>
        </div>
        <div className="stat-card clay">
          <div className="stat-lbl">Qayıb</div>
          <div className="stat-val">{counts.absent}</div>
        </div>
        <div className="stat-card blue">
          <div className="stat-lbl">Yarımçıq</div>
          <div className="stat-val">{counts.incomplete}</div>
        </div>
      </div>

      {error && (
        <div className="fb fb-err" style={{ marginBottom: 12 }}>
          <IconX />
          <span>{error}</span>
        </div>
      )}

      <div className="tbl-wrap">
        <table>
          <thead>
            <tr>
              <th>İşçi</th>
              <th>Ərazi</th>
              <th>Status</th>
              <th>Giriş</th>
              <th>Çıxış</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.employeeId}>
                <td style={{ fontWeight: 700, color: 'var(--c900)' }}>{r.employeeName}</td>
                <td>{r.locationName}</td>
                <td>
                  <StatusBadge status={r.status} />
                </td>
                <td className="mono">{fmtTime(r.checkInAtUtc)}</td>
                <td className="mono">{fmtTime(r.checkOutAtUtc)}</td>
              </tr>
            ))}
            {loadedOnce && rows.length === 0 && !error && (
              <tr>
                <td colSpan={5} className="muted" style={{ textAlign: 'center', padding: 28 }}>
                  Məlumat yoxdur
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  )
}

function fmtTime(iso: string | null): string {
  return iso ? new Date(iso).toLocaleTimeString('az-AZ', { hour: '2-digit', minute: '2-digit' }) : '—'
}
