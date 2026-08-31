import { useCallback, useEffect, useMemo, useState, type MouseEvent } from 'react'
import { countToday, matchesLeaveCard, sortRows, type SortColumn } from './todayCounts'
import { useSearchParams } from 'react-router-dom'
import { EmployeeLink } from '../../components/EmployeeLink'
import { exportDayXlsx, getToday, type DayAttendanceRow } from '../../api/admin'
import { addLeave, deleteLeave, type LeaveType } from '../../api/leaves'
import { createManagerLeave, deleteManagerLeave } from '../../api/manager'
import { useAuth } from '../../auth/AuthContext'
import { getPhotoUrl, type PhotoUrlResponse } from '../../api/attendance'
import { StatusBadge, STATUS_MAP, leaveVisual } from '../../components/StatusBadge'
import { PhotoCompareModal } from '../../components/PhotoCompareModal'
import { FaceFlagBadge, faceIsFlagged } from '../../components/FaceFlagBadge'
import { IconCamera, IconPencil, IconX } from '../../components/icons'
import { fmtTime } from '../../lib/format'

function localDateISO(d: Date): string {
  const p = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`
}

// Does a row's status belong to the clicked stat-card bucket? Mirrors the counts grouping (Late folds
// into present; "incomplete" is everything not one of the five named statuses).
function statusMatches(status: string, filter: string): boolean {
  switch (filter) {
    case 'present':
      return status === 'OnTime' || status === 'Late' || status === 'Field'
    case 'absent':
      return status === 'Absent'
    case 'pending':
      return status === 'Pending'
    case 'dayOff':
      return status === 'DayOff'
    case 'onLeave':
      return status === 'OnLeave'
    case 'permission':
      return status === 'Permission'
    case 'incomplete':
      return !['OnTime', 'Late', 'Field', 'Absent', 'Pending', 'DayOff', 'OnLeave', 'Permission'].includes(status)
    default:
      return true
  }
}

// The reasons an admin/manager can pin on a Qayıb row — each with the colour dot that matches the
// badge it becomes (İcazə green, Məzuniyyət purple, Xəstəlik blue, Ödənişsiz amber, İstirahət grey).
const LEAVE_OPTIONS: { type: LeaveType; label: string; dot: string }[] = [
  { type: 'Permission', label: 'İcazə', dot: 'var(--amber)' },
  { type: 'Vacation', label: 'Məzuniyyət', dot: 'var(--purple)' },
  { type: 'Sick', label: 'Xəstəlik', dot: 'var(--blue)' },
  { type: 'Unpaid', label: 'Ödənişsiz', dot: 'var(--clay)' },
  { type: 'Rest', label: 'İstirahət', dot: 'var(--c400)' },
  { type: 'BusinessTrip', label: 'Ezamiyyət', dot: 'var(--teal)' },
]

/** A heading that sorts. The arrow only appears on the column actually in use. */
function Th({ col, label, sortBy, desc, onSort }: {
  col: SortColumn
  label: string
  sortBy: string
  desc: boolean
  onSort: (c: SortColumn) => void
}) {
  const active = sortBy === col
  return (
    <th>
      <button className={`tbl-sort${active ? ' active' : ''}`} onClick={() => onSort(col)}>
        {label}<span className="tbl-sort-a">{active ? (desc ? '↓' : '↑') : ''}</span>
      </button>
    </th>
  )
}

export function TodayPage() {
  const { role } = useAuth()
  // Viewing a check-in selfie is Admin-only (owner's call, 2026-08-31) — the server refuses a
  // manager outright, so these controls would be dead buttons rather than hidden powers.
  const mayViewPhotos = role === 'Admin'
  const [assigningId, setAssigningId] = useState<string | null>(null)
  // Which absent row's reason menu is open, and where to float it. A pencil next to the Qayıb badge
  // opens a dropdown; it is position:fixed so the table's overflow never clips it.
  const [reasonFor, setReasonFor] = useState<string | null>(null)
  const [menuPos, setMenuPos] = useState<{ top: number; left: number } | null>(null)

  function openReasonMenu(e: MouseEvent, employeeId: string) {
    const r = (e.currentTarget as HTMLElement).getBoundingClientRect()
    // Clamp so a menu near the right/bottom edge stays on screen.
    setMenuPos({ top: Math.min(r.bottom + 4, window.innerHeight - 250), left: Math.min(r.left, window.innerWidth - 210) })
    setReasonFor(employeeId)
  }
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
  // A caller can deep-link a pre-applied status filter, e.g. the dashboard's "Bu gün gəlməyib" →
  // /admin/today?status=absent. Read once at mount.
  const [searchParams] = useSearchParams()
  const [statusFilter, setStatusFilter] = useState<string | null>(() => searchParams.get('status'))
  const [search, setSearch] = useState('')
  const [noPhotoOnly, setNoPhotoOnly] = useState(false)
  /**
   * Sorting and the two value filters the table itself offers.
   *
   * The board is read one way in the morning — "who is missing" — and the answer is almost never
   * about the whole site. It is about the gardeners, or one branch, or everyone still marked absent.
   * The cards above already filter by STATUS; a job title and a branch had no equivalent, and the
   * data was on screen the whole time in a column nobody could press.
   */
  const [sortBy, setSortBy] = useState<SortColumn>('name')
  const [sortDesc, setSortDesc] = useState(false)
  const [filterPosition, setFilterPosition] = useState<string | null>(null)
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

  // Assign a reason to an absent employee straight from this board: a single-day leave for the date
  // being viewed. Managers file through their own scoped endpoint, admins through the admin one — the
  // server re-checks scope either way. The board then reloads and the row flips from Qayıb to its
  // real reason.
  async function assignLeave(employeeId: string, type: LeaveType, existingLeaveId?: string | null) {
    setAssigningId(employeeId)
    // Changing an already-assigned reason: drop the old single-day leave first, then add the new one.
    if (existingLeaveId) await (role === 'Manager' ? deleteManagerLeave(existingLeaveId) : deleteLeave(existingLeaveId))
    const res = role === 'Manager'
      ? await createManagerLeave({ employeeIds: [employeeId], fromDate: date, toDate: date, type, note: null })
      : await addLeave({ employeeIds: [employeeId], fromDate: date, toDate: date, type })
    setAssigningId(null)
    setReasonFor(null)
    if (res.status === 200) await load()
  }

  // Undo a mistaken reason — delete the single-day leave so the row goes back to Qayıb.
  async function removeLeave(employeeId: string, leaveId: string) {
    setAssigningId(employeeId)
    const res = role === 'Manager' ? await deleteManagerLeave(leaveId) : await deleteLeave(leaveId)
    setAssigningId(null)
    setReasonFor(null)
    if (res.status === 200) await load()
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
  // present = checked in AND out ("Tamamlayıb"). incomplete = checked in, no check-out yet — reads as
  // "İşdə" (still at work) on today's board, or "Çıxış yoxdur" (forgot to check out) on a past date.
  // Bucketing lives in ./todayCounts, with tests. Every kind of leave arrives as one status
  // (`OnLeave`) and is separable only by `leaveType`, so a screen that counts by status merges a
  // work trip into the holidays — which is what this board did, and what the reports did before
  // 3d6ac7e. Twice is enough for it to belong somewhere a test can see it.
  const counts = countToday(locFiltered)
  const flaggedCount = locFiltered.filter((r) => faceIsFlagged(r.faceMatchStatus)).length
  const incompleteLabel = isToday ? 'İşdə' : 'Çıxış yoxdur'
  const incompleteOverride = isToday ? undefined : { cls: 'b-absent', label: 'Çıxış yoxdur', icon: 'x' as const }

  const q = search.trim().toLowerCase()
  const visible = sortRows(locFiltered.filter((r) => {
    if (flaggedOnly && !faceIsFlagged(r.faceMatchStatus)) return false
    // Sick / Ezamiyyət / Məzuniyyət all come from OnLeave, split by leaveType — so their filters
    // need the row, not just the status.
    if (statusFilter === 'sick' || statusFilter === 'trip' || statusFilter === 'onLeave') {
      if (!matchesLeaveCard(r, statusFilter)) return false
    } else if (statusFilter && !statusMatches(r.status, statusFilter)) return false
    // "No photo" = checked in but the selfie is missing (an absentee having no photo is not notable).
    if (filterPosition && (r.position ?? '') !== filterPosition) return false
    if (noPhotoOnly && !(r.checkInAtUtc && !r.hasPhoto)) return false
    if (q && !r.employeeName.toLowerCase().includes(q)) return false
    return true
  }), sortBy, sortDesc)

  // Same column twice reverses it; a new column starts ascending, which is what every table does.
  const sort = (c: typeof sortBy) => {
    if (c === sortBy) setSortDesc((d) => !d)
    else { setSortBy(c); setSortDesc(false) }
  }

  const toggleStatus = (k: string) => setStatusFilter((f) => (f === k ? null : k))
  const cardStyle = (k: string) =>
    statusFilter === k
      ? { cursor: 'pointer', boxShadow: '0 0 0 2px #1E70C8' }
      : { cursor: 'pointer' }

  async function exportXlsx() {
    const label = (st: string) =>
      st === 'Incomplete' ? incompleteLabel : (STATUS_MAP as Record<string, { label: string }>)[st]?.label ?? st
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
        <span
          className={`chip${flaggedOnly ? ' active' : ''}`}
          onClick={() => setFlaggedOnly(true)}
          title="Giriş şəklindəki üz referans şəkillə uyğun gəlməyən — yoxlanmalı girişlər"
        >
          ⚠ Üzü uyğun gəlməyənlər{flaggedCount > 0 ? ` (${flaggedCount})` : ''}
        </span>
        {mayViewPhotos && (
          <span className={`chip${noPhotoOnly ? ' active' : ''}`} onClick={() => setNoPhotoOnly((v) => !v)}>
            📷 Şəkilsizlər
          </span>
        )}
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
        <div
          className={`stat-card ${isToday ? 'blue' : 'clay'}`}
          style={cardStyle('incomplete')}
          onClick={() => toggleStatus('incomplete')}
        >
          <div className="stat-lbl">{incompleteLabel}</div>
          <div className="stat-val">{counts.incomplete}</div>
          <div className="stat-sub">{isToday ? 'Hazırda işdədir, çıxışı yoxdur' : 'Gün bitib, çıxış qeydə alınmayıb'}</div>
        </div>
        <div className="stat-card clay" style={cardStyle('absent')} onClick={() => toggleStatus('absent')}>
          <div className="stat-lbl">{STATUS_MAP.Absent.label}</div>
          <div className="stat-val">{counts.absent}</div>
          <div className="stat-sub">Heç giriş etməyib</div>
        </div>
        {/* Shown only when someone is actually pending — an empty card on a day with no night shift
            would be clutter. Neutral, next to Qayıb, so a not-yet-due worker never reads as a no-show. */}
        {isToday && counts.pending > 0 && (
          <div className="stat-card slate" style={cardStyle('pending')} onClick={() => toggleStatus('pending')}>
            <div className="stat-lbl">{STATUS_MAP.Pending.label}</div>
            <div className="stat-val">{counts.pending}</div>
            <div className="stat-sub">Növbəsi hələ başlamayıb</div>
          </div>
        )}
        <div className="stat-card leaf" style={cardStyle('present')} onClick={() => toggleStatus('present')}>
          <div className="stat-lbl">{STATUS_MAP.OnTime.label}</div>
          <div className="stat-val">{counts.present}</div>
          <div className="stat-sub">Giriş və çıxış edib</div>
        </div>
        <div className="stat-card purple" style={cardStyle('dayOff')} onClick={() => toggleStatus('dayOff')}>
          <div className="stat-lbl">{STATUS_MAP.DayOff.label}</div>
          <div className="stat-val">{counts.dayOff}</div>
          <div className="stat-sub">Bu gün iş günü deyil</div>
        </div>
        <div className="stat-card purple" style={cardStyle('onLeave')} onClick={() => toggleStatus('onLeave')}>
          <div className="stat-lbl">{STATUS_MAP.OnLeave.label}</div>
          <div className="stat-val">{counts.onLeave}</div>
          <div className="stat-sub">Təsdiqlənmiş məzuniyyətdədir</div>
        </div>
        {counts.sick > 0 && (
          <div className="stat-card blue" style={cardStyle('sick')} onClick={() => toggleStatus('sick')}>
            <div className="stat-lbl">Xəstəlik</div>
            <div className="stat-val">{counts.sick}</div>
            <div className="stat-sub">Xəstəlik məzuniyyətindədir</div>
          </div>
        )}
        {/* Shown only when somebody is on one, like Xəstəlik — a permanent 0 is a tile you read and
            discard every morning. The wording says the thing that matters about it: they are working. */}
        {counts.trip > 0 && (
          <div className="stat-card teal" style={cardStyle('trip')} onClick={() => toggleStatus('trip')}>
            <div className="stat-lbl">Ezamiyyət</div>
            <div className="stat-val">{counts.trip}</div>
            <div className="stat-sub">İş səfərindədir — qayıb sayılmır</div>
          </div>
        )}
        <div className="stat-card amber" style={cardStyle('permission')} onClick={() => toggleStatus('permission')}>
          <div className="stat-lbl">{STATUS_MAP.Permission.label}</div>
          <div className="stat-val">{counts.permission}</div>
          <div className="stat-sub">Təsdiqlənmiş icazəlidir</div>
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

      {filterPosition && (
        <div className="fb fb-info" style={{ marginBottom: 10 }}>
          <span>
            Yalnız <b>{filterPosition}</b> vəzifəsindəkilər — {visible.length} nəfər.
          </span>
          <button className="btn btn-sm" style={{ marginLeft: 'auto' }} onClick={() => setFilterPosition(null)}>
            Süzgəci ləğv et
          </button>
        </div>
      )}

      <div className="tbl-wrap tbl-cards">
        <table>
          <thead>
            <tr>
              {/* Clicking a heading sorts by it; clicking it again reverses. The two columns that are
                  really categories — branch and job — also filter when their VALUE is clicked, below. */}
              <Th col="name" label="İşçi" sortBy={sortBy} desc={sortDesc} onSort={sort} />
              <Th col="location" label="Filial" sortBy={sortBy} desc={sortDesc} onSort={sort} />
              <Th col="position" label="Vəzifə" sortBy={sortBy} desc={sortDesc} onSort={sort} />
              <Th col="status" label="Status" sortBy={sortBy} desc={sortDesc} onSort={sort} />
              <Th col="in" label="Giriş" sortBy={sortBy} desc={sortDesc} onSort={sort} />
              <Th col="out" label="Çıxış" sortBy={sortBy} desc={sortDesc} onSort={sort} />
              <th>Foto</th>
              <th>Üz</th>
            </tr>
          </thead>
          <tbody>
            {visible.map((r) => (
              <tr key={r.employeeId}>
                <td data-label="İşçi" style={{ fontWeight: 700, color: 'var(--c900)' }}><EmployeeLink id={r.employeeId} name={r.employeeName} /></td>
                <td data-label="Filial">
                  {/* The value is the filter. The branch picker above does the same thing, but a name
                      already on screen is the shortest way to ask "just these". */}
                  <button className="tbl-filter" onClick={() => setFilterLoc((v) => (v === r.locationId ? '' : r.locationId))}>
                    {r.locationName}
                  </button>
                </td>
                <td data-label="Vəzifə">
                  {r.position
                    ? (
                      <button
                        className="tbl-filter"
                        onClick={() => setFilterPosition((v) => (v === r.position ? null : r.position ?? null))}
                      >
                        {r.position}
                      </button>
                    )
                    : <span className="muted">—</span>}
                </td>
                <td data-label="Status">
                  {/* Pencil next to the badge on a Qayıb row (to pin a reason) or an assigned single-day
                      leave (to change it, or revert to Qayıb). Menu is fixed so the table can't clip it. */}
                  <span style={{ display: 'inline-flex', alignItems: 'center', gap: 2 }}>
                    <StatusBadge
                      status={r.status}
                      override={
                        r.status === 'Incomplete'
                          ? incompleteOverride
                          : r.status === 'OnLeave'
                            ? leaveVisual(r.leaveType)
                            : undefined
                      }
                    />
                    {(r.status === 'Absent' || ((r.status === 'OnLeave' || r.status === 'Permission') && r.leaveId)) && (
                      assigningId === r.employeeId ? (
                        <span className="muted" style={{ marginLeft: 6, fontSize: 12 }}>…</span>
                      ) : (
                        <button className="reason-pencil" title="Səbəb təyin et / dəyiş" onClick={(e) => openReasonMenu(e, r.employeeId)}>
                          <IconPencil />
                        </button>
                      )
                    )}
                    {reasonFor === r.employeeId && menuPos && (
                      <>
                        <div className="reason-backdrop" onClick={() => setReasonFor(null)} />
                        <div className="reason-pop" style={{ top: menuPos.top, left: menuPos.left }}>
                          <div className="reason-pop-h">Səbəb seçin</div>
                          {LEAVE_OPTIONS.map((o) => (
                            <button key={o.type} className="reason-pop-item" onClick={() => void assignLeave(r.employeeId, o.type, r.leaveId)}>
                              <span className="reason-dot" style={{ background: o.dot }} />
                              {o.label}
                            </button>
                          ))}
                          {r.leaveId && (
                            <button className="reason-pop-item" style={{ color: 'var(--clay)' }} onClick={() => void removeLeave(r.employeeId, r.leaveId!)}>
                              <span className="reason-dot" style={{ background: 'var(--clay)' }} />
                              Qayıba qaytar
                            </button>
                          )}
                        </div>
                      </>
                    )}
                  </span>
                  {/* Who pinned this reason — for every assigned leave (Məzuniyyət, İcazə, …). */}
                  {r.leaveAssignedBy && (
                    <div className="muted" style={{ fontSize: 11, marginTop: 4 }}>
                      Təyin edən: {r.leaveAssignedBy}
                    </div>
                  )}
                  {/* This giriş-çıxış was entered/changed by hand, not scanned — attribute it. */}
                  {r.manualBy && (
                    <div style={{ fontSize: 11, marginTop: 4, color: 'var(--amber)' }}>
                      Əl ilə daxil edilib · {r.manualBy}
                    </div>
                  )}
                  {/* Not a manual entry and not a poster scan: the worker closed their own field visit
                      and went home, which closed this day at the moment they left the site. */}
                  {r.closedByFieldVisit && (
                    <div style={{ fontSize: 11, marginTop: 4, color: 'var(--c600)' }}>
                      📍 Ərazi çıxışı ilə bağlandı
                    </div>
                  )}
                </td>
                <td className="mono" data-label="Giriş">
                  {fmtTime(r.checkInAtUtc ?? r.fieldCheckInAtUtc)}
                  {r.status === 'Field' && (
                    <span className="tag" title="Sahə ziyarəti — GPS ilə" style={{ marginLeft: 6, background: 'var(--leaf-bg)', color: 'var(--leaf-d)' }}>📍 sahə</span>
                  )}
                  {r.wasOffline && (
                    <span
                      className="tag"
                      title="Oflayn qeydə alınıb — vaxt telefonun saatı ilədir"
                      style={{ marginLeft: 6, background: 'var(--amber-bg)', color: 'var(--amber)' }}
                    >
                      📴 oflayn
                    </span>
                  )}
                  {r.lateArrivalReason && (
                    <div style={{ fontSize: 11, color: 'var(--amber)', fontWeight: 600, marginTop: 2 }}>
                      Gec: {r.lateArrivalReason}
                    </div>
                  )}
                </td>
                <td className="mono" data-label="Çıxış">
                  {fmtTime(r.checkOutAtUtc ?? r.fieldCheckOutAtUtc)}
                  {r.earlyDepartureReason && (
                    <div style={{ fontSize: 11, color: 'var(--amber)', fontWeight: 600, marginTop: 2 }}>
                      Tez: {r.earlyDepartureReason}
                    </div>
                  )}
                </td>
                <td data-label="Foto">
                  {/* Şəkli olan HƏR sətirdə (sahibin qərarı, 2026-08-31). Əvvəl yalnız üz-uyğunsuzluğu
                      flaqlı və ortaq telefonlu sətirlərdə göstərilirdi; səbəb R2-dən yüklənmə gecikməsi
                      idi, o isə burada tətbiq olunmur — şəkil YALNIZ düyməyə basanda çəkilir, düymənin
                      özü heç nə yükləmir. Yəni məhdudiyyət xərci azaltmırdı, sadəcə adminin baxa
                      biləcəyi sətirləri azaldırdı. Menecerdə hələ də görünmür: `mayViewPhotos`. */}
                  {mayViewPhotos && r.hasPhoto && r.recordId ? (
                    <button
                      className="btn btn-sm"
                      disabled={busyId === r.recordId}
                      onClick={() => void viewPhoto(r)}
                      title="Giriş şəklini gör"
                      aria-label="Giriş şəklini gör"
                    >
                      {busyId === r.recordId ? '…' : <IconCamera />}
                    </button>
                  ) : (
                    <span className="muted">—</span>
                  )}
                </td>
                <td data-label="Üz">
                  <FaceFlagBadge status={r.faceMatchStatus} score={r.faceMatchScore} />
                </td>
              </tr>
            ))}
            {loadedOnce && visible.length === 0 && !error && (
              <tr>
                <td colSpan={8} className="muted" style={{ textAlign: 'center', padding: 28 }}>
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

