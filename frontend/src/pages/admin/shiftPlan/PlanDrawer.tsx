import { useEffect, useMemo, useRef, useState } from 'react'
import { applyShiftPlan, type PlanApply, type PlanEmployee, type PlanPreview, type PlanShift, type PlanStep } from '../../../api/shiftPlan'
import { IconX } from '../../../components/icons'
import { PreviewSummary } from './PreviewSummary'
import {
  addDays, bandOf, cellsOf, daysBetween, maskOf, needsSecondShift, rotationSteps, ROTATIONS, weekday, WEEKDAYS_SHORT, WORKWEEK,
} from './planModel'

/**
 * «Yeni plan» — who, which shift, from when. That is the whole first screen, because it is the whole job most of
 * the time. Repetition is one switch away and only then shows its options. Nothing is written from this screen:
 * «Davam et» asks the server what the plan would change — new days, days that replace a plan already there, which
 * shift becomes which — and the plan is saved only from that second screen, after it has been read.
 */

export interface DrawerSeed {
  employeeIds: string[]
  from: string
  to: string
}

type Repeat = 'none' | 'weekdays' | 'rotation'
const MONDAY_FIRST = [1, 2, 3, 4, 5, 6, 0]

const ERRORS: Record<string, string> = {
  OutOfScope: 'Bu işçilərin növbəsini dəyişmək səlahiyyətiniz yoxdur.',
  ScheduleBelongsToOtherBranch: 'Seçilmiş növbə başqa filiala aiddir.',
  StandingTooFarBack: 'Bitmə tarixi olmayan plan ən çox 31 gün əvvəldən başlaya bilər.',
  RangeTooLong: 'Bitmə tarixi olan plan ən çox 92 gün ola bilər. Daha uzun müddət üçün «Bitmə tarixi yoxdur» seçin.',
}

