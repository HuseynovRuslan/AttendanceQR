import { useCallback, useEffect, useMemo, useState } from 'react'
import { getToday, type DayAttendanceRow } from '../../api/admin'
import { getPhotoUrl, type PhotoUrlResponse } from '../../api/attendance'
import { StatusBadge, STATUS_MAP } from '../../components/StatusBadge'
import { PhotoCompareModal } from '../../components/PhotoCompareModal'
import { FaceFlagBadge, faceIsFlagged } from '../../components/FaceFlagBadge'
import { IconCamera, IconX } from '../../components/icons'

function localDateISO(d: Date): string {
  const p = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`
}

export function TodayPage() {
  const todayISO = useMemo(() => localDateISO(new Date()), [])
  const [date, setDate] = useState(todayISO)
  const isToday = date === todayISO

  const [rows, setRows] = useState<DayAttendanceRow[]>([])
  const [error, setError] = useState<string | null>(null)
  const [loadedOnce, setLoadedOnce] = useState(false)
  const [filterLoc, setFilterLoc] = useState<string | null>(null)
  const [flaggedOnly, setFlaggedOnly] = useState(false)
  const [busyId, setBusyId] = useState<string | null>(null)
  const [photoError, setPhotoError] = useState<string | null>(null)
  const [modal, setModal] = useState<{ title: string; photo: PhotoUrlResponse } | null>(null)

  async function viewPhoto(row: DayAttendanceRow) {
    if (!row.recordId) return
    setBusyId(row.recordId)
    setPhotoError(null)
    // Fetch fresh presigned URLs each time — they expire (~5 min).
    const { status, data } = await getPhotoUrl(row.recordId)
    setBusyId(null)
    if (status !== 200 || !data || 'error' in data || !data.hasPhoto) {
      setPhotoError('Şəkil yüklənmədi')
      return
    }
    setModal({ title: row.employeeName, photo: data })
  }

  const load = useCallback(async () => {
    const { status, data } = await getToday(isToday ? undefined : date)
    if (status === 200 && Array.isArray(data)) {
      setRows(data)
      setError(null)
    } else if (status === 403) {
      setError('İcazəniz yoxdur')
    } else {
      setError('Məlumat yüklənmədi')
    }
    setLoadedOnce(true)
  }, [date, isToday])

  useEffect(() => {
    setLoadedOnce(false)
    void load()
    // Poll only the live "today" board — a past day's data doesn't change.
    if (!isToday) return
    const id = setInterval(() => void load(), 30_000)
    return () => clearInterval(id)
  }, [load, isToday])

  function shiftDate(delta: number) {
    const d = new Date(`${date}T00:00:00`)
    d.setDate(d.getDate() + delta)
    const iso = localDateISO(d)
    if (iso <= todayISO) setDate(iso)
  }

  const locations = useMemo(() => {
    const seen = new Map<string, string>()
    for (const r of rows) seen.set(r.locationId, r.locationName)
    return Array.from(seen, ([id, name]) => ({ id, name })).sort((a, b) => a.name.localeCompare(b.name))
  }, [rows])

  const locFiltered = filterLoc ? rows.filter((r) => r.locationId === filterLoc) : rows
  const flaggedCount = locFiltered.filter((r) => faceIsFlagged(r.faceMatchStatus)).length
  const visible = flaggedOnly ? locFiltered.filter((r) => faceIsFlagged(r.faceMatchStatus)) : locFiltered

  const counts = { present: 0, absent: 0, incomplete: 0, dayOff: 0, onLeave: 0, permission: 0 }
  for (const r of visible) {
    // Late folds into present — see StatusBadge: there is no per-employee schedule to be late against.
    if (r.status === 'OnTime' || r.status === 'Late') counts.present++
    else if (r.status === 'Absent') counts.absent++
    else if (r.status === 'DayOff') counts.dayOff++
    else if (r.status === 'OnLeave') counts.onLeave++
    else if (r.status === 'Permission') counts.permission++
    else counts.incomplete++
  }

  const dateLabel = new Date(`${date}T00:00:00`).toLocaleDateString('az-AZ', {
    weekday: 'long', day: 'numeric', month: 'long', year: 'numeric',
  })

  return (
    <div>
      <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 10, flexWrap: 'wrap' }}>
        <button className="btn btn-sm" onClick={() => shiftDate(-1)}>‹ Əvvəlki gün</button>
        <input
          type="date"
          value={date}
          max={todayISO}
          onChange={(e) => { if (e.target.value && e.target.value <= todayISO) setDate(e.target.value) }}
          className="inp"
          style={{ width: 'auto', padding: '6px 10px' }}
        />
        <button className="btn btn-sm" disabled={isToday} onClick={() => shiftDate(1)}>Növbəti gün ›</button>
        {!isToday && <button className="btn btn-sm" onClick={() => setDate(todayISO)}>Bugün</button>}
      </div>
      <div className="muted" style={{ fontSize: 13, marginBottom: 12, textTransform: 'capitalize' }}>
        {isToday ? 'Bugün' : 'Tarix'}: {dateLabel}{isToday ? ' · canlı' : ''}
      </div>

      {locations.length > 1 && (
        <div className="chip-row">
          <span className={`chip${!filterLoc ? ' active' : ''}`} onClick={() => setFilterLoc(null)}>
            Hamısı
          </span>
          {locations.map((l) => (
            <span
              key={l.id}
              className={`chip${filterLoc === l.id ? ' active' : ''}`}
              onClick={() => setFilterLoc(l.id)}
            >
              {l.name}
            </span>
          ))}
        </div>
      )}

      <div className="chip-row">
        <span className={`chip${!flaggedOnly ? ' active' : ''}`} onClick={() => setFlaggedOnly(false)}>
          Bütün işçilər
        </span>
        <span className={`chip${flaggedOnly ? ' active' : ''}`} onClick={() => setFlaggedOnly(true)}>
          ⚠ Yalnız bayraqlananlar{flaggedCount > 0 ? ` (${flaggedCount})` : ''}
        </span>
      </div>

      <div className="stat-grid">
        <div className="stat-card leaf">
          <div className="stat-lbl">{STATUS_MAP.OnTime.label}</div>
          <div className="stat-val">{counts.present}</div>
        </div>
        <div className="stat-card clay">
          <div className="stat-lbl">{STATUS_MAP.Absent.label}</div>
          <div className="stat-val">{counts.absent}</div>
        </div>
        <div className="stat-card blue">
          <div className="stat-lbl">{STATUS_MAP.Incomplete.label}</div>
          <div className="stat-val">{counts.incomplete}</div>
        </div>
        <div className="stat-card purple">
          <div className="stat-lbl">{STATUS_MAP.DayOff.label}</div>
          <div className="stat-val">{counts.dayOff}</div>
        </div>
        <div className="stat-card purple">
          <div className="stat-lbl">{STATUS_MAP.OnLeave.label}</div>
          <div className="stat-val">{counts.onLeave}</div>
        </div>
        <div className="stat-card">
          <div className="stat-lbl">{STATUS_MAP.Permission.label}</div>
          <div className="stat-val">{counts.permission}</div>
        </div>
      </div>

      {error && (
        <div className="fb fb-err" style={{ marginBottom: 12 }}>
          <IconX />
          <span>{error}</span>
        </div>
      )}
      {photoError && (
        <div className="fb fb-err" style={{ marginBottom: 12 }}>
          <IconX />
          <span>{photoError}</span>
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
              <th>Foto</th>
              <th>Üz</th>
            </tr>
          </thead>
          <tbody>
            {visible.map((r) => (
              <tr key={r.employeeId}>
                <td style={{ fontWeight: 700, color: 'var(--c900)' }}>{r.employeeName}</td>
                <td>{r.locationName}</td>
                <td>
                  <StatusBadge status={r.status} />
                </td>
                <td className="mono">{fmtTime(r.checkInAtUtc)}</td>
                <td className="mono">{fmtTime(r.checkOutAtUtc)}</td>
                <td>
                  {r.hasPhoto && r.recordId ? (
                    <button
                      className="btn btn-sm"
                      disabled={busyId === r.recordId}
                      onClick={() => void viewPhoto(r)}
                    >
                      <IconCamera /> {busyId === r.recordId ? '…' : 'Şəkli gör'}
                    </button>
                  ) : (
                    <span className="muted">—</span>
                  )}
                </td>
                <td>
                  <FaceFlagBadge status={r.faceMatchStatus} score={r.faceMatchScore} />
                </td>
              </tr>
            ))}
            {loadedOnce && visible.length === 0 && !error && (
              <tr>
                <td colSpan={7} className="muted" style={{ textAlign: 'center', padding: 28 }}>
                  Məlumat yoxdur
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      {modal && (
        <PhotoCompareModal
          title={modal.title}
          referenceUrl={modal.photo.referencePhotoUrl}
          checkInUrl={modal.photo.checkInPhotoUrl}
          checkInTakenAtUtc={modal.photo.checkInPhotoTakenAtUtc}
          faceMatchStatus={modal.photo.faceMatchStatus}
          faceMatchScore={modal.photo.faceMatchScore}
          onClose={() => setModal(null)}
        />
      )}
    </div>
  )
}

function fmtTime(iso: string | null): string {
  return iso ? new Date(iso).toLocaleTimeString('az-AZ', { hour: '2-digit', minute: '2-digit' }) : '—'
}
