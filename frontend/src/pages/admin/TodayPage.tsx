import { Fragment, useCallback, useEffect, useMemo, useRef, useState, type MouseEvent } from 'react'
import { bucketOf, countToday, matchesLeaveCard, sortRows, type SortColumn } from './todayCounts'
import { areaOf, exportRow, uniqueAreas, type AreaView } from './exportRows'
import { formatWorked, workedMinutes } from './todayShift'
import { TodayRow } from './TodayRow'
import { useSearchParams } from 'react-router-dom'
import { exportDayXlsx, getToday, markAbsent, unmarkAbsent, type DayAttendanceRow } from '../../api/admin'
import { getImpersonation } from '../../api/client'
import { addLeave, deleteLeave, type LeaveType } from '../../api/leaves'
import { createManagerLeave, deleteManagerLeave } from '../../api/manager'
import { useAuth } from '../../auth/AuthContext'
import { getPhotoUrl, type PhotoUrlResponse } from '../../api/attendance'
import { STATUS_MAP, dayLabel } from '../../components/StatusBadge'
import { PhotoCompareModal } from '../../components/PhotoCompareModal'
import { faceIsFlagged } from '../../components/FaceFlagBadge'
import {
  IconCalendar, IconCheck, IconChevronDown, IconChevronLeft, IconChevronRight, IconClock,
  IconColumns, IconDownload, IconSearch, IconTable, IconUserX, IconX,
} from '../../components/icons'
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

/** Which optional columns are on. Remembered per browser: an admin who works branch by branch turns
 *  Ərazi off once, and an HR reader who lives in the job titles turns Vəzifə on once. */
type Cols = { location: boolean; position: boolean; schedule: boolean; worked: boolean }
const COLS_KEY = 'qrlog.today.cols'
const COLS_DEFAULT: Cols = { location: true, position: false, schedule: true, worked: true }

