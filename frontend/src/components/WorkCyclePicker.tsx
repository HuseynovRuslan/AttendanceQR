import { useMemo } from 'react'

/**
 * Picking an employee's rotation ("növbə").
 *
 * The underlying model is a cycle length, how many of its first days are worked, and an anchor date —
 * three numbers that describe every rotation but that nobody would ever want to fill in. So the form
 * offers the four rotations that actually exist in these companies by name, and only the unusual case
 * ("Fərdi") exposes the numbers.
 *
 * The calendar strip below the picker is the point of the whole component. An anchor date is
 * impossible to reason about in the abstract — "is the 24th the day they work or the day they rest?"
 * — and getting it one day out silently marks the person absent on every day they actually worked,
 * which comes off their salary. So the next two weeks are drawn as they will be recorded, and the
 * manager confirms it against what they know rather than trusting the arithmetic.
 */

export interface WorkCycleValue {
  /** Cycle length in days; null = no rotation (the branch's weekly calendar decides). */
  days: number | null
  /** How many days at the start of each cycle are worked. */
  onDays: number
  /** A date the employee IS working — day 0 of the cycle. "yyyy-MM-dd". */
  anchor: string
}

export const NO_CYCLE: WorkCycleValue = { days: null, onDays: 1, anchor: '' }

/** The rotations these companies actually run, named the way a manager says them out loud. */
const PRESETS: { label: string; hint: string; days: number | null; onDays: number }[] = [
  { label: 'Həftəlik (adi)', hint: 'Filialın iş günləri — bazar istirahət', days: null, onDays: 1 },
  { label: 'Bir gündən bir', hint: '1 gün iş, 1 gün istirahət', days: 2, onDays: 1 },
  { label: 'Sutka', hint: '1 gün iş, 2 gün istirahət', days: 3, onDays: 1 },
  { label: '2 iş / 2 istirahət', hint: '2 gün iş, 2 gün istirahət', days: 4, onDays: 2 },
]

const WEEKDAYS = ['B', 'B.e', 'Ç.a', 'Ç', 'C.a', 'C', 'Ş']

/** Mirrors AttendanceCalculator.IsScheduledWorkingDay — including the floored modulo, so dates
 *  before the anchor land on the right half of the cycle instead of reading as rest days. */
export function isWorkingDay(value: WorkCycleValue, date: Date, weeklyWorksOn?: (d: Date) => boolean): boolean {
  if (!value.days || value.days < 2 || !value.anchor) {
    return weeklyWorksOn ? weeklyWorksOn(date) : date.getDay() !== 0
  }
  const anchor = new Date(value.anchor + 'T00:00:00')
  const dayMs = 24 * 60 * 60 * 1000
  const offset = Math.round((startOfDay(date).getTime() - startOfDay(anchor).getTime()) / dayMs)
  const inCycle = ((offset % value.days) + value.days) % value.days
  return inCycle < Math.min(Math.max(value.onDays, 1), value.days - 1)
}

const startOfDay = (d: Date) => new Date(d.getFullYear(), d.getMonth(), d.getDate())
const iso = (d: Date) =>
  `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`

export function WorkCyclePicker({
  value,
  onChange,
}: {
  value: WorkCycleValue
  onChange: (v: WorkCycleValue) => void
}) {
  // Which preset the current value matches; -1 means the numbers were typed by hand ("Fərdi").
  const presetIndex = useMemo(() => {
    const i = PRESETS.findIndex((p) => p.days === value.days && (p.days === null || p.onDays === value.onDays))
    return i
  }, [value.days, value.onDays])

  const custom = presetIndex === -1

  function pickPreset(index: number) {
    if (index === -1) {
      // Entering "Fərdi" from a preset keeps whatever is already there so the strip doesn't blank out.
      onChange({ ...value, days: value.days ?? 5, onDays: value.onDays || 2, anchor: value.anchor || iso(new Date()) })
      return
    }
    const p = PRESETS[index]
    onChange({
      days: p.days,
      onDays: p.onDays,
      // A rotation needs an anchor; default to today so the strip is meaningful immediately, and the
      // manager only has to move it if today happens to be a rest day.
      anchor: p.days === null ? '' : value.anchor || iso(new Date()),
    })
  }

  // 14 days from today — long enough to show two full turns of every preset.
  const preview = useMemo(() => {
    if (!value.days) return []
    const today = startOfDay(new Date())
    return Array.from({ length: 14 }, (_, i) => {
      const d = new Date(today.getFullYear(), today.getMonth(), today.getDate() + i)
      return { date: d, working: isWorkingDay(value, d) }
    })
  }, [value.days, value.onDays, value.anchor])

  const workDays = preview.filter((p) => p.working).length

  return (
    <div className="wc">
      <label className="form-label">Növbə</label>
      <select
        className="inp"
        value={custom ? 'custom' : String(presetIndex)}
        onChange={(e) => pickPreset(e.target.value === 'custom' ? -1 : Number(e.target.value))}
      >
        {PRESETS.map((p, i) => (
          <option key={p.label} value={i}>
            {p.label} — {p.hint}
          </option>
        ))}
        <option value="custom">Fərdi…</option>
      </select>

      {custom && (
        <div className="form-row cols2 wc-custom">
          <div>
            <label className="form-label">Neçə gün iş</label>
            <input
              className="inp"
              type="number"
              min={1}
              max={27}
              value={value.onDays}
              onChange={(e) => onChange({ ...value, onDays: Number(e.target.value) || 1 })}
            />
          </div>
          <div>
            <label className="form-label">Neçə gün istirahət</label>
            <input
              className="inp"
              type="number"
              min={1}
              max={27}
              value={Math.max((value.days ?? 2) - value.onDays, 1)}
              onChange={(e) =>
                onChange({ ...value, days: value.onDays + (Number(e.target.value) || 1) })
              }
            />
          </div>
        </div>
      )}

      {value.days !== null && (
        <>
          <div className="wc-anchor">
            <label className="form-label">İşlədiyi bir gün</label>
            <input
              className="inp"
              type="date"
              value={value.anchor}
              onChange={(e) => onChange({ ...value, anchor: e.target.value })}
            />
            <div className="wc-note">
              Bu tarixdə işdə olduğunu bildiyiniz bir gün seçin — qalan günləri sistem özü hesablayır.
            </div>
          </div>

          {value.anchor ? (
            <div className="wc-preview">
              <div className="wc-preview-head">
                <span>Növbəti 14 gün</span>
                <span className="wc-count">{workDays} iş günü</span>
              </div>
              <div className="wc-strip">
                {preview.map(({ date, working }) => (
                  <div key={iso(date)} className={working ? 'wc-day on' : 'wc-day off'}>
                    <span className="wc-dow">{WEEKDAYS[date.getDay()]}</span>
                    <span className="wc-num">{date.getDate()}</span>
                  </div>
                ))}
              </div>
              <div className="wc-legend">
                <span><i className="sw on" /> iş</span>
                <span><i className="sw off" /> istirahət</span>
                <span className="wc-check">Doğrudurmu? Yanlışdırsa tarixi bir gün dəyişin.</span>
              </div>
            </div>
          ) : (
            <div className="fb fb-err wc-missing">Növbə üçün bir tarix seçilməlidir.</div>
          )}
        </>
      )}
    </div>
  )
}
