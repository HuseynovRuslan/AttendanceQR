import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import {
  applyShiftPlan,
  getPlanLocations,
  getShiftPlan,
  undoPlanChange,
  type PlanApply,
  type PlanChange,
  type PlanPreview,
  type PlanDay,
  type PlanEmployee,
  type PlanGrid,
  type PlanLocation,
  type PlanShift,
} from '../../../api/shiftPlan'
import { IconCheck, IconChevronLeft, IconChevronRight, IconSearch, IconX } from '../../../components/icons'
import { HistoryPanel } from './HistoryPanel'
import { PlanDrawer, type DrawerSeed } from './PlanDrawer'
import { PreviewSummary } from './PreviewSummary'
import {
  bandOf, dayLabel, datesOf, initials, LEAVE_LABEL, rangeLabel, rangeOf, shortRestDays, step, weekday, WEEKDAYS_SHORT, type View,
} from './planModel'
import './shiftPlan.css'

/**
 * «Növbə planı» — a branch's people down the side, the days across, a shift in each cell.
 *
 * The screen is built for the most common job: a branch's person in charge putting tomorrow's thirty shifts in
 * place. That is the Day view — one list, one choice per person, saved as it is chosen. The Week and Month views
 * show the same days as a grid: a click on a day changes that day; ticking people and «Yeni plan» writes a range,
 * a weekly pattern or a rotation. Everything else — what a rest day means for the tabel, a night that ends the next
 * morning, an edited shift's history — happens on the server and is not something to set here.
 */

const LOCATION_KEY = 'qrlog.shiftPlan.location'

function readStored(key: string): string | null {
  try { return window.localStorage.getItem(key) } catch { return null }
}
function store(key: string, value: string) {
  try { window.localStorage.setItem(key, value) } catch { /* private window: the choice is simply not remembered */ }
}

