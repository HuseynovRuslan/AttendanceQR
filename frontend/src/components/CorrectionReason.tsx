import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react'

/**
 * The owner's rule of 2026-10-15: a correction to somebody's attendance says why, and the why goes into the
 * audit line beside the change. One dialog for every correction — edit, add a day, close forgotten days,
 * undo a check-out, delete, a Qayıb put on or taken off, a forgotten check-out decided, a day voided — and
 * it doubles as the confirmation the screens used to ask with window.confirm, so a correction is still one
 * decision, not two.
 *
 * The presets are the reasons that actually come up; anything else is typed. A screen whose corrections
 * have reasons of their own (a Qayıb, a voided day) passes its own.
 */
const PRESETS = [
  'Çıxış vurmağı unudub',
  'Telefon və ya internet problemi — skan edə bilmədi',
  'Səhv və ya təkrar skan',
  'Rəhbərin təsdiqi ilə',
]

/** What the two new refusals mean, for every screen that corrects attendance. */
export const CORRECTION_ERROR: Record<string, string> = {
  ReasonRequired: 'Səbəbi yazın — ən azı 3 hərf.',
  OwnAttendance: 'Öz davamiyyətinizə düzəliş edə bilməzsiniz — bunu başqa admin və ya rəhbəriniz edir.',
}

interface AskOptions {
  title: string
  detail?: string
  confirmLabel?: string
  /** For a confirm button that itself says «ləğv et» (voiding a day) — two «Ləğv et» side by side is a trap. */
  cancelLabel?: string
  danger?: boolean
  presets?: string[]
}

/** `ask` resolves to the reason, or null when the dialog was dismissed. Render `dialog` once in the page. */
export function useCorrectionReason(): { ask: (o: AskOptions) => Promise<string | null>; dialog: ReactNode } {
  const [pending, setPending] = useState<(AskOptions & { resolve: (r: string | null) => void }) | null>(null)
  const ask = useCallback(
    (o: AskOptions) => new Promise<string | null>((resolve) => setPending({ ...o, resolve })),
    [],
  )
  const dialog = pending ? (
    <ReasonDialog
      {...pending}
      onDone={(reason) => {
        pending.resolve(reason)
        setPending(null)
      }}
    />
  ) : null
  return { ask, dialog }
}

function ReasonDialog({
  title, detail, confirmLabel = 'Təsdiq et', cancelLabel = 'Ləğv et', danger, presets = PRESETS, onDone,
}: AskOptions & { onDone: (reason: string | null) => void }) {
  const [text, setText] = useState('')
  const input = useRef<HTMLTextAreaElement>(null)
  const done = useRef(onDone)
  done.current = onDone

  useEffect(() => {
    input.current?.focus()
    // Capture phase, and the Escape stops here: the dialog can open over another modal (the photo
    // comparison) whose own Escape would otherwise close both at once.
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== 'Escape') return
      e.stopPropagation()
      done.current(null)
    }
    window.addEventListener('keydown', onKey, true)
    return () => window.removeEventListener('keydown', onKey, true)
  }, [])

  const trimmed = text.trim()
  const valid = trimmed.length >= 3 && trimmed.length <= 500

  return (
    <div className="acc-modal-backdrop" onClick={() => onDone(null)}>
      <div className="acc-modal" role="dialog" aria-modal="true" aria-label={title} onClick={(e) => e.stopPropagation()}>
        <h3 className="acc-modal-title">{title}</h3>
        {detail && <p className="acc-modal-detail">{detail}</p>}
        <label className="acc-label" htmlFor="correction-reason">
          Səbəb <span className="muted">— məcburidir, jurnala yazılır</span>
        </label>
        <div className="acc-chips">
          {presets.map((p) => (
            <button
              key={p}
              type="button"
              className={`acc-chip${trimmed === p ? ' on' : ''}`}
              onClick={() => setText(p)}
            >
              {p}
            </button>
          ))}
        </div>
        <textarea
          id="correction-reason"
          ref={input}
          className="inp acc-reason"
          rows={3}
          maxLength={500}
          value={text}
          onChange={(e) => setText(e.target.value)}
          placeholder="Və ya öz sözlərinizlə yazın…"
        />
        <div className="acc-modal-foot">
          <button type="button" className="btn btn-sm" onClick={() => onDone(null)}>
            {cancelLabel}
          </button>
          <button
            type="button"
            className={`btn btn-sm ${danger ? 'btn-danger' : 'btn-primary'}`}
            disabled={!valid}
            onClick={() => onDone(trimmed)}
          >
            {confirmLabel}
          </button>
        </div>
      </div>
    </div>
  )
}