export function PlanDrawer(props: {
  seed: DrawerSeed
  employees: PlanEmployee[]
  shifts: PlanShift[]
  today: string
  locationId: string | null
  saving: boolean
  onClose: () => void
  onApply: (body: PlanApply) => Promise<boolean>
}) {
  const { seed, employees, shifts, today, locationId, saving, onClose, onApply } = props
  const [picked, setPicked] = useState<Set<string>>(() => new Set(seed.employeeIds))
  const [who, setWho] = useState('')
  const [shift, setShift] = useState<string>(shifts[0]?.id ?? '')
  const [from, setFrom] = useState(seed.from)
  const [to, setTo] = useState(seed.to)
  const [open, setOpen] = useState(false)
  const [repeat, setRepeat] = useState<Repeat>('none')
  const [weekdays, setWeekdays] = useState<number[]>([1, 2, 3, 4, 5])
  const [restOthers, setRestOthers] = useState(true)
  const [rotation, setRotation] = useState(ROTATIONS[1].id)
  const [second, setSecond] = useState<string>(shifts.find((s) => bandOf(s.start, s.overnight) === 'night')?.id ?? shifts[1]?.id ?? shifts[0]?.id ?? '')
  const [note, setNote] = useState('')
  const [preview, setPreview] = useState<PlanPreview | null>(null)
  const [checking, setChecking] = useState(false)
  const [problemFromServer, setProblemFromServer] = useState<string | null>(null)
  const panel = useRef<HTMLDivElement>(null)

  useEffect(() => {
    panel.current?.querySelector<HTMLInputElement>('input, select, button')?.focus()
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose() }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  // Any change to the plan sends it back to the first screen: a preview is only true for what it was asked about.
  useEffect(() => { setPreview(null); setProblemFromServer(null) }, [picked, shift, from, to, open, repeat, weekdays, restOthers, rotation, second])

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
        : !from ? 'Başlanğıc tarixini seçin.'
          : open && shift === 'rest' && repeat !== 'rotation' ? 'Bitmə tarixi olmayan plan istirahətdən ibarət ola bilməz.'
            : !open && (!to || span < 1) ? 'Bitmə tarixi başlanğıcdan əvvəl ola bilməz.'
              : !open && span > 92 ? 'Bitmə tarixi olan plan ən çox 92 gün ola bilər — daha uzun üçün «Bitmə tarixi yoxdur» seçin.'
                : repeat === 'weekdays' && weekdays.length === 0 ? 'Ən azı bir həftə günü seçin.'
                  : null

  /** The plan as the server takes it. With no end, a weekly pattern is a seven-day rotation from the first day. */
  function body(): PlanApply {
    const employeeIds = [...picked]
    if (repeat === 'rotation') {
      const steps = rotationSteps(preset, shift, needsSecondShift(preset.steps) ? second : null)
      return open ? { employeeIds, steps, from, standing: true } : { employeeIds, steps, from, to, note: note.trim() || null }
    }
    if (repeat === 'weekdays') {
      if (open) {
        const steps: PlanStep[] = Array.from({ length: 7 }, (_, i) => (weekdays.includes(weekday(addDays(from, i))) ? shift : 'rest'))
        return { employeeIds, steps, from, standing: true }
      }
      return { employeeIds, steps: [shift], from, to, weekdays: maskOf(weekdays), restOnOtherDays: restOthers, note: note.trim() || null }
    }
    return open ? { employeeIds, steps: [shift], from, standing: true } : { employeeIds, steps: [shift], from, to, note: note.trim() || null }
  }

  async function check() {
    if (problem) return
    setChecking(true)
    const { status, data } = await applyShiftPlan({ ...body(), locationId, preview: true })
    setChecking(false)
    if (status === 200 && data && 'preview' in data) setPreview(data)
    else setProblemFromServer(data && 'error' in data ? ERRORS[data.error] ?? 'Plan yoxlanmadı. Yenidən cəhd edin.' : 'Plan yoxlanmadı. Yenidən cəhd edin.')
  }

  function toggleDay(d: number) {
    setWeekdays((prev) => (prev.includes(d) ? prev.filter((x) => x !== d) : [...prev, d]))
  }

  const allListed = listed.length > 0 && listed.every((e) => picked.has(e.id))
  const nothing = preview && !preview.standing && (preview.newDays ?? 0) + (preview.replacedDays ?? 0) + (preview.clearedDays ?? 0) === 0

  return (
    <div className="sp-overlay" role="presentation" onMouseDown={(e) => { if (e.target === e.currentTarget) onClose() }}>
      <div ref={panel} className="sp-drawer" role="dialog" aria-modal="true" aria-labelledby="sp-drawer-title">
        <header className="sp-drawerhead">
          <h2 id="sp-drawer-title">{preview ? 'Planı yoxlayın' : 'Yeni plan'}</h2>
          <button type="button" className="btn btn-sm btn-ghost" aria-label="Bağla" onClick={onClose}><IconX /></button>
        </header>

        {preview ? (
          <div className="sp-drawerbody">
            <PreviewSummary preview={preview} names={employees.filter((e) => picked.has(e.id)).map((e) => e.name)} />
            <p className="sp-note">Yadda saxladıqdan sonra da dəyişikliyi «Tarixçə»dən 30 gün ərzində geri ala bilərsiniz.</p>
          </div>
        ) : (
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
                {repeat !== 'rotation' && !open && (
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
                  <input className="sp-input" type="date" value={from} onChange={(e) => { setFrom(e.target.value); if (e.target.value > to) setTo(e.target.value) }} />
                </label>
                <label className="sp-datefield">
                  <span>Bitmə</span>
                  <input className="sp-input" type="date" value={open ? '' : to} min={from} disabled={open} onChange={(e) => setTo(e.target.value)} />
                </label>
              </div>
              <label className="sp-switch">
                <input type="checkbox" checked={open} onChange={(e) => { setOpen(e.target.checked); if (e.target.checked && shift === 'rest') setShift(shifts[0]?.id ?? '') }} />
                <span>Bitmə tarixi yoxdur — işçinin daimi növbəsi olsun</span>
              </label>
              {open && <p className="sp-note">Başlanğıcdan əvvəlki günlər köhnə növbə ilə qalır. Ayrı günlərə yazılmış planlar dəyişmir.</p>}
              {from < today && <p className="sp-note sp-note-warn">Keçmiş günlər də daxildir — yadda saxlamazdan əvvəl təsdiq soruşulacaq.</p>}
            </section>

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
                  {!open && (
                    <label className="sp-switch">
                      <input type="checkbox" checked={restOthers} onChange={(e) => setRestOthers(e.target.checked)} />
                      <span>Qalan günlər istirahət olsun</span>
                    </label>
                  )}
                  {(open || restOthers) && maskOf(weekdays) === WORKWEEK && <p className="sp-note">Şənbə və bazar istirahət olacaq.</p>}
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
                  <p className="sp-note">Dövrə başlanğıc tarixindən sayılır{open ? ' və dayanmadan təkrarlanır.' : ' və bitmə tarixinədək təkrarlanır.'}</p>
                </div>
              )}
            </section>

            {!open && (
              <label className="sp-datefield">
                <span>Qeyd <span className="muted">(istəyə bağlı)</span></span>
                <input className="sp-input" value={note} maxLength={200} placeholder="məs. bayram həftəsi" onChange={(e) => setNote(e.target.value)} />
              </label>
            )}
          </div>
        )}

        <footer className="sp-drawerfoot">
          {!preview && (
            <p className="sp-summary" aria-live="polite">
              {problemFromServer ?? problem ?? (open
                ? `${picked.size} işçi — ${from.slice(8, 10)}.${from.slice(5, 7)}-dən etibarən, bitmə tarixi yoxdur.`
                : `${picked.size} işçi × ${span} gün — cəmi ${cells} plan günü.`)}
            </p>
          )}
          <div className="sp-dialogbtns">
            {preview ? (
              <>
                <button type="button" className="btn" onClick={() => setPreview(null)}>Geri</button>
                <button type="button" className="btn btn-primary" disabled={saving || Boolean(nothing)} onClick={() => void onApply(body())}>
                  {saving ? 'Yadda saxlanılır…' : 'Təsdiqlə və yadda saxla'}
                </button>
              </>
            ) : (
              <>
                <button type="button" className="btn" onClick={onClose}>Ləğv et</button>
                <button type="button" className="btn btn-primary" disabled={Boolean(problem) || checking} onClick={() => void check()}>
                  {checking ? 'Yoxlanılır…' : 'Davam et'}
                </button>
              </>
            )}
          </div>
        </footer>
      </div>
    </div>
  )
}
