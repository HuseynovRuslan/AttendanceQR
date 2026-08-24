import { useRef, useState, type ChangeEvent, type ClipboardEvent, type KeyboardEvent } from 'react'
import { applyBackspace, applyDigit, applyPaste } from './pinDigits'

/**
 * The PIN box on the login screen: four squares instead of one long field.
 *
 * The old field was a bare password input labelled "Şifrə / PIN", and older employees typed their phone
 * number into it, or a word, or gave up — nothing on screen said how much was expected. Four boxes say
 * it without a sentence: this many characters, digits, and you are done.
 *
 * Two things it must not do. It must not lock anyone out: an account created before PinRules existed can
 * still hold a longer password, so there is a way back to a single field, and paste always works. And it
 * must not hide the PIN from the person typing it — a four-digit code that cannot be re-read is a login
 * failure waiting to happen on a phone keyboard, so the eye toggle is there and starts hidden.
 */
export function PinInput({
  value,
  onChange,
  length = 4,
}: {
  value: string
  onChange: (next: string) => void
  length?: number
}) {
  const [visible, setVisible] = useState(false)
  const [freeform, setFreeform] = useState(false)
  const boxes = useRef<(HTMLInputElement | null)[]>([])

  const digits = Array.from({ length }, (_, i) => value[i] ?? '')

  function setDigit(index: number, raw: string) {
    const { next, focus } = applyDigit(value, length, index, raw)
    onChange(next)
    if (focus !== index) boxes.current[focus]?.focus()
  }

  function onKeyDown(index: number, e: KeyboardEvent<HTMLInputElement>) {
    if (e.key === 'Backspace') {
      const { next, focus, handled } = applyBackspace(value, length, index)
      if (handled) {
        e.preventDefault()
        onChange(next)
        boxes.current[focus]?.focus()
      }
    }
    if (e.key === 'ArrowLeft') boxes.current[index - 1]?.focus()
    if (e.key === 'ArrowRight') boxes.current[index + 1]?.focus()
  }

  function onPaste(e: ClipboardEvent<HTMLInputElement>) {
    // A temporary PIN arrives by message and gets pasted, so pasting has to fill the row rather than
    // land four digits in one box.
    const result = applyPaste(e.clipboardData.getData('text'), length)
    if (!result) return
    e.preventDefault()
    onChange(result.next)
    boxes.current[result.focus]?.focus()
  }

  const eye = (
    <button
      type="button"
      onClick={() => setVisible((v) => !v)}
      aria-label={visible ? 'PIN-i gizlət' : 'PIN-i göstər'}
      style={{
        background: 'none',
        border: 'none',
        cursor: 'pointer',
        fontSize: 18,
        lineHeight: 1,
        padding: 4,
        color: 'var(--c500)',
      }}
    >
      {visible ? '🙈' : '👁️'}
    </button>
  )

  if (freeform) {
    return (
      <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
        <input
          className="inp"
          type={visible ? 'text' : 'password'}
          required
          autoComplete="current-password"
          value={value}
          onChange={(e: ChangeEvent<HTMLInputElement>) => onChange(e.target.value)}
        />
        {eye}
      </div>
    )
  }

  return (
    <div>
      <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
        <div style={{ display: 'flex', gap: 8, flex: 1 }}>
          {digits.map((digit, i) => (
            <input
              key={i}
              ref={(el) => {
                boxes.current[i] = el
              }}
              className="inp"
              type={visible ? 'text' : 'password'}
              inputMode="numeric"
              autoComplete={i === 0 ? 'current-password' : 'off'}
              aria-label={`PIN ${i + 1}`}
              maxLength={1}
              value={digit}
              onChange={(e) => setDigit(i, e.target.value)}
              onKeyDown={(e) => onKeyDown(i, e)}
              onPaste={onPaste}
              onFocus={(e) => e.currentTarget.select()}
              style={{ textAlign: 'center', fontSize: 22, fontWeight: 700, padding: '10px 0', flex: 1, minWidth: 0 }}
            />
          ))}
        </div>
        {eye}
      </div>
      {/* The way out for an account whose password predates the four-digit rule. Quiet on purpose:
          almost nobody needs it, and everybody who does would otherwise be stuck. */}
      <button
        type="button"
        onClick={() => setFreeform(true)}
        style={{
          marginTop: 8,
          background: 'none',
          border: 'none',
          color: 'var(--c500)',
          fontSize: 12,
          cursor: 'pointer',
          textDecoration: 'underline',
          textUnderlineOffset: 2,
          padding: 0,
        }}
      >
        Şifrəm 4 rəqəmdən uzundur
      </button>
    </div>
  )
}
