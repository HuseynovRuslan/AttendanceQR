import type { CSSProperties } from 'react'
import { PhoneInput } from './PhoneInput'
import { FieldError } from './FieldError'

/** Hansı sahə göstərilir: telefon və ya e-poçt. */
export type IdentifierMode = 'phone' | 'email'

/**
 * "Telefon / E-poçt" seçimi və seçilən sahə — giriş və "PIN-i unutdum" səhifələrinin ilk sahəsi.
 * Harada: LoginPage, ForgotPinPage.
 * Props: mode — hansı sahə görünür; phone və email — iki AYRI dəyər, seçim dəyişəndə yazılan itmir;
 * on...Change — dəyəri valideynə qaytaran funksiyalar; id — görünən sahənin id-si; error; disabled.
 * İşləmə: bu "idarə olunan" (controlled) komponentdir — dəyərləri özü saxlamır, valideyn saxlayır.
 * Çünki "Daxil ol" basılanda serverə nə göndəriləcəyini valideyn (səhifə) bilməlidir.
 */
type IdentifierInputProps = {
  mode: IdentifierMode
  onModeChange: (mode: IdentifierMode) => void
  phone: string
  onPhoneChange: (digits: string) => void
  email: string
  onEmailChange: (email: string) => void
  id: string
  error?: string | null
  disabled?: boolean
  switchClassName?: string
  switchStyle?: CSSProperties
  fieldClassName?: string
  fieldStyle?: CSSProperties
  boxClassName?: string
}

export function IdentifierInput({
  mode,
  onModeChange,
  phone,
  onPhoneChange,
  email,
  onEmailChange,
  id,
  error,
  disabled,
  switchClassName = '',
  switchStyle,
  fieldClassName,
  fieldStyle,
  boxClassName,
}: IdentifierInputProps) {
  const errorId = `${id}-error`

  return (
    <div className="identifier-input">
      {/* İki düymə bir seçim kimi işləyir; aria-pressed ekran oxuyucuya hansının seçildiyini deyir. */}
      <div
        role="group"
        aria-label="Giriş üsulu"
        className={`seg ${switchClassName}`}
        style={{ marginBottom: 14, ...switchStyle }}
      >
        <button
          type="button"
          className="seg-btn"
          aria-pressed={mode === 'phone'}
          onClick={() => onModeChange('phone')}
          disabled={disabled}
        >
          Telefon
        </button>
        <button
          type="button"
          className="seg-btn"
          aria-pressed={mode === 'email'}
          onClick={() => onModeChange('email')}
          disabled={disabled}
        >
          E-poçt
        </button>
      </div>

      <div className={fieldClassName} style={fieldStyle}>
        {mode === 'phone' ? (
          <>
            <label className="form-label" htmlFor={id}>
              Telefon nömrəsi
            </label>
            <PhoneInput
              id={id}
              variant="local"
              value={phone}
              onChange={onPhoneChange}
              error={error}
              disabled={disabled}
              autoComplete="username"
              boxClassName={boxClassName}
            />
          </>
        ) : (
          <>
            <label className="form-label" htmlFor={id}>
              E-poçt
            </label>
            <div className={boxClassName}>
              <input
                id={id}
                className="inp"
                type="email"
                inputMode="email"
                autoComplete="username"
                autoCapitalize="none"
                spellCheck={false}
                placeholder="ad@sirket.az"
                value={email}
                onChange={(event) => onEmailChange(event.target.value)}
                disabled={disabled}
                aria-invalid={error ? true : undefined}
                aria-describedby={error ? errorId : undefined}
              />
            </div>
            <FieldError id={errorId} message={error} />
          </>
        )}
      </div>
    </div>
  )
}