function localToday(): string {
  const now = new Date()
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`
}

const ERRORS: Record<string, string> = {
  OutOfScope: 'Bu işçilərin növbəsini dəyişmək səlahiyyətiniz yoxdur.',
  ScheduleBelongsToOtherBranch: 'Seçilmiş növbə başqa filiala aiddir.',
  ScheduleNotFound: 'Növbə tapılmadı — səhifəni yeniləyin.',
  EmployeeNotFound: 'İşçilərdən biri tapılmadı — səhifəni yeniləyin.',
  RangeTooLong: 'Bir planda ən çox 92 gün ola bilər. Daha uzun müddət üçün işçinin daimi növbəsini dəyişin.',
  TooManyCells: 'Plan çox böyükdür. Tarix aralığını və ya işçi sayını azaldın.',
  TooManyEmployees: 'Bir dəfəyə ən çox 500 işçi seçmək olar.',
  DateTooFar: 'Bir ildən uzaq tarixə plan yazmaq olmaz.',
  NoDays: 'Tarix seçilməyib.',
  NoEmployees: 'İşçi seçilməyib.',
}

type Pending =
  | { kind: 'apply'; request: PlanApply; pastDays: number; done?: (n: number) => void }
  | { kind: 'undo'; change: string; pastDays: number }

/** A bulk change waiting for its yes, with what the server says it will do. */
type Check = { request: PlanApply; preview: PlanPreview }

type Toast = { ok: boolean; text: string; undo?: string }

export function ShiftPlanner() {
  const [locations, setLocations] = useState<PlanLocation[] | null>(null)
  const [locationId, setLocationId] = useState<string | null>(null)
  const [view, setView] = useState<View>(() =>
    typeof window !== 'undefined' && window.matchMedia?.('(max-width: 720px)').matches ? 'day' : 'week')
  const [anchor, setAnchor] = useState(localToday)
  const [grid, setGrid] = useState<PlanGrid | null>(null)
  const [loading, setLoading] = useState(true)
  const [failed, setFailed] = useState<string | null>(null)
  const [search, setSearch] = useState('')
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [menu, setMenu] = useState<{ employee: PlanEmployee; day: PlanDay; rect: DOMRect } | null>(null)
  const [drawer, setDrawer] = useState<DrawerSeed | null>(null)
  const [pending, setPending] = useState<Pending | null>(null)
  const [saving, setSaving] = useState(false)
  const [toast, setToast] = useState<Toast | null>(null)
  const [check, setCheck] = useState<Check | null>(null)
  const [history, setHistory] = useState(false)
  const [historyKey, setHistoryKey] = useState(0)
  const request = useRef(0)

  const { from, to } = useMemo(() => rangeOf(view, anchor), [view, anchor])

  // The branches this person may plan; the last one used is opened again.
  useEffect(() => {
    void (async () => {
      const { status, data } = await getPlanLocations()
      if (status !== 200 || !Array.isArray(data)) { setLocations([]); setFailed('Filiallar yüklənmədi.'); setLoading(false); return }
      setLocations(data)
      const remembered = readStored(LOCATION_KEY)
      setLocationId(data.find((l) => l.id === remembered)?.id ?? data[0]?.id ?? null)
      if (data.length === 0) setLoading(false)
    })()
  }, [])

  const load = useCallback(async () => {
    if (!locationId) return
    const mine = ++request.current
    setLoading(true)
    setFailed(null)
    const { status, data } = await getShiftPlan(locationId, from, to)
    if (mine !== request.current) return
    if (status === 200 && data && 'employees' in data) setGrid(data as PlanGrid)
    else setFailed(status === 403 ? 'Bu filialın planına baxmaq səlahiyyətiniz yoxdur.' : 'Plan yüklənmədi. Bağlantını yoxlayıb yenidən cəhd edin.')
    setLoading(false)
  }, [locationId, from, to])

  useEffect(() => { void load() }, [load])
  useEffect(() => {
    if (!toast) return
    const t = window.setTimeout(() => setToast(null), toast.undo ? 12000 : 5000)
    return () => window.clearTimeout(t)
  }, [toast])

  const today = grid?.today ?? localToday()
  const dates = useMemo(() => datesOf(from, to), [from, to])
  const shifts = grid?.schedules ?? []
  const shiftById = useMemo(() => new Map(shifts.map((s) => [s.id, s])), [shifts])

  const people = useMemo(() => {
    const all = grid?.employees ?? []
    const q = search.trim().toLocaleLowerCase('az')
    const visible = q ? all.filter((e) => `${e.name} ${e.position ?? ''}`.toLocaleLowerCase('az').includes(q)) : all
    return [...visible].sort((a, b) => a.name.localeCompare(b.name, 'az'))
  }, [grid, search])

  const shortRest = useMemo(() => new Map((grid?.employees ?? []).map((e) => [e.id, shortRestDays(e.days)])), [grid])

  /** Saves a plan; asks first when it reaches back into days already worked. */
  const apply = useCallback(async (body: PlanApply, done?: (n: number) => void) => {
    setSaving(true)
    const { status, data } = await applyShiftPlan({ ...body, locationId })
    setSaving(false)
    if (status === 200 && data && 'ok' in data) {
      const n = data.set + data.cleared
      const rebuilt = data.rebuiltDays ? `, ${data.rebuiltDays} keçmiş gün yenidən hesablandı` : ''
      const waiting = data.rebuildPending ? ` ${data.rebuildPending} keçmiş günün hesabatı hələ yenilənir — bir neçə dəqiqəyə hazır olacaq.` : ''
      setToast({
        ok: true,
        text: n === 0 ? 'Dəyişiklik yoxdur — plan artıq belədir.' : `Plan yadda saxlandı: ${body.standing ? `${data.people} işçinin daimi növbəsi` : `${n} gün`}${rebuilt}.${waiting}`,
        undo: data.changeId ?? undefined,
      })
      setPending(null)
      setCheck(null)
      setHistoryKey((k) => k + 1)
      done?.(n)
      void load()
      return true
    }
    if (status === 409 && data && 'error' in data && data.error === 'PastDatesNeedConfirm') {
      setCheck(null)
      setPending({ kind: 'apply', request: body, pastDays: data.pastDays ?? 0, done })
      return false
    }
    const code = data && 'error' in data ? data.error : ''
    setToast({ ok: false, text: ERRORS[code] ?? 'Plan yadda saxlanmadı. Yenidən cəhd edin.' })
    return false
  }, [load, locationId])

  /** Asks the server what a bulk change would do, and shows it for a yes before anything is written. */
  const ask = useCallback(async (body: PlanApply) => {
    setSaving(true)
    const { status, data } = await applyShiftPlan({ ...body, locationId, preview: true })
    setSaving(false)
    if (status === 200 && data && 'preview' in data) {
      setCheck({ request: body, preview: data })
      return
    }
    const code = data && 'error' in data ? data.error : ''
    setToast({ ok: false, text: ERRORS[code] ?? 'Plan yoxlanmadı. Yenidən cəhd edin.' })
  }, [locationId])

  /** Puts back what a change replaced; days somebody changed since are left as they are now. */
  const undo = useCallback(async (change: string, confirmPast = false) => {
    setSaving(true)
    const { status, data } = await undoPlanChange(change, confirmPast)
    setSaving(false)
    if (status === 200 && data && 'ok' in data) {
      setToast({
        ok: true,
        text: `Geri alındı: ${data.reverted} dəyişiklik${data.kept ? `. ${data.kept} gün sonradan yenidən dəyişdirildiyi üçün olduğu kimi saxlandı` : ''}.`,
      })
      setPending(null)
      setHistoryKey((k) => k + 1)
      void load()
      return
    }
    if (status === 409 && data && 'error' in data && data.error === 'PastDatesNeedConfirm') {
      setPending({ kind: 'undo', change, pastDays: data.pastDays ?? 0 })
      return
    }
    const code = data && 'error' in data ? data.error : ''
    setToast({
      ok: false,
      text: code === 'AlreadyUndone' ? 'Bu dəyişiklik artıq geri alınıb.'
        : code === 'NothingToUndo' ? 'Geri alınacaq bir şey qalmayıb — bütün günlər sonradan yenidən dəyişdirilib.'
          : code === 'TooOld' ? '30 gündən köhnə dəyişiklik geri alınmır.'
            : 'Geri alınmadı. Yenidən cəhd edin.',
    })
  }, [load])

  function chooseLocation(id: string) {
    setLocationId(id)
    setSelected(new Set())
    store(LOCATION_KEY, id)
  }

  function toggle(id: string) {
    setSelected((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })
  }

  const allSelected = people.length > 0 && people.every((p) => selected.has(p.id))
  function toggleAll() {
    setSelected(allSelected ? new Set() : new Set(people.map((p) => p.id)))
  }

  function openDrawer() {
    const ids = selected.size > 0 ? [...selected] : []
    const start = view === 'day' ? anchor : from >= today ? from : today
    setDrawer({ employeeIds: ids, from: start, to: view === 'day' ? anchor : to < start ? start : to })
  }

  const noShifts = grid !== null && shifts.length === 0

  return (
    <div className="sp">
      <div className="sp-bar" role="toolbar" aria-label="Plan">
        <label className="sp-field">
          <span className="sr-only">Filial</span>
          <select
            className="sp-select"
            value={locationId ?? ''}
            onChange={(e) => chooseLocation(e.target.value)}
            disabled={!locations || locations.length === 0}
          >
            {(locations ?? []).map((l) => <option key={l.id} value={l.id}>{l.name}</option>)}
          </select>
        </label>

        <label className="sp-field sp-search">
          <IconSearch aria-hidden />
          <span className="sr-only">İşçi axtar</span>
          <input type="search" placeholder="İşçi axtar" value={search} onChange={(e) => setSearch(e.target.value)} />
        </label>

        <div className="seg sp-views" role="group" aria-label="Görünüş">
          {([['day', 'Gün'], ['week', 'Həftə'], ['month', 'Ay']] as const).map(([v, label]) => (
            <button key={v} type="button" className="seg-btn" aria-pressed={view === v} onClick={() => setView(v)}>{label}</button>
          ))}
        </div>

        <div className="sp-nav">
          <button type="button" className="btn btn-sm btn-ghost" aria-label="Əvvəlki" onClick={() => setAnchor(step(view, anchor, -1))}><IconChevronLeft /></button>
          <button type="button" className="btn btn-sm" onClick={() => setAnchor(today)}>Bu gün</button>
          <button type="button" className="btn btn-sm btn-ghost" aria-label="Növbəti" onClick={() => setAnchor(step(view, anchor, 1))}><IconChevronRight /></button>
          <span className="sp-range" aria-live="polite">{rangeLabel(view, from, to)}</span>
        </div>

        <div className="sp-actions">
          <button type="button" className="btn" onClick={() => setHistory(true)} disabled={!locationId}>Tarixçə</button>
          <button type="button" className="btn btn-primary" onClick={openDrawer} disabled={!grid || noShifts}>Yeni plan</button>
        </div>
      </div>

      {toast && (
        <div className={`fb ${toast.ok ? 'fb-ok' : 'fb-err'} sp-toast`} role="status">
          {toast.ok ? <IconCheck /> : <IconX />}<span>{toast.text}</span>
          {toast.undo && (
            <button type="button" className="btn btn-sm sp-undo" disabled={saving}
              onClick={() => { const id = toast.undo!; setToast(null); void undo(id) }}>Geri al</button>
          )}
        </div>
      )}

      {locations && locations.length === 0 && !failed && (
        <div className="card card-pad sp-empty">Planlaşdıra biləcəyiniz filial yoxdur.</div>
      )}

      {failed && (
        <div className="card card-pad sp-empty" role="alert">
          <p>{failed}</p>
          <button type="button" className="btn btn-sm" onClick={() => void load()}>Yenidən yüklə</button>
        </div>
      )}

      {!failed && loading && !grid && <GridSkeleton />}

      {!failed && grid && noShifts && (
        <div className="card card-pad sp-empty">
          <p>Bu filial üçün hələ növbə şablonu yoxdur. Planlaşdırmaq üçün əvvəlcə «Növbə şablonları» bölməsində ən azı bir növbə yaradın.</p>
        </div>
      )}

      {!failed && grid && !noShifts && grid.employees.length === 0 && (
        <div className="card card-pad sp-empty">Bu filialda aktiv işçi yoxdur.</div>
      )}

      {!failed && grid && !noShifts && grid.employees.length > 0 && (
        <div className={`card sp-board${loading ? ' sp-busy' : ''}`} aria-busy={loading}>
          {view === 'day' ? (
            <DayList
              people={people}
              date={anchor}
              today={today}
              shifts={shifts}
              shiftById={shiftById}
              selected={selected}
              allSelected={allSelected}
              onToggle={toggle}
              onToggleAll={toggleAll}
              saving={saving}
              onChoose={(employeeIds, value) => void (employeeIds.length > 1
                ? ask({ employeeIds, steps: [value], dates: [anchor] })
                : apply({ employeeIds, steps: [value], dates: [anchor] }))}
            />
          ) : (
            <Grid
              view={view}
              people={people}
              dates={dates}
              today={today}
              shortRest={shortRest}
              selected={selected}
              allSelected={allSelected}
              onToggle={toggle}
              onToggleAll={toggleAll}
              onCell={(employee, day, rect) => setMenu({ employee, day, rect })}
            />
          )}
          {people.length === 0 && <div className="sp-none">«{search}» adlı işçi tapılmadı.</div>}
        </div>
      )}

      {grid && view !== 'day' && <Legend />}

      {selected.size > 0 && (
        <div className="sp-selection" role="region" aria-label="Seçilmiş işçilər">
          <span><b>{selected.size}</b> işçi seçildi</span>
          <button type="button" className="btn btn-primary btn-sm" onClick={openDrawer}>Seçilənlərə plan yaz</button>
          <button type="button" className="btn btn-sm btn-ghost" onClick={() => setSelected(new Set())}>Seçimi təmizlə</button>
        </div>
      )}

      {menu && (
        <CellMenu
          {...menu}
          shifts={shifts}
          onClose={() => setMenu(null)}
          onChoose={(value) => {
            const target = menu
            setMenu(null)
            void apply({ employeeIds: [target.employee.id], steps: [value], dates: [target.day.date] })
          }}
        />
      )}

      {drawer && grid && (
        <PlanDrawer
          seed={drawer}
          employees={grid.employees}
          shifts={shifts}
          today={today}
          locationId={locationId}
          saving={saving}
          onClose={() => setDrawer(null)}
          onApply={(body) => apply(body, () => { setDrawer(null); setSelected(new Set()) })}
        />
      )}

      {check && (
        <CheckDialog
          check={check}
          names={(grid?.employees ?? []).filter((e) => check.request.employeeIds.includes(e.id)).map((e) => e.name)}
          saving={saving}
          onCancel={() => setCheck(null)}
          onConfirm={() => void apply(check.request, () => setSelected(new Set()))}
        />
      )}

      {history && locationId && (
        <HistoryPanel
          locationId={locationId}
          reloadKey={historyKey}
          busy={saving}
          onClose={() => setHistory(false)}
          onUndo={(change: PlanChange) => void undo(change.id)}
        />
      )}

      {pending && (
        <ConfirmPast
          pending={pending}
          saving={saving}
          onCancel={() => setPending(null)}
          onConfirm={() => void (pending.kind === 'undo'
            ? undo(pending.change, true)
            : apply({ ...pending.request, confirmPast: true }, pending.done))}
        />
      )}
    </div>
  )
}

/* ---- the day: one list, one choice per person ------------------------------------------------------------------- */

function DayList(props: {
  people: PlanEmployee[]
  date: string
  today: string
  shifts: PlanShift[]
  shiftById: Map<string, PlanShift>
  selected: Set<string>
  allSelected: boolean
  onToggle: (id: string) => void
  onToggleAll: () => void
  saving: boolean
  onChoose: (employeeIds: string[], value: string) => void
}) {
  const { people, date, today, shifts, selected, allSelected, onToggle, onToggleAll, saving, onChoose } = props
  const [bulk, setBulk] = useState('')
  const past = date < today

  return (
    <div className="sp-daylist">
      {selected.size > 1 && (
        <div className="sp-day-bulk">
          <span>Seçilən {selected.size} işçiyə:</span>
          <ShiftSelect value={bulk} shifts={shifts} placeholder="Növbə seçin" onChange={setBulk} />
          <button type="button" className="btn btn-sm btn-primary" disabled={!bulk || saving}
            onClick={() => { onChoose([...selected], bulk); setBulk('') }}>Tətbiq et</button>
        </div>
      )}
      <table className="sp-table sp-daytable">
        <thead>
          <tr>
            <th scope="col" className="sp-check"><input type="checkbox" aria-label="Hamısını seç" checked={allSelected} onChange={onToggleAll} /></th>
            <th scope="col">İşçi</th>
            <th scope="col">Növbə {past && <span className="sp-hint">(keçmiş gün)</span>}</th>
            <th scope="col" className="sp-actual">Faktiki</th>
          </tr>
        </thead>
        <tbody>
          {people.map((p) => {
            const day = p.days.find((d) => d.date === date)
            if (!day) return null
            const value = day.kind === 'plan' ? day.scheduleId ?? '' : day.kind === 'rest' ? 'rest' : 'clear'
            return (
              <tr key={p.id} className={selected.has(p.id) ? 'sp-row-on' : undefined}>
                <td className="sp-check"><input type="checkbox" aria-label={`${p.name} seç`} checked={selected.has(p.id)} onChange={() => onToggle(p.id)} /></td>
                <th scope="row" className="sp-person">
                  <span className="sp-name">{p.name}</span>
                  {p.position && <span className="sp-pos">{p.position}</span>}
                </th>
                <td>
                  <ShiftSelect
                    value={value}
                    shifts={shifts}
                    own={day.kind === 'base' || day.kind === 'off' ? day : null}
                    label={`${p.name}: növbə`}
                    disabled={saving}
                    onChange={(v) => { if (v !== value) onChoose([p.id], v) }}
                  />
                  {(day.leave || day.holiday) && <span className="sp-tag sp-tag-leave">{day.leave ? LEAVE_LABEL[day.leave] ?? day.leave : day.holiday}</span>}
                </td>
                <td className="sp-actual"><Actual day={day} today={today} /></td>
              </tr>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}

/** The choice for one day: a shift, a day off, or the person's own standing shift. */
function ShiftSelect(props: {
  value: string
  shifts: PlanShift[]
  own?: PlanDay | null
  placeholder?: string
  label?: string
  disabled?: boolean
  onChange: (value: string) => void
}) {
  const { value, shifts, own, placeholder, label, disabled, onChange } = props
  // The shift first: on a phone the end of the text is what gets cut off, and the hours are what matters.
  const ownText = own
    ? own.working && own.start ? `${[own.shift, `${own.start}–${own.end}`].filter(Boolean).join(' ')} · öz növbəsi` : 'İstirahət günü · öz növbəsi'
    : 'Öz növbəsinə qaytar'
  return (
    <select className="sp-select sp-shiftselect" value={value} aria-label={label ?? placeholder} disabled={disabled} onChange={(e) => onChange(e.target.value)}>
      {placeholder && <option value="" disabled>{placeholder}</option>}
      <option value="clear">{ownText}</option>
      {shifts.map((s) => <option key={s.id} value={s.id}>{s.name} · {s.start}–{s.end}</option>)}
      <option value="rest">İstirahət</option>
    </select>
  )
}

function Actual({ day, today }: { day: PlanDay; today: string }) {
  if (day.checkIn) return <span className="sp-times">{day.checkIn}–{day.checkOut ?? 'işdə'}</span>
  if (day.date < today && day.working && !day.leave && !day.holiday) return <span className="sp-missed">Gəlməyib</span>
  return <span className="muted">—</span>
}

/* ---- the week and the month: people × days ------------------------------------------------------------------------ */

function Grid(props: {
  view: View
  people: PlanEmployee[]
  dates: string[]
  today: string
  shortRest: Map<string, Set<string>>
  selected: Set<string>
  allSelected: boolean
  onToggle: (id: string) => void
  onToggleAll: () => void
  onCell: (employee: PlanEmployee, day: PlanDay, rect: DOMRect) => void
}) {
  const { view, people, dates, today, shortRest, selected, allSelected, onToggle, onToggleAll, onCell } = props
  const compact = view === 'month'
  return (
    <div className="sp-scroll">
      <table className={`sp-table sp-grid${compact ? ' sp-compact' : ''}`}>
        <thead>
          <tr>
            <th scope="col" className="sp-check sp-sticky"><input type="checkbox" aria-label="Hamısını seç" checked={allSelected} onChange={onToggleAll} /></th>
            <th scope="col" className="sp-personcol sp-sticky2">İşçi</th>
            {dates.map((d) => {
              const wd = weekday(d)
              return (
                <th key={d} scope="col" className={`sp-dayhead${d === today ? ' sp-today' : ''}${wd === 0 || wd === 6 ? ' sp-weekend' : ''}`} aria-label={dayLabel(d)}>
                  <span className="sp-wd">{WEEKDAYS_SHORT[wd]}</span>
                  <span className="sp-dn">{Number(d.slice(8, 10))}</span>
                </th>
              )
            })}
          </tr>
        </thead>
        <tbody>
          {people.map((p) => (
            <tr key={p.id} className={selected.has(p.id) ? 'sp-row-on' : undefined}>
              <td className="sp-check sp-sticky"><input type="checkbox" aria-label={`${p.name} seç`} checked={selected.has(p.id)} onChange={() => onToggle(p.id)} /></td>
              <th scope="row" className="sp-person sp-sticky2">
                <span className="sp-name">{p.name}</span>
                {!compact && p.position && <span className="sp-pos">{p.position}</span>}
              </th>
              {p.days.map((day) => (
                <td key={day.date} className={`sp-cell${day.date === today ? ' sp-today' : ''}`}>
                  <button
                    type="button"
                    className="sp-cellbtn"
                    aria-label={cellLabel(p, day)}
                    aria-haspopup="menu"
                    onClick={(e) => onCell(p, day, e.currentTarget.getBoundingClientRect())}
                  >
                    <Chip day={day} compact={compact} />
                    {!compact && (day.checkIn || (day.date < today && day.working && !day.leave && !day.holiday)) && <Actual day={day} today={today} />}
                    {shortRest.get(p.id)?.has(day.date) && <span className="sp-warn" title="Əvvəlki növbədən sonra istirahət 11 saatdan azdır" aria-hidden>!</span>}
                  </button>
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

function cellLabel(p: PlanEmployee, day: PlanDay): string {
  const what = day.kind === 'rest' || (day.kind === 'off')
    ? 'istirahət'
    : `${day.shift ?? 'növbə'} ${day.start}–${day.end}${day.kind === 'base' ? ', öz növbəsi' : ', planlı'}`
  return `${p.name}, ${dayLabel(day.date)}: ${what}${day.leave ? `, ${LEAVE_LABEL[day.leave] ?? day.leave}` : ''}. Dəyişmək üçün açın.`
}

function Chip({ day, compact }: { day: PlanDay; compact: boolean }) {
  if (day.leave) return <span className="sp-chip sp-leave">{compact ? (LEAVE_LABEL[day.leave] ?? day.leave).slice(0, 2) : LEAVE_LABEL[day.leave] ?? day.leave}</span>
  if (day.kind === 'rest') return <span className="sp-chip sp-rest">{compact ? '–' : 'İstirahət'}</span>
  if (day.kind === 'off' || !day.working) return <span className="sp-chip sp-off">{day.holiday && !compact ? 'Bayram' : '·'}</span>
  const band = bandOf(day.start, day.overnight)
  const planned = day.kind === 'plan'
  return (
    <span className={`sp-chip sp-${band}${planned ? ' sp-planned' : ' sp-own'}`}>
      {/* A shift with no name (the branch's own hours) shows its start hour — a «—» would read as «nothing». */}
      {compact ? (day.shift ? initials(day.shift) : (day.start ?? '').slice(0, 2)) : (
        <>
          {day.shift && <span className="sp-chipname">{day.shift}</span>}
          <span className="sp-chiptime">{day.start}–{day.end}</span>
        </>
      )}
    </span>
  )
}

function Legend() {
  return (
    <ul className="sp-legend" aria-label="İşarələr">
      <li><span className="sp-chip sp-morning sp-planned">Planlı</span> gün üçün yazılıb</li>
      <li><span className="sp-chip sp-morning sp-own">Öz növbəsi</span> plan yoxdur, daimi növbə</li>
      <li><span className="sp-chip sp-rest">İstirahət</span> planlı istirahət</li>
      <li><span className="sp-band sp-morning" />Səhər <span className="sp-band sp-day" />Gündüz <span className="sp-band sp-evening" />Axşam <span className="sp-band sp-night" />Gecə</li>
    </ul>
  )
}

/* ---- one cell's choices ---------------------------------------------------------------------------------------------- */

function CellMenu(props: {
  employee: PlanEmployee
  day: PlanDay
  rect: DOMRect
  shifts: PlanShift[]
  onClose: () => void
  onChoose: (value: string) => void
}) {
  const { employee, day, rect, shifts, onClose, onChoose } = props
  const ref = useRef<HTMLDivElement>(null)

  useEffect(() => {
    ref.current?.querySelector<HTMLButtonElement>('button[aria-checked="true"], button')?.focus()
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose() }
    const onDown = (e: MouseEvent) => { if (ref.current && !ref.current.contains(e.target as Node)) onClose() }
    window.addEventListener('keydown', onKey)
    window.addEventListener('mousedown', onDown)
    return () => { window.removeEventListener('keydown', onKey); window.removeEventListener('mousedown', onDown) }
  }, [onClose])

  function onKeyDown(e: React.KeyboardEvent) {
    if (e.key !== 'ArrowDown' && e.key !== 'ArrowUp') return
    e.preventDefault()
    const items = [...(ref.current?.querySelectorAll<HTMLButtonElement>('[role="menuitemradio"]') ?? [])]
    const at = items.indexOf(document.activeElement as HTMLButtonElement)
    items[(at + (e.key === 'ArrowDown' ? 1 : items.length - 1)) % items.length]?.focus()
  }

  const width = 260
  const left = Math.max(8, Math.min(rect.left + rect.width / 2 - width / 2, window.innerWidth - width - 8))
  const below = rect.bottom + 8 + 320 < window.innerHeight
  const style = below ? { left, top: rect.bottom + 6, width } : { left, bottom: window.innerHeight - rect.top + 6, width }
  const current = day.kind === 'plan' ? day.scheduleId : day.kind === 'rest' ? 'rest' : 'clear'

  return (
    <div ref={ref} className="sp-menu" role="menu" aria-label={`${employee.name}, ${dayLabel(day.date)}`} style={style} onKeyDown={onKeyDown}>
      <div className="sp-menuhead">
        <span className="sp-name">{employee.name}</span>
        <span className="sp-pos">{dayLabel(day.date)}</span>
      </div>
      {shifts.map((s) => (
        <button key={s.id} type="button" role="menuitemradio" aria-checked={current === s.id} className="sp-menuitem" onClick={() => onChoose(s.id)}>
          <span className={`sp-band sp-${bandOf(s.start, s.overnight)}`} aria-hidden />
          <span className="sp-menuname">{s.name}</span>
          <span className="sp-menutime">{s.start}–{s.end}</span>
        </button>
      ))}
      <button type="button" role="menuitemradio" aria-checked={current === 'rest'} className="sp-menuitem" onClick={() => onChoose('rest')}>
        <span className="sp-band sp-restband" aria-hidden />
        <span className="sp-menuname">İstirahət</span>
      </button>
      <button type="button" role="menuitemradio" aria-checked={current === 'clear'} className="sp-menuitem sp-menuown" onClick={() => onChoose('clear')}>
        <span className="sp-menuname">Öz növbəsi</span>
        <span className="sp-menutime">plan yoxdur</span>
      </button>
    </div>
  )
}

/* ---- a plan that reaches back into days already worked ------------------------------------------------------------- */

function ConfirmPast({ pending, saving, onCancel, onConfirm }: { pending: Pending; saving: boolean; onCancel: () => void; onConfirm: () => void }) {
  const ref = useRef<HTMLDivElement>(null)
  useEffect(() => {
    ref.current?.querySelector<HTMLButtonElement>('.btn')?.focus()
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onCancel() }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onCancel])
  return (
    <div className="sp-overlay" role="presentation">
      <div ref={ref} className="sp-dialog" role="alertdialog" aria-modal="true" aria-labelledby="sp-past-title" aria-describedby="sp-past-text">
        <h2 id="sp-past-title">Keçmiş günlər dəyişəcək</h2>
        <p id="sp-past-text">
          {pending.kind === 'undo' ? 'Geri alma' : 'Plan'} <b>{pending.pastDays}</b> keçmiş günü əhatə edir. O günlərin tabeli, saatları və maaş
          hesabatı yenidən hesablanacaq. Dəyişiklik kimin etdiyi ilə birlikdə tarixçəyə yazılır və 30 gün ərzində geri alına bilər.
        </p>
        <div className="sp-dialogbtns">
          <button type="button" className="btn" onClick={onCancel}>Ləğv et</button>
          <button type="button" className="btn btn-primary" disabled={saving} onClick={onConfirm}>Bəli, dəyiş</button>
        </div>
      </div>
    </div>
  )
}

function CheckDialog({ check, names, saving, onCancel, onConfirm }: { check: Check; names: string[]; saving: boolean; onCancel: () => void; onConfirm: () => void }) {
  const ref = useRef<HTMLDivElement>(null)
  useEffect(() => {
    ref.current?.querySelector<HTMLButtonElement>('.btn')?.focus()
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onCancel() }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onCancel])
  const nothing = !check.preview.standing && (check.preview.newDays ?? 0) + (check.preview.replacedDays ?? 0) + (check.preview.clearedDays ?? 0) === 0
  return (
    <div className="sp-overlay" role="presentation">
      <div ref={ref} className="sp-dialog" role="alertdialog" aria-modal="true" aria-labelledby="sp-check-title">
        <h2 id="sp-check-title">{check.preview.people} işçi üçün dəyişiklik</h2>
        <PreviewSummary preview={check.preview} names={names} />
        <div className="sp-dialogbtns">
          <button type="button" className="btn" onClick={onCancel}>Ləğv et</button>
          <button type="button" className="btn btn-primary" disabled={saving || nothing} onClick={onConfirm}>Təsdiqlə və yadda saxla</button>
        </div>
      </div>
    </div>
  )
}

function GridSkeleton() {
  return (
    <div className="card sp-board sp-skeleton" aria-busy="true" aria-label="Yüklənir">
      {Array.from({ length: 6 }, (_, i) => <div key={i} className="sp-skelrow"><span /><span /><span /><span /></div>)}
    </div>
  )
}
