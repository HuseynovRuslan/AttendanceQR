import { Fragment, useEffect, useState } from 'react'
import { EmployeeLink } from '../../components/EmployeeLink'
import {
  createSchedule,
  getAdminLocations,
  deleteSchedule,
  getEmployees,
  getSchedules,
  updateSchedule,
  type AdminEmployee,
  type Schedule,
  type ScheduleInput,
} from '../../api/admin'
import {
  createManagerSchedule,
  deleteManagerSchedule,
  getManagerEmployees,
  getManagerSchedules,
  updateManagerSchedule,
} from '../../api/manager'
import { useAuth } from '../../auth/AuthContext'
import { WorkCyclePicker, NO_CYCLE, type WorkCycleValue } from '../../components/WorkCyclePicker'
import { IconCheck, IconTrash, IconX } from '../../components/icons'

/**
 * Named shifts ("Növbələr") — hours, working days and an optional rotation, defined once and assigned
 * to employees from their own page.
 *
 * Worth its own screen because the alternative is what production actually had: shifts buried in the
 * location form, three companies each with a "Gecə növbəsi" row saying 22:00–06:00 while the eight
 * people working nights were on 21:00–07:00, and a duplicate "gece" nobody noticed. A library you
 * cannot see all of drifts from the thing it describes.
 *
 * Shared by admins and managers — a manager is usually the one who knows what hours their crews
 * actually work, and only being able to report a wrong shift rather than fix it is how the old
 * library drifted in the first place. The endpoints differ: a manager's edit is refused server-side
 * once anyone outside their branches is on the shift, since editing re-judges their past days too.
 */

const DAYS = ['B.e', 'Ç.a', 'Ç', 'C.a', 'C', 'Ş', 'B'] // Monday-first for reading; bit index below
const BIT = [1, 2, 3, 4, 5, 6, 0] // .NET DayOfWeek: Sunday = 0

const ERRORS: Record<string, string> = {
  ScheduleUsedOutsideBranch: 'Bu növbədə başqa filialın işçiləri var — dəyişiklik yalnız admin tərəfindən edilə bilər',
  ScheduleInUse: 'Bu növbədə işçi var — silmək olmaz',
  NameRequired: 'Ad tələb olunur',
  ShiftStartInvalid: 'Başlama saatı düzgün deyil',
  ShiftEndInvalid: 'Bitmə saatı düzgün deyil',
  LateThresholdNegative: 'Gecikmə həddi mənfi ola bilməz',
  WorkCycleDaysInvalid: 'Dövrə 2–28 gün aralığında olmalıdır',
  WorkCycleOnDaysInvalid: 'İş günlərinin sayı dövrədən az olmalıdır',
  WorkCycleAnchorRequired: 'Dövrə üçün işlədiyi bir gün seçilməlidir',
  ScheduleNotFound: 'Növbə tapılmadı',
}

type FormState = {
  name: string
  /** '' = a shift the whole company shares. */
  locationId: string
  shiftStart: string
  shiftEnd: string
  lateThresholdMinutes: string
  workDaysMask: number
  cycle: WorkCycleValue
  /** Days whose hours differ, keyed by .NET day number as a string ("0" = Sunday … "6" = Saturday). */
  dayHours: Record<string, { start: string; end: string }>
}

const EMPTY: FormState = {
  name: '',
  locationId: '',
  shiftStart: '09:00',
  shiftEnd: '18:00',
  lateThresholdMinutes: '15',
  workDaysMask: 126,
  cycle: NO_CYCLE,
  dayHours: {},
}

