import { Fragment, useCallback, useEffect, useMemo, useState, type MouseEvent } from 'react'
import { bucketOf, countToday, matchesLeaveCard, sortRows, type SortColumn } from './todayCounts'
import { exportRow } from './exportRows'
import { useSearchParams } from 'react-router-dom'
import { EmployeeLink } from '../../components/EmployeeLink'
import { exportDayXlsx, getToday, markAbsent, unmarkAbsent, type DayAttendanceRow } from '../../api/admin'
import { getImpersonation } from '../../api/client'
import { addLeave, deleteLeave, type LeaveType } from '../../api/leaves'
import { createManagerLeave, deleteManagerLeave } from '../../api/manager'
import { useAuth } from '../../auth/AuthContext'
import { getPhotoUrl, type PhotoUrlResponse } from '../../api/attendance'
import { StatusBadge, STATUS_MAP, leaveVisual } from '../../components/StatusBadge'
import { PhotoCompareModal } from '../../components/PhotoCompareModal'
import { FaceFlagBadge, faceIsFlagged } from '../../components/FaceFlagBadge'
import { IconCamera, IconPencil, IconX } from '../../components/icons'
import { fmtLongDate, fmtTime, toCompanyInputValue } from '../../lib/format'

function localDateISO(d: Date): string {
  const p = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`
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
  // A manager may look at the selfies of the people they manage again (owner's call, 2026-09-04):
  // they are who stands at the site and notices, and routing every suspect photograph through one
  // admin did not make the data safer, it made the check not happen. The server scopes it to their
  // own branches; ACTING on what they see — voiding a day, sending a warning — stays Admin-only, so
  // canAct below is deliberately narrower than this.
  const mayViewPhotos = role === 'Admin' || role === 'Manager'
  const mayAct = role === 'Admin'
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
  // The company's day, not the device's — and recomputed every render rather than frozen at mount.
  //
  // It was `localDateISO(new Date())` inside a useMemo with no deps, which is two bugs in one line: a
  // laptop on any other timezone opened the board on the wrong date (the rows come from the server's
  // Baku day, so the file was headed one day and filled with another's people), and a board left open
  // past midnight went on calling itself today — exporting the new day's rows under yesterday's date
  // and filename until somebody reloaded. Both are silent; both put the wrong date on a file sent to
  // the leadership.
  const todayISO = toCompanyInputValue(new Date().toISOString()).slice(0, 10)
  const [date, setDate] = useState(todayISO)
  const isToday = date === todayISO

  const [rows, setRows] = useState<DayAttendanceRow[]>([])
  const [error, setError] = useState<string | null>(null)
  const [loadedOnce, setLoadedOnce] = useState(false)
  const [filterLoc, setFilterLoc] = useState<string | null>(null)
  const [flaggedOnly, setFlaggedOnly] = useState(false)
  const [busyId, setBusyId] = useState<string | null>(null)
  const [photoError, setPhotoError] = useState<string | null>(null)
  const [modal, setModal] = useState<{ title: string; photo: PhotoUrlResponse; recordId: string | null } | null>(null)
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
  // Which sites go into the workbook. Chosen explicitly before every export: the file is sent to the
  // leadership, and one that quietly carried whatever filter happened to be on screen is a report
  // nobody can tell apart from the whole company.
  const [exportOpen, setExportOpen] = useState(false)
  const [exportSites, setExportSites] = useState<string[]>([])

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
    setModal({ title: row.employeeName, photo: data, recordId: row.recordId ?? null })
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

  // «Qayıb yaz» — say, in so many words, that this person did not come.
  //
  // Needed because the system stopped guessing. Somebody who has never recorded any attendance is no
  // longer written up as absent on their own (it was deducting a day's pay from people it could not
  // show were ever handed a working phone), so their Qayıb now comes from whoever watched the day.
  async function markDayAbsent(employeeId: string) {
    setAssigningId(employeeId)
    const res = await markAbsent(employeeId, date)
    setAssigningId(null)
    setReasonFor(null)
    if (res.status === 200) { await load(); return }
    const code = res.data && typeof res.data === 'object' && 'error' in res.data
      ? (res.data as { error: string }).error : ''
    setPhotoError(
      code === 'HasRecord' ? 'Bu gün skan var — qayıb yazmaq olmaz'
        : code === 'HasLeave' ? 'Bu gün üçün məzuniyyət/icazə var — əvvəlcə onu silin'
          : code === 'DateInFuture' ? 'Gələcək günə qayıb yazmaq olmaz'
            : 'Qayıb yazılmadı')
  }

  async function undoDayAbsent(employeeId: string) {
    setAssigningId(employeeId)
    const res = await unmarkAbsent(employeeId, date)
    setAssigningId(null)
    setReasonFor(null)
    if (res.status === 200) await load()
    else setPhotoError('Qayıb geri alınmadı')
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
    } else if (statusFilter && bucketOf(r) !== statusFilter) return false
    // "No photo" = checked in but the selfie is missing (an absentee having no photo is not notable).
    if (filterPosition && (r.position ?? '') !== filterPosition) return false
    if (noPhotoOnly && !(r.checkInAtUtc && !r.hasPhoto)) return false
    if (q && !r.employeeName.toLowerCase().includes(q)) return false
    return true
  }), sortBy, sortDesc)

  /**
   * The list, cut into branches.
   *
   * 221 rows in one run is not a board, it is a scroll — and the branch column was the same word
   * repeated forty times down the page while the reader looked for a name. Grouped, the word is said
   * once as a heading and the column disappears; ungrouped (a single branch already picked) nothing
   * changes, because there is nothing to say.
   */
  const grouped = !filterLoc
  const byBranch = grouped
    ? [...visible.reduce((m, r) => {
        const list = m.get(r.locationName)
        if (list) list.push(r); else m.set(r.locationName, [r])
        return m
      }, new Map<string, typeof visible>())].sort((a, b) => b[1].length - a[1].length || a[0].localeCompare(b[0], 'az'))
    : [['', visible] as [string, typeof visible]]

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

  // The exported Status must say EXACTLY what the badge beside it says.
  //
  // It did not. The file was built from `STATUS_MAP[r.status]`, and the backend stores Məzuniyyət,
  // Xəstəlik and Ezamiyyət under one status — OnLeave — so every one of them printed «Məzuniyyət».
  // On screen the same row already read correctly, because the badge passes leaveVisual(leaveType).
  // An admin therefore saw «Xəstəlik» on the board, pressed Excel, and got a file saying the same
  // person took annual leave on the same day. Worst of all «Ezamiyyət», which is WORK, exported as
  // leave. The type is already on the row and was simply never read here.
  const statusLabel = (r: DayAttendanceRow) =>
    r.status === 'Incomplete'
      ? incompleteLabel
      : r.status === 'OnLeave'
        ? leaveVisual(r.leaveType)?.label ?? 'Məzuniyyət'
        : (STATUS_MAP as Record<string, { label: string }>)[r.status]?.label ?? r.status


  function openExport() {
    // Pre-tick what the reader is already looking at: the site they filtered to, or all of them.
    setExportSites(filterLoc ? [filterLoc] : locations.map((l) => l.id))
    setExportOpen(true)
  }

  async function runExport() {
    const chosen = new Set(exportSites)
    // Deliberately built from `rows`, not from `visible`. The screen's search, status and photo
    // filters are how somebody LOOKS at a day; a report to the leadership has to be everybody at the
    // sites it claims to cover, or a file headed «Qala Anbar» silently omits the nine people who did
    // not match a search box left over from ten minutes ago.
    const picked = sortRows(rows.filter((r) => chosen.has(r.locationId)), 'name', false)
    // One line per person, shaped by exportRows — pure, and tested against the cases an audit of this
    // report actually found wrong: a night that read backwards, a carried-over night that read as this
    // morning, and a field worker exported with no times at all.
    const payload = picked.map((r) => exportRow(r, date, statusLabel(r)))

    // Never «Bütün ərazilər». `locations` is only what is on THIS board: a branch manager sees their
    // own branches, so the phrase would stamp a two-site file as the whole company, and even for an
    // admin a site whose staff are all inactive never appears. The sites are named instead, and once
    // there are too many to name the count stands — the Xülasə sheet lists every one of them anyway.
    const names = locations.filter((l) => chosen.has(l.id)).map((l) => l.name)
    const scopeNote = names.length <= 6
      ? `${names.length} ərazi: ${names.join(', ')} · ${picked.length} işçi`
      : `${names.length} ərazi · ${picked.length} işçi (siyahı «Xülasə» vərəqindədir)`

    setExporting(true)
    const ok = await exportDayXlsx({
      title: `Davamiyyət — ${dateLabel}`,
      date,
      rows: payload,
      scopeNote,
      // The sheet's headings are the BOARD's, sent rather than repeated server-side. Three of them
      // had already drifted — it said «Gəlib» where this screen says «Tamamlayıb», a word retired on
      // purpose because it read as though somebody still at work had not come.
      bucketLabels: {
        present: STATUS_MAP.OnTime.label,
        incomplete: incompleteLabel,
        absent: STATUS_MAP.Absent.label,
        onLeave: STATUS_MAP.OnLeave.label,
        sick: 'Xəstəlik',
        trip: 'Ezamiyyət',
        permission: STATUS_MAP.Permission.label,
        dayOff: STATUS_MAP.DayOff.label,
        pending: STATUS_MAP.Pending.label,
        onboarding: STATUS_MAP.Onboarding.label,
      },
    })
    setExporting(false)
    if (ok) setExportOpen(false)
    else setPhotoError('Excel çıxarıla bilmədi')
  }

  const dateLabel = fmtLongDate(date)

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
        <button className="btn btn-sm" disabled={exporting} onClick={openExport} style={{ marginLeft: 'auto' }}>
          {exporting ? 'Çıxarılır…' : '⬇ Excel-ə çıxar'}
        </button>
      </div>

      <div className="stat-grid day-tiles">
        <div
          className={`stat-card ${isToday ? 'blue' : 'clay'}`}
          style={cardStyle('incomplete')}
          onClick={() => toggleStatus('incomplete')}
        >
          <div className="stat-lbl">{incompleteLabel}</div>
          <div className="stat-val">{counts.incomplete}</div>
        </div>
        <div className="stat-card clay" style={cardStyle('absent')} onClick={() => toggleStatus('absent')}>
          <div className="stat-lbl">{STATUS_MAP.Absent.label}</div>
          <div className="stat-val">{counts.absent}</div>
        </div>
        {/* Shown only when someone is actually pending — an empty card on a day with no night shift
            would be clutter. Neutral, next to Qayıb, so a not-yet-due worker never reads as a no-show. */}
        {isToday && counts.pending > 0 && (
          <div className="stat-card slate" style={cardStyle('pending')} onClick={() => toggleStatus('pending')}>
            <div className="stat-lbl">{STATUS_MAP.Pending.label}</div>
            <div className="stat-val">{counts.pending}</div>
          </div>
        )}
        {/* Yalnız biri belə olanda görünür, Xəstəlik kimi. Qayıbdan QƏSDƏN ayrıdır: import olunmuş,
            telefonu hələ qurulmamış adam gəlməyən adam deyil — və 290-ı bir səhər Qayıb sayılanda
            əsl 67 qayıb səs-küydə itmişdi. */}
        {counts.onboarding > 0 && (
          <div className="stat-card slate" style={cardStyle('onboarding')} onClick={() => toggleStatus('onboarding')}>
            <div className="stat-lbl">{STATUS_MAP.Onboarding.label}</div>
            <div className="stat-val">{counts.onboarding}</div>
          </div>
        )}
        <div className="stat-card leaf" style={cardStyle('present')} onClick={() => toggleStatus('present')}>
          <div className="stat-lbl">{STATUS_MAP.OnTime.label}</div>
          <div className="stat-val">{counts.present}</div>
        </div>
        <div className="stat-card purple" style={cardStyle('dayOff')} onClick={() => toggleStatus('dayOff')}>
          <div className="stat-lbl">{STATUS_MAP.DayOff.label}</div>
          <div className="stat-val">{counts.dayOff}</div>
        </div>
        <div className="stat-card purple" style={cardStyle('onLeave')} onClick={() => toggleStatus('onLeave')}>
          <div className="stat-lbl">{STATUS_MAP.OnLeave.label}</div>
          <div className="stat-val">{counts.onLeave}</div>
        </div>
        {counts.sick > 0 && (
          <div className="stat-card blue" style={cardStyle('sick')} onClick={() => toggleStatus('sick')}>
            <div className="stat-lbl">Xəstəlik</div>
            <div className="stat-val">{counts.sick}</div>
          </div>
        )}
        {/* Shown only when somebody is on one, like Xəstəlik — a permanent 0 is a tile you read and
            discard every morning. The wording says the thing that matters about it: they are working. */}
        {counts.trip > 0 && (
          <div className="stat-card teal" style={cardStyle('trip')} onClick={() => toggleStatus('trip')}>
            <div className="stat-lbl">Ezamiyyət</div>
            <div className="stat-val">{counts.trip}</div>
          </div>
        )}
        <div className="stat-card amber" style={cardStyle('permission')} onClick={() => toggleStatus('permission')}>
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

      <div className="tbl-wrap tbl-cards tbl-dense">
        <table>
          <thead>
            <tr>
              {/* Clicking a heading sorts by it; clicking it again reverses. The two columns that are
                  really categories — branch and job — also filter when their VALUE is clicked, below. */}
              <Th col="name" label="İşçi" sortBy={sortBy} desc={sortDesc} onSort={sort} />
              {!grouped && <Th col="location" label="Filial" sortBy={sortBy} desc={sortDesc} onSort={sort} />}
              <Th col="position" label="Vəzifə" sortBy={sortBy} desc={sortDesc} onSort={sort} />
              <Th col="status" label="Status" sortBy={sortBy} desc={sortDesc} onSort={sort} />
              <Th col="in" label="Giriş" sortBy={sortBy} desc={sortDesc} onSort={sort} />
              <Th col="out" label="Çıxış" sortBy={sortBy} desc={sortDesc} onSort={sort} />
              <th>Foto</th>
              <th>Üz</th>
            </tr>
          </thead>
          <tbody>
            {byBranch.map(([branch, rows]) => (
              <Fragment key={branch || 'all'}>
                {grouped && (
                  <tr className="tbl-group">
                    <td colSpan={7}>
                      <button
                        className="tbl-filter"
                        onClick={() => { const id = rows[0]?.locationId; if (id) setFilterLoc(id) }}
                      >
                        {branch}
                      </button>
                      <span className="tbl-group-n">{rows.length}</span>
                    </td>
                  </tr>
                )}
                {rows.map((r) => (
                  <tr key={r.employeeId}>
                <td data-label="İşçi" style={{ fontWeight: 700, color: 'var(--c900)' }}><EmployeeLink id={r.employeeId} name={r.employeeName} /></td>
                {!grouped && (
                  <td data-label="Filial">
                    <button className="tbl-filter" onClick={() => setFilterLoc((v) => (v === r.locationId ? '' : r.locationId))}>
                      {r.locationName}
                    </button>
                  </td>
                )}
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
                    : null}
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
                    {/* Which rows can be given a reason.
                        «Aktivləşdirməyib»: the person whose day nobody can decide but a human — no
                        scan history, so the system will never call them absent by itself.
                        «İstirahət»: a rest day is the branch's calendar, not a statement about the
                        person, and somebody on that day may in fact be on holiday or off sick. The
                        Fəvvarələr manager had nineteen people reading «İstirahət» on a Sunday, some
                        of them on leave and some ill, and no way to say so from this screen — the
                        pencil simply never appeared on those rows. */}
                    {(r.status === 'Absent' || r.status === 'Onboarding' || r.status === 'DayOff'
                      || ((r.status === 'OnLeave' || r.status === 'Permission') && r.leaveId)) && (
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
                          {/* «Səbəbi sil», not «Qayıba qaytar»: the day underneath may be a rest day,
                              and removing a holiday from a Sunday returns it to İstirahət. */}
                          {r.leaveId && (
                            <button className="reason-pop-item" style={{ color: 'var(--clay)' }} onClick={() => void removeLeave(r.employeeId, r.leaveId!)}>
                              <span className="reason-dot" style={{ background: 'var(--clay)' }} />
                              Səbəbi sil
                            </button>
                          )}
                          {/* The other half of the pair: a day the system will not judge by itself. */}
                          {!r.leaveId && !r.absenceMarkedBy && r.status !== 'Absent' && (
                            <button className="reason-pop-item" style={{ color: 'var(--clay)' }} onClick={() => void markDayAbsent(r.employeeId)}>
                              <span className="reason-dot" style={{ background: 'var(--clay)' }} />
                              Qayıb yaz
                            </button>
                          )}
                          {r.absenceMarkedBy && (
                            <button className="reason-pop-item" onClick={() => void undoDayAbsent(r.employeeId)}>
                              <span className="reason-dot" style={{ background: 'var(--c400)' }} />
                              Qayıbı geri al
                            </button>
                          )}
                        </div>
                      </>
                    )}
                  </span>
                  {/* Who pinned this reason. It was a second line under the badge, which made every
                      leave row twice the height of the ones around it — on a board of two hundred
                      names, uneven rows are what stops the eye tracking down a column. It is a title
                      on the badge now: still there for anyone who asks, no longer a layout event. */}
                  {r.leaveAssignedBy && (
                    <span className="tbl-by" title={`Təyin edən: ${r.leaveAssignedBy}`}>ⓘ</span>
                  )}
                  {/* A Qayıb somebody wrote by hand says whose decision it was — it costs a day's pay. */}
                  {r.absenceMarkedBy && (
                    <span className="tbl-by" title={`Qayıbı yazan: ${r.absenceMarkedBy}`}>✋</span>
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
                  {(r.checkInAtUtc ?? r.fieldCheckInAtUtc) ? fmtTime(r.checkInAtUtc ?? r.fieldCheckInAtUtc) : ''}
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
                  {(r.checkOutAtUtc ?? r.fieldCheckOutAtUtc) ? fmtTime(r.checkOutAtUtc ?? r.fieldCheckOutAtUtc) : ''}
                  {r.earlyDepartureReason && (
                    <div className="tbl-note">
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
                      className="tbl-icon"
                      disabled={busyId === r.recordId}
                      onClick={() => void viewPhoto(r)}
                      title="Giriş şəklini gör"
                      aria-label="Giriş şəklini gör"
                    >
                      {busyId === r.recordId ? '…' : <IconCamera />}
                    </button>
                  ) : null}
                </td>
                <td data-label="Üz">
                  <FaceFlagBadge status={r.faceMatchStatus} score={r.faceMatchScore} compact />
                </td>
                  </tr>
                ))}
              </Fragment>
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

      {exportOpen && (
        <ExportDialog
          date={dateLabel}
          locations={locations}
          countAt={(id) => rows.filter((r) => r.locationId === id).length}
          selected={exportSites}
          onToggle={(id) =>
            setExportSites((prev) => (prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]))}
          onAll={() => setExportSites(locations.map((l) => l.id))}
          onNone={() => setExportSites([])}
          busy={exporting}
          onRun={() => void runExport()}
          onClose={() => setExportOpen(false)}
        />
      )}

      {modal && (
        <PhotoCompareModal
          title={modal.title}
          referenceUrl={modal.photo.referencePhotoUrl}
          checkInUrl={modal.photo.checkInPhotoUrl}
          checkInTakenAtUtc={modal.photo.checkInPhotoTakenAtUtc}
          faceMatchStatus={modal.photo.faceMatchStatus}
          faceMatchScore={modal.photo.faceMatchScore}
          recordId={modal.recordId}
          // Admin only — a manager compares faces and reports; ending a paid day is not theirs.
          //
          // AND not in a «baxış rejimi» session. A view session carries the borrowed admin's role, so
          // the button would render and the server would refuse it (ViewOnlyBoundary blocks every
          // POST): a control that looks armed and does nothing, on the one action in this product
          // that must not be pressed twice in confusion.
          canAct={mayAct && !getImpersonation()?.readOnly}
          onActed={() => void load()}
          onClose={() => setModal(null)}
        />
      )}
    </div>
  )
}

/**
 * Choosing what goes into the morning report.
 *
 * The workbook is sent to the leadership, so the one thing this must never do is produce a file whose
 * scope is a surprise — hence the sites are ticked by hand every time, the file is headed with the
 * choice, and the dialog says out loud that the screen's own filters do not travel with it.
 */
function ExportDialog({
  date, locations, countAt, selected, onToggle, onAll, onNone, busy, onRun, onClose,
}: {
  date: string
  locations: { id: string; name: string }[]
  countAt: (id: string) => number
  selected: string[]
  onToggle: (id: string) => void
  onAll: () => void
  onNone: () => void
  busy: boolean
  onRun: () => void
  onClose: () => void
}) {
  const total = locations.filter((l) => selected.includes(l.id)).reduce((n, l) => n + countAt(l.id), 0)

  return (
    <div
      onClick={onClose}
      style={{
        position: 'fixed', inset: 0, background: 'rgba(0,0,0,.6)', zIndex: 10000,
        display: 'flex', alignItems: 'center', justifyContent: 'center', padding: 16,
      }}
    >
      <div
        className="card card-pad"
        onClick={(e) => e.stopPropagation()}
        style={{ maxWidth: 560, width: '100%', maxHeight: '85vh', overflow: 'auto' }}
      >
        <div className="card-title" style={{ marginBottom: 4 }}>Excel-ə çıxar</div>
        <div className="muted" style={{ fontSize: 12.5, marginBottom: 14, lineHeight: 1.6 }}>
          {date} · Fayl iki vərəqdən ibarətdir: <b>Xülasə</b> (hər ərazi üzrə günün rəqəmləri) və{' '}
          <b>Davamiyyət</b> (adamlar ərazi-ərazi qruplaşdırılmış). Seçilmiş ərazilərin{' '}
          <b>bütün işçiləri</b> daxil edilir — ekrandakı axtarış və status filtrləri fayla keçmir.
        </div>

        <div style={{ display: 'flex', gap: 8, marginBottom: 10 }}>
          <button className="btn btn-sm" onClick={onAll}>Hamısı</button>
          <button className="btn btn-sm" onClick={onNone}>Heç biri</button>
          <span className="muted" style={{ marginLeft: 'auto', alignSelf: 'center', fontSize: 12 }}>
            {selected.length} ərazi · {total} işçi
          </span>
        </div>

        <div style={{ border: '1px solid var(--c100)', borderRadius: 12, overflow: 'hidden', marginBottom: 14 }}>
          {locations.map((l, i) => (
            <label
              key={l.id}
              style={{
                display: 'flex', alignItems: 'center', gap: 10, padding: '9px 12px', cursor: 'pointer',
                borderTop: i === 0 ? 'none' : '1px solid var(--c100)',
              }}
            >
              <input type="checkbox" checked={selected.includes(l.id)} onChange={() => onToggle(l.id)} />
              <span style={{ flex: 1, fontSize: 13.5, color: 'var(--c900)' }}>{l.name}</span>
              <span className="muted" style={{ fontSize: 12 }}>{countAt(l.id)} nəfər</span>
            </label>
          ))}
          {locations.length === 0 && (
            <div className="muted" style={{ padding: 14, fontSize: 13 }}>Bu gün üçün ərazi yoxdur.</div>
          )}
        </div>

        <div style={{ display: 'flex', gap: 8 }}>
          <button className="btn btn-primary" disabled={busy || selected.length === 0} onClick={onRun}>
            {busy ? 'Çıxarılır…' : '⬇ Çıxar'}
          </button>
          <button className="btn" onClick={onClose}>Ləğv et</button>
        </div>
      </div>
    </div>
  )
}