function readCols(): Cols {
  // Storage can be absent or throw outright (private window, blocked site data) — a board that fails
  // to render because of a remembered column preference would be a poor trade.
  try {
    const raw = localStorage.getItem(COLS_KEY)
    return raw ? { ...COLS_DEFAULT, ...JSON.parse(raw) as Partial<Cols> } : COLS_DEFAULT
  } catch {
    return COLS_DEFAULT
  }
}

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

  // useCallback with no dependencies, and that is load-bearing rather than tidiness: the row is
  // memoised, so a handler that changed identity on every render would re-render all 488 rows and
  // undo the whole point. Every handler below is written to need nothing from the render it is in.
  const openReasonMenu = useCallback((e: MouseEvent, employeeId: string) => {
    const r = (e.currentTarget as HTMLElement).getBoundingClientRect()
    // Clamp so a menu near the right/bottom edge stays on screen.
    setMenuPos({ top: Math.min(r.bottom + 4, window.innerHeight - 250), left: Math.min(r.left, window.innerWidth - 210) })
    setReasonFor(employeeId)
  }, [])

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
  // Which branches the board is narrowed to — several at once.
  //
  // It used to be one id or none, drawn as a row of chips. That was right for three branches and
  // unusable at twenty-two: the chips wrapped to four lines and pushed the table off the first
  // screen, and because only one could be on, «the two parks» was a question the board could not be
  // asked at all.
  const [filterLocs, setFilterLocs] = useState<string[]>([])
  const [locOpen, setLocOpen] = useState(false)
  const [locQuery, setLocQuery] = useState('')
  const [busyId, setBusyId] = useState<string | null>(null)
  const [photoError, setPhotoError] = useState<string | null>(null)
  const [modal, setModal] = useState<{ title: string; photo: PhotoUrlResponse; recordId: string | null } | null>(null)
  // A caller can deep-link a pre-applied status filter, e.g. the dashboard's "Bu gün gəlməyib" →
  // /admin/today?status=absent. Read once at mount.
  const [searchParams] = useSearchParams()
  const [statusFilter, setStatusFilter] = useState<string | null>(() => searchParams.get('status'))
  const [search, setSearch] = useState('')
  const searchRef = useRef<HTMLInputElement>(null)
  /**
   * The audit lens: everybody, the faces that did not match, or the scans with no selfie.
   *
   * One of three rather than two independent toggles. They were never used together — «show me the
   * flagged ones that also have no photo» is not a question anybody asks, and as separate switches
   * the board could land in that empty intersection and look broken.
   */
  const [lens, setLens] = useState<'all' | 'flagged' | 'nophoto'>('all')
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
  const [groupByBranch, setGroupByBranch] = useState(true)
  const [cols, setCols] = useState<Cols>(readCols)
  const [colsOpen, setColsOpen] = useState(false)
  // The statuses that are not every day's business, folded away until asked for.
  const [moreOpen, setMoreOpen] = useState(false)
  // When the board last answered, and the clock the live «İş vaxtı» column counts against. Both are
  // set by the same poll, so a row never shows minutes the header has not admitted to loading.
  const [loadedAt, setLoadedAt] = useState<string | null>(null)
  const [nowMs, setNowMs] = useState(() => Date.now())
  // Which sites go into the workbook. Chosen explicitly before every export: the file is sent to the
  // leadership, and one that quietly carried whatever filter happened to be on screen is a report
  // nobody can tell apart from the whole company.
  const [exportOpen, setExportOpen] = useState(false)
  const [exportSites, setExportSites] = useState<string[]>([])
  // Which structure the file is built in. HR sends leadership the «sənəd üzrə» view every morning;
  // somebody else wants the branches. Both are the same people on the same day — only the shape of
  // the report differs — so it is one switch, not two exports to keep in step.
  const [exportView, setExportView] = useState<AreaView>('actual')

  const pickPosition = useCallback((p: string) => setFilterPosition((v) => (v === p ? null : p)), [])
  const pickLocation = useCallback((id: string) => setFilterLocs([id]), [])

  function toggleCol(key: keyof Cols) {
    setCols((c) => {
      const next = { ...c, [key]: !c[key] }
      try { localStorage.setItem(COLS_KEY, JSON.stringify(next)) } catch { /* not worth a broken board */ }
      return next
    })
  }

  const viewPhoto = useCallback(async (row: DayAttendanceRow) => {
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
  }, [])

  const onPhoto = useCallback((row: DayAttendanceRow) => { void viewPhoto(row) }, [viewPhoto])

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
  /** What is actually standing in the way, in the same words the row beside it uses. */
  function blockingLabel(employeeId: string): string {
    const r = rows.find((x) => x.employeeId === employeeId)
    return r ? dayLabel(r.status, r.leaveType).toLocaleLowerCase('az') : 'məzuniyyət/icazə'
  }

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
        // Names what is actually there. It said «məzuniyyət/icazə» whatever the record was, so an
        // admin blocked by a rest day — two thirds of every record ever filed — or by an ezamiyyət
        // went looking for a holiday that does not exist.
        : code === 'HasLeave' ? `Bu gün üçün ${blockingLabel(employeeId)} var — əvvəlcə onu silin`
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
      setLoadedAt(new Date().toISOString())
      setNowMs(Date.now())
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

  // ⌘K / Ctrl-K puts the cursor in the name box. The board is opened to look one person up more
  // often than for anything else, and the box is three blocks up the page from where the eye is.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key.toLowerCase() !== 'k' || !(e.metaKey || e.ctrlKey)) return
      e.preventDefault()
      searchRef.current?.focus()
      searchRef.current?.select()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [])

  function shiftDate(delta: number) {
    const d = new Date(`${date}T00:00:00`)
    d.setDate(d.getDate() + delta)
    const iso = localDateISO(d)
    if (iso <= todayISO) setDate(iso)
  }

  const locations = useMemo(() => {
    const seen = new Map<string, { name: string; count: number }>()
    for (const r of rows) {
      const hit = seen.get(r.locationId)
      if (hit) hit.count++
      else seen.set(r.locationId, { name: r.locationName, count: 1 })
    }
    return Array.from(seen, ([id, v]) => ({ id, name: v.name, count: v.count }))
      .sort((a, b) => a.name.localeCompare(b.name, 'az'))
  }, [rows])

  const locFiltered = useMemo(
    () => (filterLocs.length ? rows.filter((r) => filterLocs.includes(r.locationId)) : rows),
    [rows, filterLocs],
  )

  // Counts reflect the LOCATION scope only (not the status/search/photo filters), so the cards keep
  // showing the day's real breakdown and stay usable as toggles.
  // present = checked in AND out ("Tamamlayıb"). incomplete = checked in, no check-out yet — reads as
  // "İşdə" (still at work) on today's board, or "Çıxış yoxdur" (forgot to check out) on a past date.
  // Bucketing lives in ./todayCounts, with tests. Every kind of leave arrives as one status
  // (`OnLeave`) and is separable only by `leaveType`, so a screen that counts by status merges a
  // work trip into the holidays — which is what this board did, and what the reports did before
  // 3d6ac7e. Twice is enough for it to belong somewhere a test can see it.
  const counts = useMemo(() => countToday(locFiltered), [locFiltered])
  const { flaggedCount, noPhotoCount } = useMemo(() => ({
    flaggedCount: locFiltered.filter((r) => faceIsFlagged(r.faceMatchStatus)).length,
    noPhotoCount: locFiltered.filter((r) => r.checkInAtUtc && !r.hasPhoto).length,
  }), [locFiltered])
  const incompleteLabel = isToday ? 'İşdə' : 'Çıxış yoxdur'

  const q = search.trim().toLowerCase()
  const visible = useMemo(() => sortRows(locFiltered.filter((r) => {
    if (lens === 'flagged' && !faceIsFlagged(r.faceMatchStatus)) return false
    // "No photo" = checked in but the selfie is missing (an absentee having no photo is not notable).
    if (lens === 'nophoto' && !(r.checkInAtUtc && !r.hasPhoto)) return false
    // Sick / Ezamiyyət / Məzuniyyət all come from OnLeave, split by leaveType — so their filters
    // need the row, not just the status.
    if (statusFilter === 'sick' || statusFilter === 'trip' || statusFilter === 'onLeave' || statusFilter === 'unpaid') {
      if (!matchesLeaveCard(r, statusFilter)) return false
    } else if (statusFilter && bucketOf(r) !== statusFilter) return false
    if (filterPosition && (r.position ?? '') !== filterPosition) return false
    if (q && !r.employeeName.toLowerCase().includes(q)) return false
    return true
  }), sortBy, sortDesc), [locFiltered, lens, statusFilter, filterPosition, q, sortBy, sortDesc])

  /**
   * The list, cut into branches.
   *
   * 221 rows in one run is not a board, it is a scroll — and the branch column was the same word
   * repeated forty times down the page while the reader looked for a name. Grouped, the word is said
   * once as a heading and the column disappears; with one branch on screen there is nothing to say,
   * so the grouping switches itself off rather than printing a single heading over everything.
   */
  const branchesOnScreen = useMemo(() => new Set(visible.map((r) => r.locationName)).size, [visible])
  const grouped = groupByBranch && branchesOnScreen > 1
  const byBranch = useMemo(() => (grouped
    ? [...visible.reduce((m, r) => {
        const list = m.get(r.locationName)
        if (list) list.push(r); else m.set(r.locationName, [r])
        return m
      }, new Map<string, typeof visible>())].sort((a, b) => b[1].length - a[1].length || a[0].localeCompare(b[0], 'az'))
    : [['', visible] as [string, typeof visible]]), [visible, grouped])

  // Same column twice reverses it; a new column starts ascending, which is what every table does.
  const sort = (c: typeof sortBy) => {
    if (c === sortBy) setSortDesc((d) => !d)
    else { setSortBy(c); setSortDesc(false) }
  }

  const toggleStatus = (k: string) => setStatusFilter((f) => (f === k ? null : k))
  const toggleLoc = (id: string) =>
    setFilterLocs((v) => (v.includes(id) ? v.filter((x) => x !== id) : [...v, id]))

  const showLocCol = !grouped && cols.location
  const colCount = 5 + (showLocCol ? 1 : 0) + (cols.position ? 1 : 0) + (cols.schedule ? 1 : 0) + (cols.worked ? 1 : 0)

  // How many ways the board is narrowed right now — so «Filtrləri sıfırla» is offered only when it
  // would do something, and the reader can see at a glance that a list is short for a reason.
  const activeFilters =
    (filterLocs.length ? 1 : 0) + (lens !== 'all' ? 1 : 0) + (statusFilter ? 1 : 0)
    + (filterPosition ? 1 : 0) + (q ? 1 : 0)

  function resetFilters() {
    setFilterLocs([])
    setLens('all')
    setStatusFilter(null)
    setFilterPosition(null)
    setSearch('')
  }

  // The exported Status must say EXACTLY what the badge beside it says.
  //
  // It did not. The file was built from `STATUS_MAP[r.status]`, and the backend stores Məzuniyyət,
  // Xəstəlik and Ezamiyyət under one status — OnLeave — so every one of them printed «Məzuniyyət».
  // On screen the same row already read correctly, because the badge passes leaveVisual(leaveType).
  // An admin therefore saw «Xəstəlik» on the board, pressed Excel, and got a file saying the same
  // person took annual leave on the same day. Worst of all «Ezamiyyət», which is WORK, exported as
  // leave. The type is already on the row and was simply never read here.
  const statusLabel = (r: DayAttendanceRow) => dayLabel(r.status, r.leaveType, incompleteLabel)


  // The areas of the CHOSEN view, gathered from the board's own rows.
  //
  // Keyed by NAME rather than by branch id, because a «sənəd üzrə» area is a name — it can belong to
  // another company's structure and have no branch behind it at all. Counting from the rows also
  // means the list can never offer an area that turns out to hold nobody.
  const exportAreas = useMemo(() => {
    const by = new Map<string, number>()
    for (const r of rows) by.set(areaOf(r, exportView), (by.get(areaOf(r, exportView)) ?? 0) + 1)
    return [...by.entries()]
      .map(([name, count]) => ({ name, count }))
      .sort((a, b) => a.name.localeCompare(b.name, 'az'))
  }, [rows, exportView])

  function openExport() {
    // Pre-tick what the reader is already looking at: the areas they filtered to, or all of them.
    const picked = locations.filter((l) => filterLocs.includes(l.id)).map((l) => l.name)
    setExportView('actual')
    setExportSites(picked.length ? picked : uniqueAreas(rows, 'actual'))
    setExportOpen(true)
  }

  // Switching the view re-ticks everything: a selection made in one structure means nothing in the
  // other — «Aeroport yolu» unticked as a branch does not name an area on the paper side — and
  // carrying it across would silently drop people from a file whose scope note still claims them.
  function switchExportView(view: AreaView) {
    setExportView(view)
    setExportSites(uniqueAreas(rows, view))
  }

  async function runExport() {
    const chosen = new Set(exportSites)
    const inScope = (r: DayAttendanceRow) => chosen.has(areaOf(r, exportView))
    // Deliberately built from `rows`, not from `visible`. The screen's search, status and photo
    // filters are how somebody LOOKS at a day; a report to the leadership has to be everybody at the
    // sites it claims to cover, or a file headed «Qala Anbar» silently omits the nine people who did
    // not match a search box left over from ten minutes ago.
    const picked = sortRows(rows.filter(inScope), 'name', false)
    // One line per person, shaped by exportRows — pure, and tested against the cases an audit of this
    // report actually found wrong: a night that read backwards, a carried-over night that read as this
    // morning, and a field worker exported with no times at all.
    const payload = picked.map((r) => exportRow(r, date, statusLabel(r), exportView))

    // Never «Bütün ərazilər». `locations` is only what is on THIS board: a branch manager sees their
    // own branches, so the phrase would stamp a two-site file as the whole company, and even for an
    // admin a site whose staff are all inactive never appears. The sites are named instead, and once
    // there are too many to name the count stands — the Xülasə sheet lists every one of them anyway.
    const names = exportAreas.filter((a) => chosen.has(a.name)).map((a) => a.name)
    // The view is named FIRST, because it is the thing a reader cannot infer from the numbers: two
    // files of the same morning with the same total can group the same people differently, and the
    // one that does not say which is which is the one somebody reconciles against the wrong list.
    const viewNote = exportView === 'paper' ? 'Sənəd üzrə' : 'Faktiki ərazi'
    const areaPart = names.length <= 6
      ? `${names.length} ərazi: ${names.join(', ')}`
      : `${names.length} ərazi (siyahı «Xülasə» vərəqindədir)`
    const scopeNote = `${viewNote} · ${areaPart} · ${picked.length} işçi`

    setExporting(true)
    const ok = await exportDayXlsx({
      title: exportView === 'paper'
        ? `Davamiyyət (sənəd üzrə) — ${dateLabel}`
        : `Davamiyyət — ${dateLabel}`,
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
        unpaid: 'Ödənişsiz',
        sick: 'Xəstəlik',
        trip: 'Ezamiyyət',
        permission: STATUS_MAP.Permission.label,
        dayOff: 'Həftəlik istirahət',
        rest: 'İstirahət (təyin edilmiş)',
        pending: STATUS_MAP.Pending.label,
        onboarding: STATUS_MAP.Onboarding.label,
      },
    })
    setExporting(false)
    if (ok) setExportOpen(false)
    else setPhotoError('Excel çıxarıla bilmədi')
  }

  const dateLabel = fmtLongDate(date)
  const total = locFiltered.length

  /**
   * The four numbers the morning is actually about.
   *
   * Twelve equal tiles meant «Ezamiyyət 2» was drawn as loudly as «Qayıb 46», and the two or three
   * that decide whether somebody has to make a phone call stopped standing out. These four carry the
   * day; everything else moved to the quiet strip under them, where it is still a number and still a
   * filter, just not a headline.
   */
  const KPIS = [
    { key: 'incomplete', label: incompleteLabel, n: counts.incomplete, tint: isToday ? 'blue' : 'clay', Icon: IconClock },
    { key: 'present', label: STATUS_MAP.OnTime.label, n: counts.present, tint: 'leaf', Icon: IconCheck },
    { key: 'absent', label: STATUS_MAP.Absent.label, n: counts.absent, tint: 'clay', Icon: IconUserX },
    { key: 'dayOff', label: 'Həftəlik istirahət', n: counts.dayOff, tint: 'purple', Icon: IconCalendar },
  ]

  const MINOR = [
    { key: 'pending', label: STATUS_MAP.Pending.label, n: counts.pending, dot: 'var(--c400)' },
    { key: 'onboarding', label: STATUS_MAP.Onboarding.label, n: counts.onboarding, dot: 'var(--c400)' },
    { key: 'onLeave', label: STATUS_MAP.OnLeave.label, n: counts.onLeave, dot: 'var(--purple)' },
    { key: 'permission', label: STATUS_MAP.Permission.label, n: counts.permission, dot: 'var(--amber)' },
    { key: 'sick', label: 'Xəstəlik', n: counts.sick, dot: 'var(--blue)' },
    { key: 'trip', label: 'Ezamiyyət', n: counts.trip, dot: 'var(--teal)' },
    { key: 'unpaid', label: 'Ödənişsiz', n: counts.unpaid, dot: 'var(--clay)' },
    { key: 'rest', label: 'İstirahət (təyin edilmiş)', n: counts.rest, dot: 'var(--c400)' },
  ]
  // Inline: the ones that happened today, up to four. A status with nobody in it is still reachable —
  // it is behind «Digər statuslar», so a filter never disappears, it only stops taking up a row.
  const minorShown = moreOpen ? MINOR : MINOR.filter((m) => m.n > 0 || m.key === statusFilter).slice(0, 4)

  /** The row whose reason menu is open, if any — the menu itself is rendered once, at page level. */
  const reasonRow = reasonFor ? rows.find((r) => r.employeeId === reasonFor) : undefined

  const locLabel = (id: string) => locations.find((l) => l.id === id)?.name ?? ''
  const locMatches = locations.filter(
    (l) => !locQuery.trim() || l.name.toLocaleLowerCase('az').includes(locQuery.toLocaleLowerCase('az')),
  )

  return (
    <div>
      <div className="att-head">
        <div>
          <h1 className="att-title">Davamiyyət</h1>
          {/* No `capitalize` here, which the old line had: fmtLongDate already returns proper
              Azerbaijani, where the month and the weekday are lower case. The rule was turning every
              morning into «1 Oktyabr 2026, Cümə Axşamı». */}
          <div className="att-sub">
            {isToday ? 'Bu gün' : 'Tarix'}: {dateLabel}{isToday ? ' · canlı' : ''}
          </div>
        </div>
        <div className="att-head-act">
          {loadedAt && <span className="att-stamp">Son yenilənmə: {fmtTime(loadedAt)}</span>}
          <button className="btn btn-sm" disabled={exporting} onClick={openExport}>
            <IconDownload />
            {exporting ? 'Çıxarılır…' : 'Excel-ə çıxar'}
          </button>
        </div>
      </div>

      <div className="att-filters">
        <div className="att-f-row">
          <div className="att-f">
            <span className="att-f-lbl">Tarix</span>
            <div className="att-date">
              <button className="att-step" onClick={() => shiftDate(-1)} title="Əvvəlki gün" aria-label="Əvvəlki gün">
                <IconChevronLeft />
              </button>
              <input
                type="date"
                value={date}
                max={todayISO}
                onChange={(e) => { if (e.target.value && e.target.value <= todayISO) setDate(e.target.value) }}
                className="att-date-inp"
                aria-label="Tarix"
              />
              <button
                className="att-step"
                disabled={isToday}
                onClick={() => shiftDate(1)}
                title="Növbəti gün"
                aria-label="Növbəti gün"
              >
                <IconChevronRight />
              </button>
              {!isToday && <button className="btn btn-sm" onClick={() => setDate(todayISO)}>Bu gün</button>}
            </div>
          </div>

          {locations.length > 1 && (
            <div className="att-f" style={{ flex: '1 1 320px', maxWidth: 440 }}>
              <span className="att-f-lbl">Ərazi</span>
              <div className="att-multi">
                {/* A div, not a button: it holds the tokens' own «götür» buttons, and a button inside
                    a button is invalid markup that browsers resolve by dropping one of them. */}
                <div
                  role="button"
                  tabIndex={0}
                  aria-expanded={locOpen}
                  aria-label="Ərazi seç"
                  className={`att-multi-box${locOpen ? ' open' : ''}`}
                  onClick={() => { setLocOpen((v) => !v); setLocQuery('') }}
                  onKeyDown={(e) => {
                    if (e.key !== 'Enter' && e.key !== ' ') return
                    e.preventDefault()
                    setLocOpen((v) => !v)
                    setLocQuery('')
                  }}
                >
                  <IconSearch />
                  {filterLocs.length === 0 && <span className="att-multi-ph">Bütün ərazilər</span>}
                  {filterLocs.slice(0, 2).map((id) => (
                    <span key={id} className="att-tok">
                      <span>{locLabel(id)}</span>
                      <button
                        type="button"
                        aria-label={`${locLabel(id)} — seçimi götür`}
                        onClick={(e) => { e.stopPropagation(); toggleLoc(id) }}
                      >
                        <IconX />
                      </button>
                    </span>
                  ))}
                  {filterLocs.length > 2 && <span className="att-tok more">+{filterLocs.length - 2}</span>}
                  <span className="att-multi-sp" />
                  <IconChevronDown />
                </div>
                {locOpen && (
                  <>
                    <div className="att-backdrop" onClick={() => setLocOpen(false)} />
                    <div className="att-pop">
                      <div className="att-pop-h">
                        <span className="att-search">
                          <IconSearch />
                          <input
                            autoFocus
                            value={locQuery}
                            onChange={(e) => setLocQuery(e.target.value)}
                            placeholder="Ərazi axtar…"
                            aria-label="Ərazi axtar"
                          />
                        </span>
                      </div>
                      {locMatches.map((l) => (
                        <label key={l.id} className="att-opt">
                          <input
                            type="checkbox"
                            checked={filterLocs.includes(l.id)}
                            onChange={() => toggleLoc(l.id)}
                          />
                          <span className="att-opt-t">{l.name}</span>
                          <span className="att-opt-n">{l.count}</span>
                        </label>
                      ))}
                      {locMatches.length === 0 && (
                        <div className="muted" style={{ padding: '10px 9px', fontSize: 12 }}>Tapılmadı</div>
                      )}
                      <div className="att-pop-f">
                        <button type="button" onClick={() => setFilterLocs(locations.map((l) => l.id))}>Hamısı</button>
                        <button type="button" onClick={() => setFilterLocs([])}>Təmizlə</button>
                      </div>
                    </div>
                  </>
                )}
              </div>
            </div>
          )}

          <div className="att-f" style={{ flex: '1 1 260px' }}>
            <span className="att-f-lbl">İşçi</span>
            <span className="att-search">
              <IconSearch />
              <input
                ref={searchRef}
                type="text"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                placeholder="Ad üzrə axtar…"
                aria-label="Ad üzrə axtar"
              />
              {search
                ? (
                  <button className="att-clear" onClick={() => setSearch('')} title="Təmizlə" aria-label="Axtarışı təmizlə">
                    <IconX />
                  </button>
                )
                : <span className="att-kbd">⌘K</span>}
            </span>
          </div>
        </div>

        <div className="att-f-foot">
          <div className="att-seg">
            <button className={lens === 'all' ? 'on' : ''} onClick={() => setLens('all')}>
              Bütün işçilər
              <span className="att-seg-n">{total}</span>
            </button>
            <button
              className={lens === 'flagged' ? 'on' : ''}
              onClick={() => setLens('flagged')}
              title="Giriş şəklindəki üz referans şəkillə uyğun gəlməyən — yoxlanmalı girişlər"
            >
              Üzü uyğun gəlməyənlər
              <span className="att-seg-n">{flaggedCount}</span>
            </button>
            {mayViewPhotos && (
              <button className={lens === 'nophoto' ? 'on' : ''} onClick={() => setLens('nophoto')}>
                Şəkilsizlər
                <span className="att-seg-n">{noPhotoCount}</span>
              </button>
            )}
          </div>
          <div className="att-f-state">
            {activeFilters > 0 && (
              <>
                <span style={{ display: 'inline-flex', alignItems: 'center', gap: 5 }}>
                  <i />
                  {filterLocs.length > 0 && `${filterLocs.length} ərazi seçilib`}
                  {filterLocs.length > 0 && activeFilters > 1 && ' · '}
                  {activeFilters > (filterLocs.length > 0 ? 1 : 0)
                    && `${activeFilters - (filterLocs.length > 0 ? 1 : 0)} süzgəc aktiv`}
                </span>
                <button className="att-reset" onClick={resetFilters}>Filtrləri sıfırla</button>
              </>
            )}
          </div>
        </div>
      </div>

      <div className="att-stats-h">
        <div>
          <span className="att-stats-t">{isToday ? 'Bu günün icmalı' : 'Günün icmalı'}</span>
          <span className="att-stats-n">{total} işçi</span>
        </div>
        {isToday && (
          <span className="att-live"><i />Canlı məlumat</span>
        )}
      </div>

      <div className="att-kpis">
        {KPIS.map(({ key, label, n, tint, Icon }) => (
          <button
            key={key}
            className={`att-kpi${statusFilter === key ? ' on' : ''}`}
            onClick={() => toggleStatus(key)}
            title={`${label} — sətirləri süzmək üçün basın`}
          >
            <div className="att-kpi-h">
              <span className={`att-kpi-ic att-t-${tint}`}><Icon /></span>
              <span className="att-kpi-pct">{total > 0 ? `${Math.round((n / total) * 100)}% ümumi` : '—'}</span>
            </div>
            <div className="att-kpi-v">
              <span className="att-kpi-n">{n}</span>
              <span className="att-kpi-l">{label}</span>
            </div>
          </button>
        ))}
      </div>

      <div className="att-minor">
        {minorShown.map((m, i) => (
          <Fragment key={m.key}>
            {i > 0 && <span className="att-minor-sep" />}
            <button
              className={`att-minor-i${statusFilter === m.key ? ' on' : ''}`}
              onClick={() => toggleStatus(m.key)}
              title={`${m.label} — sətirləri süzmək üçün basın`}
            >
              <span className="att-minor-d" style={{ background: m.dot }} />
              <span className="att-minor-t">
                <span className="att-minor-l">{m.label}</span>
                <span className="att-minor-n">{m.n}</span>
              </span>
            </button>
          </Fragment>
        ))}
        {minorShown.length === 0 && (
          <span className="muted" style={{ padding: '10px 10px', fontSize: 12 }}>
            Bu gün başqa status yoxdur
          </span>
        )}
        <button className={`att-more${moreOpen ? ' open' : ''}`} onClick={() => setMoreOpen((v) => !v)}>
          {moreOpen ? 'Yığ' : 'Digər statuslar'}
          <IconChevronDown />
        </button>
      </div>

      {error && (
        <div className="fb fb-err" style={{ marginTop: 14 }}>
          <IconX />
          <span>{error}</span>
        </div>
      )}
      {photoError && (
        <div className="fb fb-err" style={{ marginTop: 14 }}>
          <IconX />
          <span>{photoError}</span>
        </div>
      )}

      {filterPosition && (
        <div className="fb fb-info" style={{ marginTop: 14 }}>
          <span>
            Yalnız <b>{filterPosition}</b> vəzifəsindəkilər — {visible.length} nəfər.
          </span>
          <button className="btn btn-sm" style={{ marginLeft: 'auto' }} onClick={() => setFilterPosition(null)}>
            Süzgəci ləğv et
          </button>
        </div>
      )}

      <div className="att-table">
        <div className="att-tbar">
          <div>
            <span className="att-tbar-t">Davamiyyət qeydləri</span>
            <span className="att-tbar-n">{visible.length} nəticə</span>
          </div>
          <div className="att-tbar-a">
            <button
              className={`att-tool${grouped ? ' on' : ''}`}
              onClick={() => setGroupByBranch((v) => !v)}
              disabled={branchesOnScreen < 2}
              title={branchesOnScreen < 2 ? 'Ekranda bir ərazi var' : 'Əraziyə görə qruplaşdır'}
            >
              <IconTable />
              Əraziyə görə
            </button>
            <button className={`att-tool${colsOpen ? ' on' : ''}`} onClick={() => setColsOpen((v) => !v)}>
              <IconColumns />
              Sütunlar
            </button>
            {colsOpen && (
              <>
                <div className="att-backdrop" onClick={() => setColsOpen(false)} />
                <div className="att-pop" style={{ left: 'auto', right: 0, minWidth: 220 }}>
                  {([
                    ['location', 'Ərazi'],
                    ['position', 'Vəzifə'],
                    ['schedule', 'İş qrafiki'],
                    ['worked', 'İş vaxtı'],
                  ] as [keyof Cols, string][]).map(([key, label]) => (
                    <label key={key} className="att-opt">
                      <input type="checkbox" checked={cols[key]} onChange={() => toggleCol(key)} />
                      <span className="att-opt-t">{label}</span>
                      {key === 'location' && grouped && <span className="att-opt-n">qruplanıb</span>}
                    </label>
                  ))}
                </div>
              </>
            )}
          </div>
        </div>

        <div className="tbl-wrap tbl-cards tbl-dense">
          <table>
            <thead>
              <tr>
                {/* Clicking a heading sorts by it; clicking it again reverses. The two columns that are
                    really categories — branch and job — also filter when their VALUE is clicked, below. */}
                <Th col="name" label="İşçi" sortBy={sortBy} desc={sortDesc} onSort={sort} />
                {showLocCol && <Th col="location" label="Ərazi" sortBy={sortBy} desc={sortDesc} onSort={sort} />}
                {cols.position && <Th col="position" label="Vəzifə" sortBy={sortBy} desc={sortDesc} onSort={sort} />}
                {cols.schedule && <th>İş qrafiki</th>}
                <Th col="status" label="Status" sortBy={sortBy} desc={sortDesc} onSort={sort} />
                <Th col="in" label="Giriş" sortBy={sortBy} desc={sortDesc} onSort={sort} />
                <Th col="out" label="Çıxış" sortBy={sortBy} desc={sortDesc} onSort={sort} />
                {cols.worked && <th>İş vaxtı</th>}
                <th style={{ textAlign: 'right' }}>Əməliyyat</th>
              </tr>
            </thead>
            <tbody>
              {byBranch.map(([branch, rows]) => (
                <Fragment key={branch || 'all'}>
                  {grouped && (
                    <tr className="tbl-group">
                      <td colSpan={colCount}>
                        <button
                          className="tbl-filter"
                          onClick={() => { const id = rows[0]?.locationId; if (id) setFilterLocs([id]) }}
                        >
                          {branch}
                        </button>
                        <span className="tbl-group-n">{rows.length}</span>
                      </td>
                    </tr>
                  )}
                  {rows.map((r) => {
                    // Computed HERE, not in the row: the row is memoised, and a clock it read for
                    // itself would make every row new on every tick. A string that has not changed
                    // lets React skip the row entirely.
                    const worked = cols.worked ? workedMinutes(r, nowMs, isToday) : null
                    const running = isToday && !!(r.checkInAtUtc ?? r.fieldCheckInAtUtc)
                      && !(r.lastCheckOutAtUtc ?? r.checkOutAtUtc ?? r.fieldCheckOutAtUtc)
                    return (
                      <TodayRow
                        key={r.employeeId}
                        r={r}
                        showLocCol={showLocCol}
                        showPosition={cols.position}
                        showSchedule={cols.schedule}
                        showWorked={cols.worked}
                        isToday={isToday}
                        workedText={formatWorked(worked)}
                        running={running}
                        mayViewPhotos={mayViewPhotos}
                        assigning={assigningId === r.employeeId}
                        photoBusy={busyId !== null && busyId === r.recordId}
                        onPosition={pickPosition}
                        onLocation={pickLocation}
                        onReason={openReasonMenu}
                        onPhoto={onPhoto}
                      />
                    )
                  })}
                </Fragment>
              ))}
              {loadedOnce && visible.length === 0 && !error && (
                <tr>
                  <td colSpan={colCount} className="muted" style={{ textAlign: 'center', padding: 28 }}>
                    {activeFilters > 0 ? 'Seçilmiş süzgəclərə uyğun işçi yoxdur' : 'Məlumat yoxdur'}
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>

        <div className="att-foot">
          <span>
            <b>{visible.length}</b> / {total} nəticə
            {grouped && <> · <b>{byBranch.length}</b> ərazi</>}
          </span>
          {visible.length > 25 && (
            <button className="att-reset" onClick={() => window.scrollTo({ top: 0, behavior: 'smooth' })}>
              Yuxarı qayıt
            </button>
          )}
        </div>
      </div>

      {/* The reason menu, once — not once per row.
          It is `position: fixed` and only ever one is open, so rendering it inside the row was a
          conditional branch 488 rows had to carry and React had to walk on every pass. Here it costs
          one lookup. */}
      {reasonRow && menuPos && (
        <>
          <div className="reason-backdrop" onClick={() => setReasonFor(null)} />
          <div className="reason-pop" style={{ top: menuPos.top, left: menuPos.left }}>
            <div className="reason-pop-h">Səbəb seçin</div>
            {LEAVE_OPTIONS.map((o) => (
              <button
                key={o.type}
                className="reason-pop-item"
                onClick={() => void assignLeave(reasonRow.employeeId, o.type, reasonRow.leaveId)}
              >
                <span className="reason-dot" style={{ background: o.dot }} />
                {o.label}
              </button>
            ))}
            {/* «Səbəbi sil», not «Qayıba qaytar»: the day underneath may be a rest day, and removing
                a holiday from a Sunday returns it to İstirahət. */}
            {reasonRow.leaveId && (
              <button
                className="reason-pop-item"
                style={{ color: 'var(--clay)' }}
                onClick={() => void removeLeave(reasonRow.employeeId, reasonRow.leaveId!)}
              >
                <span className="reason-dot" style={{ background: 'var(--clay)' }} />
                Səbəbi sil
              </button>
            )}
            {/* The other half of the pair: a day the system will not judge by itself. */}
            {!reasonRow.leaveId && !reasonRow.absenceMarkedBy && reasonRow.status !== 'Absent' && (
              <button
                className="reason-pop-item"
                style={{ color: 'var(--clay)' }}
                onClick={() => void markDayAbsent(reasonRow.employeeId)}
              >
                <span className="reason-dot" style={{ background: 'var(--clay)' }} />
                Qayıb yaz
              </button>
            )}
            {reasonRow.absenceMarkedBy && (
              <button className="reason-pop-item" onClick={() => void undoDayAbsent(reasonRow.employeeId)}>
                <span className="reason-dot" style={{ background: 'var(--c400)' }} />
                Qayıbı geri al
              </button>
            )}
          </div>
        </>
      )}

      {exportOpen && (
        <ExportDialog
          date={dateLabel}
          areas={exportAreas}
          view={exportView}
          onView={switchExportView}
          hasPaper={rows.some((r) => r.paperSite)}
          selected={exportSites}
          onToggle={(name) =>
            setExportSites((prev) => (prev.includes(name) ? prev.filter((x) => x !== name) : [...prev, name]))}
          onAll={() => setExportSites(exportAreas.map((a) => a.name))}
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
  date, areas, view, onView, hasPaper, selected, onToggle, onAll, onNone, busy, onRun, onClose,
}: {
  date: string
  /** The areas of the CHOSEN view, with how many people each holds. */
  areas: { name: string; count: number }[]
  view: AreaView
  onView: (v: AreaView) => void
  /** Whether anybody on this board actually has a «sənəd üzrə ərazi» written. */
  hasPaper: boolean
  selected: string[]
  onToggle: (name: string) => void
  onAll: () => void
  onNone: () => void
  busy: boolean
  onRun: () => void
  onClose: () => void
}) {
  const total = areas.filter((a) => selected.includes(a.name)).reduce((n, a) => n + a.count, 0)

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

        {/* The view, first: it decides what «ərazi» means in the list below, so choosing sites before
            choosing the structure would be answering the second question first. */}
        <div style={{ marginBottom: 12 }}>
          <div className="form-label" style={{ marginBottom: 6 }}>Fayl hansı quruluşda olsun?</div>
          <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
            {(['actual', 'paper'] as const).map((v) => (
              <button
                key={v}
                type="button"
                className={`btn btn-sm${view === v ? '' : ' btn-outline'}`}
                onClick={() => onView(v)}
              >
                {v === 'actual' ? 'Faktiki ərazi' : 'Sənəd üzrə'}
              </button>
            ))}
          </div>
          <div className="muted" style={{ fontSize: 12, marginTop: 6, lineHeight: 1.5 }}>
            {view === 'paper' ? (
              <>
                Adamlar <b>sənədlərinin göstərdiyi əraziyə</b> görə qruplaşır. Sənədi ayrıca
                yazılmayanlar öz filialında qalır.
                {/* Said plainly, because otherwise the two files come out identical and the reader
                    concludes the switch is broken rather than that the field is unfilled. */}
                {!hasPaper && (
                  <>
                    {' '}<b style={{ color: '#b45309' }}>Bu lövhədə hələ heç kimin «sənəd üzrə ərazi»si
                    yazılmayıb</b> — ona görə fayl faktiki ilə eyni çıxacaq.
                  </>
                )}
              </>
            ) : (
              <>Adamlar <b>skan etdikləri filiala</b> görə qruplaşır — indiki qayda.</>
            )}
          </div>
        </div>

        <div style={{ display: 'flex', gap: 8, marginBottom: 10 }}>
          <button className="btn btn-sm" onClick={onAll}>Hamısı</button>
          <button className="btn btn-sm" onClick={onNone}>Heç biri</button>
          <span className="muted" style={{ marginLeft: 'auto', alignSelf: 'center', fontSize: 12 }}>
            {selected.length} ərazi · {total} işçi
          </span>
        </div>

        <div style={{ border: '1px solid var(--c100)', borderRadius: 12, overflow: 'hidden', marginBottom: 14 }}>
          {areas.map((a, i) => (
            <label
              key={a.name}
              style={{
                display: 'flex', alignItems: 'center', gap: 10, padding: '9px 12px', cursor: 'pointer',
                borderTop: i === 0 ? 'none' : '1px solid var(--c100)',
              }}
            >
              <input type="checkbox" checked={selected.includes(a.name)} onChange={() => onToggle(a.name)} />
              <span style={{ flex: 1, fontSize: 13.5, color: 'var(--c900)' }}>{a.name}</span>
              <span className="muted" style={{ fontSize: 12 }}>{a.count} nəfər</span>
            </label>
          ))}
          {areas.length === 0 && (
            <div className="muted" style={{ padding: 14, fontSize: 13 }}>Bu gün üçün ərazi yoxdur.</div>
          )}
        </div>

        <div style={{ display: 'flex', gap: 8, justifyContent: 'flex-end' }}>
          <button className="btn btn-outline" onClick={onClose}>Ləğv et</button>
          <button className="btn" disabled={busy || selected.length === 0} onClick={onRun}>
            {busy ? 'Çıxarılır…' : 'Çıxar'}
          </button>
        </div>
      </div>
    </div>
  )
}