export function SchedulesPage() {
  const { role } = useAuth()
  const [branches, setBranches] = useState<{ id: string; name: string }[]>([])
  const isManager = role === 'Manager'
  // Same screen, different surface: a manager's writes are scope-checked server-side.
  const api = isManager
    ? {
        list: getManagerSchedules,
        staff: getManagerEmployees,
        create: createManagerSchedule,
        update: updateManagerSchedule,
        remove: deleteManagerSchedule,
      }
    : {
        list: getSchedules,
        staff: getEmployees,
        create: createSchedule,
        update: updateSchedule,
        remove: deleteSchedule,
      }

  const [rows, setRows] = useState<Schedule[]>([])
  const [employees, setEmployees] = useState<AdminEmployee[]>([])
  const [loading, setLoading] = useState(true)
  const [showForm, setShowForm] = useState(false)
  const [editingId, setEditingId] = useState<string | null>(null)
  const [form, setForm] = useState<FormState>(EMPTY)
  const [saving, setSaving] = useState(false)
  const [err, setErr] = useState<string | null>(null)
  const [ok, setOk] = useState<string | null>(null)
  // "Ərazi üzrə ayır" + "kim hansı növbədə": an area filter, and each shift row expands to its people.
  const [filterLoc, setFilterLoc] = useState<string | null>(null)
  const [expanded, setExpanded] = useState<string | null>(null)

  async function refresh() {
    setLoading(true)
    const [s, e] = await Promise.all([api.list(), api.staff()])
    if (s.status === 200 && Array.isArray(s.data)) setRows(s.data as Schedule[])
    if (e.status === 200 && Array.isArray(e.data)) setEmployees(e.data as AdminEmployee[])
    setLoading(false)
  }

  useEffect(() => { void refresh() }, [])

  // The branch list for the form's picker. A manager gets their own branches back from the same
  // endpoint, so the choice they are offered is already the choice they are allowed.
  useEffect(() => {
    void getAdminLocations().then(({ status, data }) => {
      if (status === 200 && Array.isArray(data)) setBranches(data.map((l) => ({ id: l.id, name: l.name })))
    })
  }, [])

  /** How many people are on each shift — the number that decides whether it can be deleted, and the
   *  one an admin needs before editing hours that will move somebody's pay. */
  const usedBy = (id: string) => employees.filter((e) => e.scheduleId === id && e.isActive).length
  // People on a shift within the chosen area (all areas when no filter) — for the shown count and the
  // expander. Delete still checks usedBy (the true total), so a filter can't green-light a delete.
  const staffOf = (id: string) =>
    employees.filter((e) => e.scheduleId === id && e.isActive && (!filterLoc || e.locationId === filterLoc))
  // Distinct areas that actually have staff, for the filter chips + per-shift grouping.
  const areas = Array.from(
    new Map(employees.filter((e) => e.isActive).map((e) => [e.locationId, e.locationName] as const)).entries(),
  )
    .map(([id, name]) => ({ id, name: name ?? '—' }))
    .sort((a, b) => a.name.localeCompare(b.name, 'az'))

  function startCreate() {
    setEditingId(null)
    setForm(EMPTY)
    setErr(null); setOk(null)
    setShowForm(true)
  }

  function startEdit(s: Schedule) {
    setEditingId(s.id)
    setForm({
      name: s.name,
      locationId: s.locationId ?? '',
      shiftStart: s.shiftStart,
      shiftEnd: s.shiftEnd,
      lateThresholdMinutes: String(s.lateThresholdMinutes),
      workDaysMask: s.workDaysMask,
      cycle: s.workCycleDays
        ? { days: s.workCycleDays, onDays: s.workCycleOnDays ?? 1, anchor: s.workCycleAnchor ?? '' }
        : NO_CYCLE,
      dayHours: { ...(s.dayHours ?? {}) },
    })
    setErr(null); setOk(null)
    setShowForm(true)
  }

  function toggleDay(bit: number) {
    setForm((f) => {
      const mask = f.workDaysMask ^ (1 << bit)
      // Turning a day OFF drops its hours with it: an override on a day nobody works is invisible in
      // the form and still in the column, waiting to surprise whoever turns the day back on.
      const dayHours = { ...f.dayHours }
      if ((mask & (1 << bit)) === 0) delete dayHours[String(bit)]
      return { ...f, workDaysMask: mask, dayHours }
    })
  }

  function setDayHours(key: string, hours: { start: string; end: string } | null) {
    setForm((f) => {
      const next = { ...f.dayHours }
      if (hours) next[key] = hours
      else delete next[key]
      return { ...f, dayHours: next }
    })
  }

  async function save() {
    if (!form.name.trim()) { setErr('Ad tələb olunur'); return }
    setSaving(true); setErr(null)
    const payload: ScheduleInput = {
      name: form.name.trim(),
      locationId: form.locationId || null,
      shiftStart: form.shiftStart,
      shiftEnd: form.shiftEnd,
      lateThresholdMinutes: Number(form.lateThresholdMinutes) || 0,
      workDaysMask: form.workDaysMask,
      workCycleDays: form.cycle.days,
      workCycleOnDays: form.cycle.days ? form.cycle.onDays : null,
      workCycleAnchor: form.cycle.days ? form.cycle.anchor || null : null,
      // A rotation replaces the weekly calendar, so "this weekday is different" has nothing to attach
      // to — the form hides the section then, and this makes sure a leftover cannot be saved either.
      dayHours: form.cycle.days ? {} : form.dayHours,
    }
    const res = editingId ? await api.update(editingId, payload) : await api.create(payload)
    setSaving(false)
    if (res.status === 200 && res.data && !('error' in res.data)) {
      setOk(editingId ? 'Növbə yeniləndi' : 'Növbə yaradıldı')
      setShowForm(false)
      void refresh()
    } else {
      const code = res.data && 'error' in res.data ? res.data.error : ''
      setErr(ERRORS[code] ?? 'Yadda saxlanmadı')
    }
  }

  async function remove(s: Schedule) {
    const n = usedBy(s.id)
    if (n > 0) {
      window.alert(`"${s.name}" növbəsində ${n} işçi var. Əvvəlcə onları başqa növbəyə keçirin.`)
      return
    }
    if (!window.confirm(`"${s.name}" növbəsi silinsin?`)) return
    const { status, data } = await api.remove(s.id)
    if (status === 200) { setOk('Növbə silindi'); void refresh() }
    else if (data && 'error' in data && data.error === 'ScheduleInUse')
      setErr('Bu növbədə işçi var — silmək olmaz')
    else setErr('Silinmədi')
  }

  /** Human summary of which days a shift covers — a bitmask is not something to read off a screen. */
  function daysLabel(s: Schedule): string {
    if (s.workCycleDays) {
      const off = s.workCycleDays - (s.workCycleOnDays ?? 1)
      if (s.workCycleDays === 2) return '🔄 Bir gündən bir'
      if (s.workCycleDays === 3 && s.workCycleOnDays === 1) return '🔄 Sutka (1/2)'
      return `🔄 ${s.workCycleOnDays} iş / ${off} istirahət`
    }
    const on = BIT.map((b, i) => ((s.workDaysMask & (1 << b)) !== 0 ? DAYS[i] : null)).filter(Boolean)
    if (on.length === 7) return 'Hər gün'
    if (on.length === 6 && (s.workDaysMask & 1) === 0) return 'B.e – Ş (bazar istirahət)'
    return on.join(', ')
  }

  return (
    <div>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 16, gap: 12, flexWrap: 'wrap' }}>
        <p className="muted" style={{ fontSize: 13, margin: 0, maxWidth: '62ch', lineHeight: 1.6 }}>
          Növbəni bir dəfə qurursunuz, sonra işçiləri ona təyin edirsiniz. Saatları dəyişsəniz, o
          növbədəki <b>bütün</b> işçilərə — keçmiş günlərin hesabatına da — təsir edir.
          {isManager && ' Başqa filialın işçisi olan növbəni dəyişə bilməzsiniz.'}
        </p>
        <button className="btn btn-primary" onClick={startCreate}>+ Yeni növbə</button>
      </div>

      {/* Ərazi üzrə ayır: filter the per-shift counts and the expander to one branch. */}
      {areas.length > 1 && (
        <div className="chip-row" style={{ marginBottom: 14 }}>
          <span className={`chip${!filterLoc ? ' active' : ''}`} onClick={() => setFilterLoc(null)}>
            Bütün ərazilər
          </span>
          {areas.map((a) => (
            <span key={a.id} className={`chip${filterLoc === a.id ? ' active' : ''}`} onClick={() => setFilterLoc(a.id)}>
              {a.name}
            </span>
          ))}
        </div>
      )}

      {ok && <div className="fb fb-ok" style={{ marginBottom: 12 }}><IconCheck /><span>{ok}</span></div>}
      {err && !showForm && <div className="fb fb-err" style={{ marginBottom: 12 }}><IconX /><span>{err}</span></div>}

      {showForm && (
        <div className="card card-pad" style={{ marginBottom: 16 }}>
          <div className="card-title">{editingId ? 'Növbəni redaktə et' : 'Yeni növbə'}</div>

          <div className="form-row cols2">
            <div>
              <label className="form-label">Ad</label>
              <input
                className="inp"
                placeholder="məs. Gecə A"
                value={form.name}
                onChange={(e) => setForm((f) => ({ ...f, name: e.target.value }))}
              />
            </div>
            <div>
              {/* Which branch this shift belongs to. Left on "Bütün şirkət" it behaves exactly as
                  every shift did before there was a choice — offered to everybody. Pinned to a branch
                  it appears only on that branch's staff cards, which is the difference between a
                  picker of two and a picker of twenty once there are ten branches. */}
              <label className="form-label">Filial</label>
              <select
                className="inp"
                value={form.locationId}
                onChange={(e) => setForm((f) => ({ ...f, locationId: e.target.value }))}
              >
                <option value="">Bütün şirkət</option>
                {branches.map((b) => (
                  <option key={b.id} value={b.id}>{b.name}</option>
                ))}
              </select>
            </div>
            <div>
              <label className="form-label">Gecikmə həddi (dəq)</label>
              <input
                className="inp"
                type="number"
                min={0}
                value={form.lateThresholdMinutes}
                onChange={(e) => setForm((f) => ({ ...f, lateThresholdMinutes: e.target.value }))}
              />
            </div>
          </div>

          <div className="form-row cols2">
            <div>
              <label className="form-label">Başlama</label>
              <input className="inp" type="time" value={form.shiftStart}
                onChange={(e) => setForm((f) => ({ ...f, shiftStart: e.target.value }))} />
            </div>
            <div>
              <label className="form-label">Bitmə</label>
              <input className="inp" type="time" value={form.shiftEnd}
                onChange={(e) => setForm((f) => ({ ...f, shiftEnd: e.target.value }))} />
            </div>
          </div>
          {form.shiftEnd < form.shiftStart && (
            <div className="fb fb-info" style={{ marginBottom: 14 }}>
              <span>🌙 Gecə növbəsi — gecə yarısını keçir, səhər çıxış həmin növbəyə yazılır.</span>
            </div>
          )}

          {/* The rotation replaces the weekly days entirely, so only one of the two is ever shown. */}
          {!form.cycle.days && (
            <div style={{ marginBottom: 14 }}>
              <label className="form-label">İş günləri</label>
              <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
                {DAYS.map((d, i) => {
                  const on = (form.workDaysMask & (1 << BIT[i])) !== 0
                  return (
                    <button key={d} type="button" className={on ? 'chip active' : 'chip'} onClick={() => toggleDay(BIT[i])}>
                      {d}
                    </button>
                  )
                })}
              </div>
            </div>
          )}

          {/* Days that run to a different clock.
              A shift held one start and one end, which cannot describe a crew that works 08:00–18:00
              on weekdays and 09:00–18:00 at the weekend — and an employee holds one schedule, so
              there was nowhere to put the second pair. Only the days that DIFFER are listed; every
              other working day follows the times above.
              Hidden under a rotation, which replaces the weekly calendar rather than layering on it. */}
          {!form.cycle.days && (
            <div style={{ marginBottom: 14 }}>
              <label className="form-label">Fərqli saatlı günlər</label>
              {Object.keys(form.dayHours).length === 0 && (
                <div className="muted" style={{ fontSize: 12, marginBottom: 8 }}>
                  Hamısı yuxarıdakı saatla işləyir. Bir günün saatı fərqlidirsə, onu aşağıdan əlavə edin.
                </div>
              )}
              {DAYS.map((label, i) => {
                const key = String(BIT[i])
                const hours = form.dayHours[key]
                if (!hours) return null
                return (
                  <div key={key} style={{ display: 'flex', gap: 8, alignItems: 'center', marginBottom: 6, flexWrap: 'wrap' }}>
                    <span className="chip active" style={{ cursor: 'default', minWidth: 46, textAlign: 'center' }}>{label}</span>
                    <input
                      className="inp" type="time" style={{ width: 120 }} value={hours.start}
                      onChange={(e) => setDayHours(key, { ...hours, start: e.target.value })}
                    />
                    <span className="muted">–</span>
                    <input
                      className="inp" type="time" style={{ width: 120 }} value={hours.end}
                      onChange={(e) => setDayHours(key, { ...hours, end: e.target.value })}
                    />
                    <button type="button" className="btn btn-sm" onClick={() => setDayHours(key, null)}>Sil</button>
                  </div>
                )
              })}
              {/* Only offers days the shift actually works — an override on a day off means nothing. */}
              <select
                className="inp"
                style={{ width: 'auto', minWidth: 190, padding: '6px 10px', fontSize: 12, marginTop: 4 }}
                value=""
                onChange={(e) => {
                  if (!e.target.value) return
                  setDayHours(e.target.value, { start: form.shiftStart, end: form.shiftEnd })
                }}
              >
                <option value="">+ Fərqli saatlı gün əlavə et</option>
                {DAYS.map((label, i) => {
                  const key = String(BIT[i])
                  const works = (form.workDaysMask & (1 << BIT[i])) !== 0
                  if (!works || form.dayHours[key]) return null
                  return <option key={key} value={key}>{label}</option>
                })}
              </select>
            </div>
          )}

          <WorkCyclePicker value={form.cycle} onChange={(cycle) => setForm((f) => ({ ...f, cycle }))} />

          {err && <div className="fb fb-err" style={{ marginTop: 10 }}><IconX /><span>{err}</span></div>}

          <div style={{ display: 'flex', gap: 8, marginTop: 14 }}>
            <button className="btn btn-primary" disabled={saving} onClick={() => void save()}>
              <IconCheck />{saving ? 'Yadda saxlanır…' : 'Yadda saxla'}
            </button>
            <button className="btn" onClick={() => setShowForm(false)}>Ləğv et</button>
          </div>
        </div>
      )}

      {loading && <div className="card card-pad muted">Yüklənir…</div>}
      {!loading && rows.length === 0 && (
        <div className="card card-pad muted" style={{ textAlign: 'center' }}>
          Növbə yoxdur. «Yeni növbə» ilə başlayın.
        </div>
      )}

      {!loading && rows.length > 0 && (
        <div className="tbl-wrap tbl-cards">
          <table>
            <thead>
              <tr>
                <th>Ad</th><th>Saatlar</th><th>Günlər</th><th>İşçi</th><th></th>
              </tr>
            </thead>
            <tbody>
              {rows.map((s) => {
                const people = staffOf(s.id)
                const isOpen = expanded === s.id
                // Group this shift's people by their branch — the "ərazi üzrə" cut of who is on it.
                const byArea = new Map<string, typeof people>()
                for (const e of people) {
                  const key = e.locationName ?? '—'
                  const arr = byArea.get(key)
                  if (arr) arr.push(e)
                  else byArea.set(key, [e])
                }
                const areaGroups = Array.from(byArea.entries()).sort((a, b) => a[0].localeCompare(b[0], 'az'))
                return (
                  <Fragment key={s.id}>
                    <tr onClick={() => setExpanded(isOpen ? null : s.id)} style={{ cursor: 'pointer' }}>
                      <td data-label="Ad">
                        <b>{s.name}</b>
                        <div style={{ fontSize: 11, color: 'var(--c400)', marginTop: 2 }}>
                          {s.locationName ?? 'bütün şirkət'}
                        </div>
                      </td>
                      <td data-label="Saatlar">
                        {s.shiftStart}–{s.shiftEnd}{s.isOvernight ? ' 🌙' : ''}
                      </td>
                      <td data-label="Günlər">{daysLabel(s)}</td>
                      <td data-label="İşçi">
                        <span style={{ fontWeight: 700 }}>{people.length}</span>
                        {people.length > 0 && (
                          <span style={{ marginLeft: 6, color: 'var(--c500)', fontSize: 12 }}>{isOpen ? '▲' : '▼'}</span>
                        )}
                      </td>
                      <td>
                        <div style={{ display: 'flex', gap: 6, justifyContent: 'flex-end' }} onClick={(e) => e.stopPropagation()}>
                          <button className="btn btn-sm" onClick={() => startEdit(s)}>Redaktə</button>
                          <button className="btn btn-sm btn-danger" onClick={() => void remove(s)}><IconTrash /></button>
                        </div>
                      </td>
                    </tr>
                    {isOpen && (
                      <tr>
                        <td colSpan={5} style={{ background: 'var(--c50)' }}>
                          {people.length === 0 ? (
                            <span className="muted" style={{ fontSize: 13 }}>
                              Bu növbədə {filterLoc ? 'bu ərazidə ' : ''}işçi yoxdur.
                            </span>
                          ) : (
                            <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
                              {areaGroups.map(([loc, list]) => (
                                <div key={loc}>
                                  <div style={{ fontSize: 12, fontWeight: 700, color: 'var(--c500)', marginBottom: 6 }}>
                                    📍 {loc} · {list.length}
                                  </div>
                                  <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8 }}>
                                    {list.map((e) => (
                                      <span key={e.id} style={{ display: 'inline-block', padding: '4px 10px', borderRadius: 999, background: 'var(--white)', border: '1px solid var(--c200)', fontSize: 13 }}>
                                        <EmployeeLink id={e.id} name={e.fullName} />
                                      </span>
                                    ))}
                                  </div>
                                </div>
                              ))}
                            </div>
                          )}
                        </td>
                      </tr>
                    )}
                  </Fragment>
                )
              })}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}
