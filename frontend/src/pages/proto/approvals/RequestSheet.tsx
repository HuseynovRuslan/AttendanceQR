import { useMemo, useState } from 'react'
import { IconAlert, IconX } from '../../../components/icons'
import { POSITIONS, SHIFTS } from './data'
import {
  NEEDS_EMPLOYEE, TYPE_LABEL, branchesInScope, canCreate, changesFor, emptyDraft, validate, type Draft, type RequestType,
} from './model'
import { useProto } from './store'
import { TypeIcon, useSheet } from './ui'

const TYPES = Object.keys(TYPE_LABEL) as RequestType[]

/**
 * A new request — or, for an Operator inside their own structure, a direct act. One sheet for all nine kinds:
 * the kind decides which fields appear, and nothing is asked that the kind does not need.
 */
export function RequestSheet({ onClose, presetBranchId, presetEmployeeId, onCreated }: {
  onClose: () => void
  presetBranchId?: string
  presetEmployeeId?: string
  onCreated?: (id: string) => void
}) {
  const { world: w, persona, create, loading } = useProto()
  const allowed = useMemo(() => branchesInScope(w, persona).filter((b) => canCreate(w, persona, b.id)), [w, persona])
  const [d, setD] = useState<Draft>(() => {
    const base = emptyDraft(presetBranchId ?? allowed[0]?.id ?? '')
    const emp = presetEmployeeId ? w.employees.find((e) => e.id === presetEmployeeId) : undefined
    return { ...base, employeeId: emp?.id ?? null, makeActive: emp ? !emp.active : false }
  })
  const [tried, setTried] = useState(false)
  const ref = useSheet(onClose)

  const direct = persona.kind === 'operator'
  const staff = w.employees.filter((e) => e.branchId === d.branchId)
  const emp = d.employeeId ? w.employees.find((e) => e.id === d.employeeId) : undefined
  const errors = validate(w, d)
  const preview = changesFor(w, d)
  const show = (k: keyof typeof errors) => (tried ? errors[k] : undefined)

  const set = <K extends keyof Draft>(k: K, v: Draft[K]) => setD((x) => ({ ...x, [k]: v }))
  const pickType = (type: RequestType) => setD((x) => {
    const person = x.employeeId ? w.employees.find((e) => e.id === x.employeeId) : undefined
    return {
      ...emptyDraft(x.branchId), type, note: x.note,
      employeeId: type === 'employee_create' ? null : x.employeeId,
      makeActive: person ? !person.active : false,
      fields: type === 'employee_update' && person ? { name: person.name, position: person.position, phone: person.phone } : emptyDraft('').fields,
    }
  })
  const pickEmployee = (id: string) => setD((x) => {
    const person = w.employees.find((e) => e.id === id)
    return {
      ...x, employeeId: id || null, makeActive: person ? !person.active : false,
      fields: x.type === 'employee_update' && person ? { name: person.name, position: person.position, phone: person.phone } : x.fields,
    }
  })

  async function submit() {
    setTried(true)
    if (Object.keys(errors).length) return
    const r = await create(d)
    if (r.ok && r.id) {
      onCreated?.(r.id)
      onClose()
    }
  }

  if (!allowed.length)
    return null

  return (
    <div className="apx-sheet-back" onMouseDown={(ev) => ev.target === ev.currentTarget && onClose()}>
      <div className="apx-sheet" role="dialog" aria-modal="true" aria-labelledby="apx-req-title" ref={ref} tabIndex={-1}>
        <div className="apx-sheet-h">
          <div>
            <h2 id="apx-req-title">{direct ? 'Yeni əməliyyat' : 'Yeni müraciət'}</h2>
            <p>
              {direct
                ? 'Öz strukturunuzda başlatdığınız əməliyyat dərhal tətbiq olunur və audit tarixçəsinə yazılır.'
                : 'Müraciət strukturun operatoruna gedir. Qərar veriləndən sonra tətbiq olunur.'}
            </p>
          </div>
          <button type="button" className="apx-btn apx-btn-ghost apx-btn-sm" onClick={onClose} aria-label="Bağla"><IconX /></button>
        </div>

        <form className="apx-sheet-b" onSubmit={(e) => { e.preventDefault(); void submit() }} noValidate>
          <fieldset className="apx-field" style={{ border: 0, padding: 0, margin: 0 }}>
            <legend style={{ fontSize: 12.5, fontWeight: 700, color: 'var(--c700)', marginBottom: 6 }}>Müraciətin növü</legend>
            <div className="apx-types">
              {TYPES.map((t) => (
                <button key={t} type="button" className="apx-type" aria-pressed={d.type === t} onClick={() => pickType(t)}>
                  <TypeIcon type={t} /> {TYPE_LABEL[t]}
                </button>
              ))}
            </div>
          </fieldset>

          {allowed.length > 1 && (
            <label className="apx-field">
              <span>Filial</span>
              <select className="apx-select" value={d.branchId} onChange={(e) => setD((x) => ({ ...x, branchId: e.target.value, employeeId: null }))}>
                {allowed.map((b) => <option key={b.id} value={b.id}>{b.name}</option>)}
              </select>
            </label>
          )}

          {NEEDS_EMPLOYEE.has(d.type) && (
            <label className="apx-field">
              <span>İşçi</span>
              <select className="apx-select" value={d.employeeId ?? ''} onChange={(e) => pickEmployee(e.target.value)} aria-invalid={!!show('employee')}>
                <option value="">İşçini seçin</option>
                {staff.map((e) => <option key={e.id} value={e.id}>{e.name}{e.active ? '' : ' (deaktiv)'}</option>)}
              </select>
              {(show('employee') || (d.type === 'employee_delete' && emp?.hasHistory)) && (
                <span className="apx-field-err" role="alert">{errors.employee}</span>
              )}
              {d.type === 'employee_delete' && emp?.hasHistory && (
                <button
                  type="button" className="apx-btn apx-btn-sm" style={{ justifySelf: 'start' }}
                  onClick={() => setD((x) => ({ ...x, type: 'employee_activation', makeActive: false }))}
                >
                  Deaktivasiya müraciətinə keç
                </button>
              )}
            </label>
          )}

          {(d.type === 'employee_create' || d.type === 'employee_update') && (
            <div className="apx-form">
              <label className="apx-field">
                <span>Ad, soyad</span>
                <input className="apx-input" value={d.fields.name} onChange={(e) => set('fields', { ...d.fields, name: e.target.value })} />
              </label>
              <div className="apx-pair">
                <label className="apx-field">
                  <span>Vəzifə</span>
                  <select className="apx-select" value={d.fields.position} onChange={(e) => set('fields', { ...d.fields, position: e.target.value })}>
                    <option value="">Kataloqdan seçin</option>
                    {POSITIONS.map((p) => <option key={p} value={p}>{p}</option>)}
                  </select>
                </label>
                <label className="apx-field">
                  <span>Telefon</span>
                  <input className="apx-input" inputMode="tel" value={d.fields.phone} onChange={(e) => set('fields', { ...d.fields, phone: e.target.value })} />
                </label>
              </div>
              {show('fields') && <span className="apx-field-err" role="alert">{errors.fields}</span>}
            </div>
          )}

          {d.type === 'employee_activation' && emp && (
            <p className="apx-note" style={{ margin: 0 }}>
              {emp.active
                ? 'İşçi deaktiv ediləcək: daxil ola və skan edə bilməyəcək, tarixçəsi və tabeli olduğu kimi qalır.'
                : 'İşçi yenidən aktiv ediləcək: köhnə PIN-i ilə daxil ola biləcək.'}
            </p>
          )}

          {d.type === 'shift_change' && (
            <label className="apx-field">
              <span>Yeni növbə</span>
              <select className="apx-select" value={d.shift} onChange={(e) => set('shift', e.target.value)} aria-invalid={!!show('shift')}>
                <option value="">Növbəni seçin</option>
                {SHIFTS.filter((s) => s !== emp?.shift).map((s) => <option key={s} value={s}>{s}</option>)}
              </select>
              {show('shift') && <span className="apx-field-err" role="alert">{errors.shift}</span>}
            </label>
          )}

          {(d.type === 'permission' || d.type === 'vacation' || d.type === 'sick' || d.type === 'attendance_fix' || d.type === 'shift_change') && (
            <div className="apx-form">
              <div className="apx-pair">
                <label className="apx-field">
                  <span>{d.type === 'permission' || d.type === 'attendance_fix' ? 'Tarix' : d.type === 'shift_change' ? 'Hansı tarixdən' : 'Başlanğıc'}</span>
                  <input type="date" className="apx-input" value={d.dateFrom} onChange={(e) => set('dateFrom', e.target.value)} aria-invalid={!!show('dates')} />
                </label>
                {(d.type === 'vacation' || d.type === 'sick') && (
                  <label className="apx-field">
                    <span>Bitmə</span>
                    <input type="date" className="apx-input" value={d.dateTo} min={d.dateFrom || undefined} onChange={(e) => set('dateTo', e.target.value)} />
                  </label>
                )}
              </div>
              {show('dates') && <span className="apx-field-err" role="alert">{errors.dates}</span>}
              {d.type === 'permission' && (
                <>
                  <div className="apx-pair">
                    <label className="apx-field">
                      <span>Başlanğıc saatı</span>
                      <input type="time" className="apx-input" value={d.timeFrom} onChange={(e) => set('timeFrom', e.target.value)} />
                    </label>
                    <label className="apx-field">
                      <span>Bitmə saatı</span>
                      <input type="time" className="apx-input" value={d.timeTo} onChange={(e) => set('timeTo', e.target.value)} />
                    </label>
                  </div>
                  <span className="apx-quiet">Saatları boş saxlasanız, bütün günlük icazə sayılır.</span>
                  {show('times') && <span className="apx-field-err" role="alert">{errors.times}</span>}
                </>
              )}
            </div>
          )}

          <label className="apx-field">
            <span>Qeyd / Səbəb</span>
            <textarea
              className="apx-textarea" value={d.note} onChange={(e) => set('note', e.target.value)} aria-invalid={!!show('note')}
              placeholder={d.type === 'attendance_fix' ? 'Nə baş verib və düzgün saat neçə olmalıdır' : 'Operatorun qərar verməsi üçün lazım olan qısa izah'}
            />
            {show('note') && <span className="apx-field-err" role="alert">{errors.note}</span>}
          </label>

          {preview.length > 0 && (
            <div>
              <h3 className="apx-sec-t">Nə dəyişəcək</h3>
              <table className="apx-diff">
                <thead><tr><th scope="col">Sahə</th><th scope="col">İndi</th><th scope="col">Olacaq</th></tr></thead>
                <tbody>
                  {preview.map((c) => (
                    <tr key={c.field}>
                      <td>{c.label}</td>
                      <td>{c.before ? <span className="apx-was">{c.before}</span> : <span className="apx-empty-cell">—</span>}</td>
                      <td>{c.after ? <span className="apx-will">{c.after}</span> : <span className="apx-empty-cell">—</span>}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}

          {tried && Object.keys(errors).length > 0 && (
            <p className="apx-lock" role="status" style={{ margin: 0 }}>
              <IconAlert aria-hidden /> Göndərmək üçün yuxarıda qırmızı ilə göstərilənləri düzəldin.
            </p>
          )}
          <button type="submit" hidden />
        </form>

        <div className="apx-sheet-f">
          <button type="button" className="apx-btn" onClick={onClose}>Bağla</button>
          <button type="button" className="apx-btn apx-btn-go" disabled={loading} onClick={() => void submit()}>
            {loading ? 'Göndərilir…' : direct ? 'Birbaşa tətbiq et' : 'Operatora göndər'}
          </button>
        </div>
      </div>
    </div>
  )
}
