import { useEffect, useMemo, useRef, useState } from 'react'
import { bulkSchedule } from '../../../api/admin'
import type { PlanApply, PlanEmployee, PlanShift, PlanStep } from '../../../api/shiftPlan'
import { IconX } from '../../../components/icons'
import {
  bandOf, cellsOf, daysBetween, maskOf, needsSecondShift, rotationSteps, ROTATIONS, WEEKDAYS_SHORT, WORKWEEK,
} from './planModel'

/**
 * «Yeni plan» — who, which shift, from when. That is the whole first screen, because it is the whole job most of
 * the time. Repetition is one switch away and only then shows its options: the weekdays it applies to, or a ready
 * rotation (2/2, 2 day / 2 night / 2 off) — the details a planner only needs once they have asked for them.
 */

export interface DrawerSeed {
  employeeIds: string[]
  from: string
  to: string
}

type Repeat = 'none' | 'weekdays' | 'rotation'
const MONDAY_FIRST = [1, 2, 3, 4, 5, 6, 0]

export function PlanDrawer(props: {
  seed: DrawerSeed
  employees: PlanEmployee[]
  shifts: PlanShift[]
  today: string
  mayStanding: boolean
  saving: boolean
  onClose: () => void
  onApply: (body: PlanApply) => Promise<boolean>
  onStanding: () => void
  onMessage: (ok: boolean, text: string) => void
}) {
  const { seed, employees, shifts, today, mayStanding, saving, onClose, onApply, onStanding, onMessage } = props
  const [picked, setPicked] = useState<Set<string>>(() => new Set(seed.employeeIds))
  const [who, setWho] = useState('')
  const [shift, setShift] = useState<string>(shifts[0]?.id ?? '')
  const [from, setFrom] = useState(seed.from)
  const [to, setTo] = useState(seed.to)
  const [standing, setStanding] = useState(false)
  const [repeat, setRepeat] = useState<Repeat>('none')
  const [weekdays, setWeekdays] = useState<number[]>([1, 2, 3, 4, 5])
  const [restOthers, setRestOthers] = useState(true)
  const [rotation, setRotation] = useState(ROTATIONS[1].id)
  const [second, setSecond] = useState<string>(shifts.find((s) => bandOf(s.start, s.overnight) === 'night')?.id ?? shifts[1]?.id ?? shifts[0]?.id ?? '')
  const [note, setNote] = useState('')
  const [busy, setBusy] = useState(false)
  const panel = useRef<HTMLDivElement>(null)

  useEffect(() => {
    panel.current?.querySelector<HTMLInputElement>('input, select, button')?.focus()
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose() }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  const listed = useMemo(() => {
    const q = who.trim().toLocaleLowerCase('az')
    const all = [...employees].sort((a, b) => a.name.localeCompare(b.name, 'az'))
    return q ? all.filter((e) => `${e.name} ${e.position ?? ''}`.toLocaleLowerCase('az').includes(q)) : all
  }, [employees, who])

  const preset = ROTATIONS.find((r) => r.id === rotation)!
  const span = daysBetween(from, to) + 1
  const mask = repeat === 'weekdays' ? maskOf(weekdays) : null
  const cells = picked.size > 0 && span > 0 ? cellsOf(picked.size, from, to, mask, restOthers) : 0

  const problem =
    picked.size === 0 ? 'İşçi seçin.'
      : !shift ? 'Növbə seçin.'
        : standing ? null
          : !from || !to ? 'Tarixləri seçin.'
            : span < 1 ? 'Bitmə tarixi başlanğıcdan əvvəl ola bilməz.'
              : span > 92 ? 'Bir planda ən çox 92 gün ola bilər.'
                : repeat === 'weekdays' && weekdays.length === 0 ? 'Ən azı bir həftə günü seçin.'
                  : null

  function steps(): PlanStep[] {
    if (repeat === 'rotation') return rotationSteps(preset, shift, needsSecondShift(preset.steps) ? second : null)
    return [shift]
  }

  async function save() {
    if (problem) return
    const employeeIds = [...picked]
    if (standing) {
      if (shift === 'rest') return
      setBusy(true)
      const { status, data } = await bulkSchedule(employeeIds, shift)
      setBusy(false)
      if (status === 200 && data && 'changed' in data) {
        onMessage(true, `${data.changed} işçinin daimi növbəsi dəyişdi${data.skipped ? `, ${data.skipped} işçi bu növbəyə uyğun deyil` : ''}.`)
        onStanding()
      } else onMessage(false, 'Daimi növbə dəyişmədi. Yenidən cəhd edin.')
      return
    }
    await onApply({
      employeeIds,
      steps: steps(),
      from,
      to,
      weekdays: repeat === 'weekdays' ? maskOf(weekdays) : null,
      restOnOtherDays: restOthers,
      note: note.trim() || null,
    })
  }

  function toggleDay(d: number) {
    setWeekdays((prev) => (prev.includes(d) ? prev.filter((x) => x !== d) : [...prev, d]))
  }

  const allListed = listed.length > 0 && listed.every((e) => picked.has(e.id))

  return (
    <div className="sp-overlay" role="presentation" onMouseDown={(e) => { if (e.target === e.currentTarget) onClose() }}>
      <div ref={panel} className="sp-drawer" role="dialog" aria-modal="true" aria-labelledby="sp-drawer-title">
        <header className="sp-drawerhead">
          <h2 id="sp-drawer-title">Yeni plan</h2>
          <button type="button" className="btn btn-sm btn-ghost" aria-label="Bağla" onClick={onClose}><IconX /></button>
        </header>

        <div className="sp-drawerbody">
          <section aria-labelledby="sp-who">
            <div className="sp-sectionhead">
              <h3 id="sp-who">İşçilər <span className="sp-count">{picked.size}</span></h3>
              <button type="button" className="btn-link" onClick={() => setPicked(allListed ? new Set() : new Set([...picked, ...listed.map((e) => e.id)]))}>
                {allListed ? 'Heç biri' : who ? 'Tapılanların hamısı' : 'Hamısı'}
              </button>
            </div>
            <input className="sp-input" type="search" placeholder="Ad və ya vəzifə" value={who} onChange={(e) => setWho(e.target.value)} aria-label="İşçi axtar" />
            <ul className="sp-picklist">
              {listed.map((e) => (
                <li key={e.id}>
                  <label>
                    <input type="checkbox" checked={picked.has(e.id)} onChange={() => setPicked((prev) => {
                      const next = new Set(prev)
                      if (next.has(e.id)) next.delete(e.id)
                      else next.add(e.id)
                      return next
                    })} />
                    <span className="sp-name">{e.name}</span>
                    {e.position && <span className="sp-pos">{e.position}</span>}
                  </label>
                </li>
              ))}
            </ul>
          </section>

          <section aria-labelledby="sp-which">
            <h3 id="sp-which">Növbə</h3>
            <div className="sp-shiftpick" role="radiogroup" aria-labelledby="sp-which">
              {shifts.map((s) => (
                <button key={s.id} type="button" role="radio" aria-checked={shift === s.id} className="sp-shiftopt" onClick={() => setShift(s.id)}>
                  <span className={`sp-band sp-${bandOf(s.start, s.overnight)}`} aria-hidden />
                  <span className="sp-menuname">{s.name}</span>
                  <span className="sp-menutime">{s.start}–{s.end}</span>
                </button>
              ))}
              {!standing && repeat !== 'rotation' && (
                <button type="button" role="radio" aria-checked={shift === 'rest'} className="sp-shiftopt" onClick={() => setShift('rest')}>
                  <span className="sp-band sp-restband" aria-hidden />
                  <span className="sp-menuname">İstirahət</span>
                </button>
              )}
            </div>
          </section>

          <section aria-labelledby="sp-when">
            <h3 id="sp-when">Tarix</h3>
            <div className="sp-dates">
              <label className="sp-datefield">
                <span>Başlanğıc</span>
                <input className="sp-input" type="date" value={from} disabled={standing} onChange={(e) => { setFrom(e.target.value); if (e.target.value > to) setTo(e.target.value) }} />
              </label>
              <label className="sp-datefield">
                <span>Bitmə</span>
                <input className="sp-input" type="date" value={to} min={from} disabled={standing} onChange={(e) => setTo(e.target.value)} />
              </label>
            </div>
            {from < today && !standing && <p className="sp-note sp-note-warn">Keçmiş günlər də daxildir — yadda saxlamazdan əvvəl təsdiq soruşulacaq.</p>}
            {mayStanding && (
              <label className="sp-switch">
                <input type="checkbox" checked={standing} onChange={(e) => { setStanding(e.target.checked); if (e.target.checked) { setRepeat('none'); if (shift === 'rest') setShift(shifts[0]?.id ?? '') } }} />
                <span>Müddətsiz — işçinin daimi növbəsi olsun</span>
              </label>
            )}
            {standing && <p className="sp-note">Daimi növbə bu gündən etibarən qüvvədədir. Ayrı-ayrı günlərə yazılmış planlar olduğu kimi qalır.</p>}
          </section>

          {!standing && (
            <section aria-labelledby="sp-repeat">
              <h3 id="sp-repeat">Təkrarlanma</h3>
              <div className="seg" role="group" aria-labelledby="sp-repeat">
                {([['none', 'Hər gün eyni'], ['weekdays', 'Həftənin günləri'], ['rotation', 'Növbəli']] as const).map(([v, label]) => (
                  <button key={v} type="button" className="seg-btn" aria-pressed={repeat === v}
                    onClick={() => { setRepeat(v); if (v === 'rotation' && shift === 'rest') setShift(shifts[0]?.id ?? '') }}>{label}</button>
                ))}
              </div>

              {repeat === 'weekdays' && (
                <div className="sp-reveal">
                  <div className="sp-weekdays" role="group" aria-label="Həftənin günləri">
                    {MONDAY_FIRST.map((d) => (
                      <button key={d} type="button" className="sp-wdbtn" aria-pressed={weekdays.includes(d)} onClick={() => toggleDay(d)}>{WEEKDAYS_SHORT[d]}</button>
                    ))}
                  </div>
                  <div className="sp-quick">
                    <button type="button" className="btn-link" onClick={() => setWeekdays([1, 2, 3, 4, 5])}>B.e – C</button>
                    <button type="button" className="btn-link" onClick={() => setWeekdays([1, 2, 3, 4, 5, 6])}>B.e – Ş</button>
                  </div>
                  <label className="sp-switch">
                    <input type="checkbox" checked={restOthers} onChange={(e) => setRestOthers(e.target.checked)} />
                    <span>Qalan günlər istirahət olsun</span>
                  </label>
                  {maskOf(weekdays) === WORKWEEK && restOthers && <p className="sp-note">Şənbə və bazar istirahət yazılacaq.</p>}
                </div>
              )}

              {repeat === 'rotation' && (
                <div className="sp-reveal">
                  <label className="sp-datefield">
                    <span>Qayda</span>
                    <select className="sp-select" value={rotation} onChange={(e) => setRotation(e.target.value)}>
                      {ROTATIONS.map((r) => <option key={r.id} value={r.id}>{r.label}</option>)}
                    </select>
                  </label>
                  {needsSecondShift(preset.steps) && (
                    <label className="sp-datefield">
                      <span>İkinci növbə</span>
                      <select className="sp-select" value={second} onChange={(e) => setSecond(e.target.value)}>
                        {shifts.map((s) => <option key={s.id} value={s.id}>{s.name} · {s.start}–{s.end}</option>)}
                      </select>
                    </label>
                  )}
                  <ol className="sp-cycle" aria-label="Dövrə">
                    {rotationSteps(preset, shift, second).map((s, i) => {
                      const sh = shifts.find((x) => x.id === s)
                      return (
                        <li key={i} className={`sp-chip ${sh ? `sp-${bandOf(sh.start, sh.overnight)} sp-planned` : 'sp-rest'}`}>
                          {sh ? sh.name : 'İstirahət'}
                        </li>
                      )
                    })}
                  </ol>
                  <p className="sp-note">Dövrə başlanğıc tarixindən sayılır və bitmə tarixinədək təkrarlanır.</p>
                </div>
              )}
            </section>
          )}

          {!standing && (
            <label className="sp-datefield">
              <span>Qeyd <span className="muted">(istəyə bağlı)</span></span>
              <input className="sp-input" value={note} maxLength={200} placeholder="məs. bayram həftəsi" onChange={(e) => setNote(e.target.value)} />
            </label>
          )}
        </div>

        <footer className="sp-drawerfoot">
          <p className="sp-summary" aria-live="polite">
            {problem ?? (standing
              ? `${picked.size} işçinin daimi növbəsi dəyişəcək.`
              : `${picked.size} işçi × ${span} gün — cəmi ${cells} plan günü yazılacaq.`)}
          </p>
          <div className="sp-dialogbtns">
            <button type="button" className="btn" onClick={onClose}>Ləğv et</button>
            <button type="button" className="btn btn-primary" disabled={Boolean(problem) || saving || busy} onClick={() => void save()}>
              {saving || busy ? 'Yadda saxlanılır…' : 'Planı yadda saxla'}
            </button>
          </div>
        </footer>
      </div>
    </div>
  )
}
