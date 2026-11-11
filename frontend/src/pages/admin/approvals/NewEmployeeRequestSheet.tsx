import { useEffect, useRef, useState } from 'react'
import {
  createEmployeeRequest, getFormOptions, isApiError, type FormOptions, type RequestSummary,
} from '../../../api/approvals'
import { IconAlert, IconX } from '../../../components/icons'
import { PhoneInput } from '../../../components/PhoneInput'
import { useSheet } from '../../approvals/shared'
import { EMPTY_DRAFT, errorText, newIdempotencyKey, validateDraft, type DraftErrors, type NewEmployeeDraft } from './model'
import { call } from './parts'

/**
 * «Yeni işçi müraciəti» for one branch. Nothing here is decided by the screen: the fields are checked against the
 * server's own rules so the person sees a problem at the field, and the server checks them all again.
 *
 * One press of «Göndər» is one request. The press carries an Idempotency-Key that stays the same while that press is
 * retried (a dropped connection, a double tap), so the server answers with the request it already made instead of a
 * second one. A new key is taken only after the server has answered.
 */
export function NewEmployeeRequestSheet({ branch, onClose, onSent }: {
  branch: { locationId: string; name: string; structureName: string | null }
  onClose: () => void
  onSent: (summary: RequestSummary) => void
}) {
  const ref = useSheet(onClose)
  const [d, setD] = useState<NewEmployeeDraft>(EMPTY_DRAFT)
  const [errors, setErrors] = useState<DraftErrors>({})
  const [tried, setTried] = useState(false)
  const [busy, setBusy] = useState(false)
  const [problem, setProblem] = useState<string | null>(null)
  const [options, setOptions] = useState<FormOptions | null>(null)
  const [optionsFailed, setOptionsFailed] = useState(false)
  const key = useRef(newIdempotencyKey())

  useEffect(() => {
    let live = true
    void call(getFormOptions(branch.locationId)).then((r) => {
      if (!live) return
      if (r.status === 200 && r.data && !isApiError(r.data)) setOptions(r.data as FormOptions)
      else setOptionsFailed(true)
    })
    return () => { live = false }
  }, [branch.locationId])

  const set = <K extends keyof NewEmployeeDraft>(k: K, v: NewEmployeeDraft[K]) => {
    const next = { ...d, [k]: v }
    setD(next)
    if (tried) setErrors(validateDraft(next))
  }

  async function submit() {
    if (busy) return
    setTried(true)
    const found = validateDraft(d)
    setErrors(found)
    if (Object.keys(found).length > 0) return
    setBusy(true)
    setProblem(null)
    const res = await call(createEmployeeRequest({
      firstName: d.firstName.trim(),
      lastName: d.lastName.trim(),
      fatherName: d.fatherName.trim() || null,
      phoneNumber: d.phone.trim() || null,
      email: d.email.trim() || null,
      position: d.position || null,
      birthDate: d.birthDate || null,
      scheduleId: d.scheduleId || null,
      locationId: branch.locationId,
      note: d.note.trim() || null,
    }, key.current))
    setBusy(false)
    if ((res.status === 201 || res.status === 200) && res.data && !isApiError(res.data)) {
      onSent(res.data as RequestSummary)
      return
    }
    // The server answered: this press is over. A network failure keeps the key, so pressing again is the same press.
    if (res.status !== 0) key.current = newIdempotencyKey()
    setProblem(errorText(res.status, res.data))
  }

  const show = (k: keyof DraftErrors) => (tried ? errors[k] : undefined)

  return (
    <div className="apx-sheet-back" onMouseDown={(ev) => ev.target === ev.currentTarget && !busy && onClose()}>
      <div className="apx-sheet" role="dialog" aria-modal="true" aria-labelledby="apx-new-t" ref={ref} tabIndex={-1}>
        <div className="apx-sheet-h">
          <div>
            <h2 id="apx-new-t">Yeni işçi müraciəti</h2>
            <p>{branch.name}{branch.structureName ? `, ${branch.structureName}` : ''}. Operator təsdiqləyəndən sonra işçi hesabı yaranır.</p>
          </div>
          <button type="button" className="apx-btn apx-btn-ghost apx-btn-sm" onClick={onClose} aria-label="Bağla" disabled={busy}><IconX /></button>
        </div>
        <form className="apx-sheet-b" onSubmit={(e) => { e.preventDefault(); void submit() }} noValidate>
          <div className="apx-form">
            <div className="apx-pair">
              <label className="apx-field">
                <span>Ad</span>
                <input className="apx-input" value={d.firstName} onChange={(e) => set('firstName', e.target.value)} maxLength={100}
                  aria-invalid={!!show('name')} autoComplete="off" />
              </label>
              <label className="apx-field">
                <span>Soyad</span>
                <input className="apx-input" value={d.lastName} onChange={(e) => set('lastName', e.target.value)} maxLength={100}
                  aria-invalid={!!show('name')} autoComplete="off" />
              </label>
            </div>
            {show('name') && <span className="apx-field-err" role="alert" style={{ marginTop: -8 }}>{errors.name}</span>}
            <label className="apx-field">
              <span>Ata adı (istəyə bağlı)</span>
              <input className="apx-input" value={d.fatherName} onChange={(e) => set('fatherName', e.target.value)} maxLength={100}
                aria-invalid={!!show('father')} autoComplete="off" />
              {show('father') && <span className="apx-field-err" role="alert">{errors.father}</span>}
            </label>
            <div className="apx-pair">
              <div className="apx-field">
                <label htmlFor="apx-new-phone"><span>Telefon (giriş üçün)</span></label>
                <PhoneInput id="apx-new-phone" variant="local" value={d.phone} onChange={(v) => set('phone', v)}
                  error={show('phone') ?? null} autoComplete="off" />
              </div>
              <label className="apx-field">
                <span>E-poçt (istəyə bağlı)</span>
                <input className="apx-input" type="email" inputMode="email" value={d.email} onChange={(e) => set('email', e.target.value)}
                  aria-invalid={!!show('email')} autoComplete="off" />
                {show('email') && <span className="apx-field-err" role="alert">{errors.email}</span>}
              </label>
            </div>
            {show('contact') && <span className="apx-field-err" role="alert" style={{ marginTop: -8 }}>{errors.contact}</span>}
            <div className="apx-pair">
              <label className="apx-field">
                <span>Vəzifə</span>
                <select className="apx-select" aria-label="Vəzifə" value={d.position} onChange={(e) => set('position', e.target.value)} disabled={!options}>
                  <option value="">{options ? (options.positions.length ? 'Seçin (istəyə bağlı)' : 'Kataloqda vəzifə yoxdur') : 'Yüklənir…'}</option>
                  {options?.positions.map((p) => <option key={p} value={p}>{p}</option>)}
                </select>
              </label>
              <label className="apx-field">
                <span>Doğum tarixi (istəyə bağlı)</span>
                <input className="apx-input" type="date" value={d.birthDate} onChange={(e) => set('birthDate', e.target.value)} max={new Date().toISOString().slice(0, 10)} />
              </label>
            </div>
            <label className="apx-field">
              <span>Növbə</span>
              <select className="apx-select" aria-label="Növbə" value={d.scheduleId} onChange={(e) => set('scheduleId', e.target.value)} disabled={!options}>
                <option value="">{options ? 'Sonra təyin olunacaq' : 'Yüklənir…'}</option>
                {options?.schedules.map((s) => <option key={s.id} value={s.id}>{s.name} ({s.start}–{s.end})</option>)}
              </select>
              {optionsFailed && <span className="apx-quiet">Vəzifə və növbə siyahısı yüklənmədi. Müraciəti onlarsız da göndərə bilərsiniz.</span>}
            </label>
            <label className="apx-field">
              <span>Qeyd (operator görəcək)</span>
              <textarea className="apx-textarea" value={d.note} onChange={(e) => set('note', e.target.value)} maxLength={500}
                aria-invalid={!!show('note')} placeholder="Məsələn: sabahdan işə başlayır, sənədləri təhvil verilib" />
              {show('note') && <span className="apx-field-err" role="alert">{errors.note}</span>}
            </label>
          </div>
          {problem && (
            <p className="apx-lock" role="alert" style={{ margin: 0, background: 'var(--clay-bg)', color: '#6E2A18' }}>
              <IconAlert aria-hidden /> {problem}
            </p>
          )}
          <div className="apx-sheet-f" style={{ margin: '0 -22px -18px' }}>
            <button type="button" className="apx-btn apx-btn-ghost" onClick={onClose} disabled={busy}>İmtina</button>
            <button type="submit" className="apx-btn apx-btn-go" disabled={busy}>{busy ? 'Göndərilir…' : 'Operatora göndər'}</button>
          </div>
        </form>
      </div>
    </div>
  )
}
