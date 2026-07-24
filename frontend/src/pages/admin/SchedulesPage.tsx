import { useEffect, useState } from 'react'
import {
  createSchedule,
  deleteSchedule,
  getEmployees,
  getSchedules,
  updateSchedule,
  type AdminEmployee,
  type Schedule,
  type ScheduleInput,
} from '../../api/admin'
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
 */

const DAYS = ['B.e', 'Ç.a', 'Ç', 'C.a', 'C', 'Ş', 'B'] // Monday-first for reading; bit index below
const BIT = [1, 2, 3, 4, 5, 6, 0] // .NET DayOfWeek: Sunday = 0

const ERRORS: Record<string, string> = {
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
  shiftStart: string
  shiftEnd: string
  lateThresholdMinutes: string
  workDaysMask: number
  cycle: WorkCycleValue
}

const EMPTY: FormState = {
  name: '',
  shiftStart: '09:00',
  shiftEnd: '18:00',
  lateThresholdMinutes: '15',
  workDaysMask: 126,
  cycle: NO_CYCLE,
}

export function SchedulesPage() {
  const [rows, setRows] = useState<Schedule[]>([])
  const [employees, setEmployees] = useState<AdminEmployee[]>([])
  const [loading, setLoading] = useState(true)
  const [showForm, setShowForm] = useState(false)
  const [editingId, setEditingId] = useState<string | null>(null)
  const [form, setForm] = useState<FormState>(EMPTY)
  const [saving, setSaving] = useState(false)
  const [err, setErr] = useState<string | null>(null)
  const [ok, setOk] = useState<string | null>(null)

  async function refresh() {
    setLoading(true)
    const [s, e] = await Promise.all([getSchedules(), getEmployees()])
    if (s.status === 200 && Array.isArray(s.data)) setRows(s.data)
    if (e.status === 200 && Array.isArray(e.data)) setEmployees(e.data)
    setLoading(false)
  }

  useEffect(() => { void refresh() }, [])

  /** How many people are on each shift — the number that decides whether it can be deleted, and the
   *  one an admin needs before editing hours that will move somebody's pay. */
  const usedBy = (id: string) => employees.filter((e) => e.scheduleId === id && e.isActive).length

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
      shiftStart: s.shiftStart,
      shiftEnd: s.shiftEnd,
      lateThresholdMinutes: String(s.lateThresholdMinutes),
      workDaysMask: s.workDaysMask,
      cycle: s.workCycleDays
        ? { days: s.workCycleDays, onDays: s.workCycleOnDays ?? 1, anchor: s.workCycleAnchor ?? '' }
        : NO_CYCLE,
    })
    setErr(null); setOk(null)
    setShowForm(true)
  }

  function toggleDay(bit: number) {
    setForm((f) => ({ ...f, workDaysMask: f.workDaysMask ^ (1 << bit) }))
  }

  async function save() {
    if (!form.name.trim()) { setErr('Ad tələb olunur'); return }
    setSaving(true); setErr(null)
    const payload: ScheduleInput = {
      name: form.name.trim(),
      shiftStart: form.shiftStart,
      shiftEnd: form.shiftEnd,
      lateThresholdMinutes: Number(form.lateThresholdMinutes) || 0,
      workDaysMask: form.workDaysMask,
      workCycleDays: form.cycle.days,
      workCycleOnDays: form.cycle.days ? form.cycle.onDays : null,
      workCycleAnchor: form.cycle.days ? form.cycle.anchor || null : null,
    }
    const res = editingId ? await updateSchedule(editingId, payload) : await createSchedule(payload)
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
    const { status, data } = await deleteSchedule(s.id)
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
        </p>
        <button className="btn btn-primary" onClick={startCreate}>+ Yeni növbə</button>
      </div>

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
              {rows.map((s) => (
                <tr key={s.id}>
                  <td data-label="Ad"><b>{s.name}</b></td>
                  <td data-label="Saatlar">
                    {s.shiftStart}–{s.shiftEnd}{s.isOvernight ? ' 🌙' : ''}
                  </td>
                  <td data-label="Günlər">{daysLabel(s)}</td>
                  <td data-label="İşçi">{usedBy(s.id)}</td>
                  <td>
                    <div style={{ display: 'flex', gap: 6, justifyContent: 'flex-end' }}>
                      <button className="btn btn-sm" onClick={() => startEdit(s)}>Redaktə</button>
                      <button className="btn btn-sm btn-danger" onClick={() => void remove(s)}><IconTrash /></button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}
