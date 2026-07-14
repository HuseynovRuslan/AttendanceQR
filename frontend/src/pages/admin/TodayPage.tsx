import { useCallback, useEffect, useMemo, useState } from 'react'
import { exportDayXlsx, getToday, type DayAttendanceRow } from '../../api/admin'
import { getPhotoUrl, type PhotoUrlResponse } from '../../api/attendance'
import { StatusBadge, STATUS_MAP } from '../../components/StatusBadge'
import { PhotoCompareModal } from '../../components/PhotoCompareModal'
import { FaceFlagBadge, faceIsFlagged } from '../../components/FaceFlagBadge'
import { IconCamera, IconX } from '../../components/icons'

function localDateISO(d: Date): string {
  const p = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`
}

// Does a row's status belong to the clicked stat-card bucket? Mirrors the counts grouping (Late folds
// into present; "incomplete" is everything not one of the five named statuses).
function statusMatches(status: string, filter: string): boolean {
  switch (filter) {
    case 'present':
      return status === 'OnTime' || status === 'Late'
    case 'absent':
      return status === 'Absent'
    case 'dayOff':
      return status === 'DayOff'
    case 'onLeave':
      return status === 'OnLeave'
    case 'permission':
      return status === 'Permission'
    case 'incomplete':
      return !['OnTime', 'Late', 'Absent', 'DayOff', 'OnLeave', 'Permission'].includes(status)
    default:
      return true
  }
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
  const [statusFilter, setStatusFilter] = useState<string | null>(null)
  const [search, setSearch] = useState('')
  const [noPhotoOnly, setNoPhotoOnly] = useState(false)
  const [exporting, setExporting] = useState(false)

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

  // Counts reflect the LOCATION scope only (not the status/search/photo filters), so the cards keep
  // showing the day's real breakdown and stay usable as toggles.
  const counts = { present: 0, absent: 0, incomplete: 0, dayOff: 0, onLeave: 0, permission: 0 }
  for (const r of locFiltered) {
    // Late folds into present — see StatusBadge: there is no per-employee schedule to be late against.
    if (r.status === 'OnTime' || r.status === 'Late') counts.present++
    else if (r.status === 'Absent') counts.absent++
    else if (r.status === 'DayOff') counts.dayOff++
    else if (r.status === 'OnLeave') counts.onLeave++
    else if (r.status === 'Permission') counts.permission++
    else counts.incomplete++
  }
  const flaggedCount = locFiltered.filter((r) => faceIsFlagged(r.faceMatchStatus)).length

  const q = search.trim().toLowerCase()
  const visible = locFiltered.filter((r) => {
    if (flaggedOnly && !faceIsFlagged(r.faceMatchStatus)) return false
    if (statusFilter && !statusMatches(r.status, statusFilter)) return false
    // "No photo" = checked in but the selfie is missing (an absentee having no photo is not notable).
    if (noPhotoOnly && !(r.checkInAtUtc && !r.hasPhoto)) return false
    if (q && !r.employeeName.toLowerCase().includes(q)) return false
    return true
  })

  const toggleStatus = (k: string) => setStatusFilter((f) => (f === k ? null : k))
  const cardStyle = (k: string) =>
    statusFilter === k
      ? { cursor: 'pointer', boxShadow: '0 0 0 2px #1E70C8' }
      : { cursor: 'pointer' }

  async function exportXlsx() {
    const label = (st: string) => (STATUS_MAP as Record<string, { label: string }>)[st]?.label ?? st
    const rows = visible.map((r) => ({
      name: r.employeeName,
      location: r.locationName,
      status: label(r.status),
      checkIn: fmtTime(r.checkInAtUtc) + (r.lateArrivalReason ? ` (gec: ${r.lateArrivalReason})` : ''),
      checkOut: fmtTime(r.checkOutAtUtc) + (r.earlyDepartureReason ? ` (tez: ${r.earlyDepartureReason})` : ''),
      photo: r.hasPhoto ? 'var' : r.checkInAtUtc ? 'yox' : '—',
    }))
    setExporting(true)
    const ok = await exportDayXlsx({ title: `Davamiyyət — ${dateLabel}`, date, rows })
    setExporting(false)
    if (!ok) setPhotoError('Excel çıxarıla bilmədi')
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
        <span className={`chip${noPhotoOnly ? ' active' : ''}`} onClick={() => setNoPhotoOnly((v) => !v)}>
          📷 Şəkilsizlər
        </span>
      </div>

      <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 12, flexWrap: 'wrap' }}>
        <input
          type="text"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="Ad üzrə axtar…"
          className="inp"
          style={{ width: 'auto', maxWidth: 220, padding: '6px 10px' }}
        />
        {search && (
          <button className="btn btn-sm" onClick={() => setSearch('')}>Təmizlə</button>
        )}
        <button className="btn btn-sm" disabled={exporting} onClick={exportXlsx} style={{ marginLeft: 'auto' }}>
          {exporting ? 'Çıxarılır…' : '⬇ Excel-ə çıxar'}
        </button>
      </div>

      <div className="stat-grid">
        <div className="stat-card leaf" style={cardStyle('present')} onClick={() => toggleStatus('present')}>
          <div className="stat-lbl">{STATUS_MAP.OnTime.label}</div>
          <div className="stat-val">{counts.present}</div>
        </div>
        <div className="stat-card clay" style={cardStyle('absent')} onClick={() => toggleStatus('absent')}>
          <div className="stat-lbl">{STATUS_MAP.Absent.label}</div>
          <div className="stat-val">{counts.absent}</div>
        </div>
        <div className="stat-card blue" style={cardStyle('incomplete')} onClick={() => toggleStatus('incomplete')}>
          <div className="stat-lbl">{STATUS_MAP.Incomplete.label}</div>
          <div className="stat-val">{counts.incomplete}</div>
        </div>
        <div className="stat-card purple" style={cardStyle('dayOff')} onClick={() => toggleStatus('dayOff')}>
          <div className="stat-lbl">{STATUS_MAP.DayOff.label}</div>
          <div className="stat-val">{counts.dayOff}</div>
        </div>
        <div className="stat-card purple" style={cardStyle('onLeave')} onClick={() => toggleStatus('onLeave')}>
          <div className="stat-lbl">{STATUS_MAP.OnLeave.label}</div>
          <div className="stat-val">{counts.onLeave}</div>
        </div>
        <div className="stat-card" style={cardStyle('permission')} onClick={() => toggleStatus('permission')}>
          <div className="stat-lbl">{STATUS_MAP.Permission.label}</div>
          <div className="stat-val">{counts.permission}</div>
        </div>
      </div>
      {statusFilter && (
        <div className="muted" style={{ fontSize: 12, marginTop: 6 }}>
          Süzgəc aktiv — kartı təkrar basıb ləğv edin.
        </div>
      )}

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
                <td className="mono">
                  {fmtTime(r.checkInAtUtc)}
                  {r.lateArrivalReason && (
                    <div style={{ fontSize: 11, color: 'var(--amber)', fontWeight: 600, marginTop: 2 }}>
                      Gec: {r.lateArrivalReason}
                    </div>
                  )}
                </td>
                <td className="mono">
                  {fmtTime(r.checkOutAtUtc)}
                  {r.earlyDepartureReason && (
                    <div style={{ fontSize: 11, color: 'var(--amber)', fontWeight: 600, marginTop: 2 }}>
                      Tez: {r.earlyDepartureReason}
                    </div>
                  )}
                </td>
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
