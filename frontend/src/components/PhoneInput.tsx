import type { ChangeEvent } from 'react'
import { cleanPhone, formatPhone, type PhoneVariant } from '../lib/phone'
import { FieldError } from './FieldError'

/**
 * Telefon nömrəsi sahəsi: yalnız rəqəm qəbul edir və nömrəni boşluqlarla göstərir (050 123 45 67).
 * Harada: IdentifierInput (giriş və "PIN-i unutdum", variant "local") və RegisterPage ("international").
 * Props: value — boşluqsuz rəqəmlər; onChange(digits) — təmizlənmiş rəqəmləri valideynə qaytarır;
 * variant — "local" və ya "international"; id; error — sahənin altındakı xəta; disabled; autoComplete.
 * İşləmə: hər dəyişiklikdə yazılanı cleanPhone ilə təmizləyirik (hərf, boşluq, artıq rəqəm silinir),
 * ekranda isə formatPhone ilə boşluqlu göstəririk. Yəni valideyn həmişə təmiz rəqəm alır.
 */
type PhoneInputProps = {
  value: string
  onChange: (digits: string) => void
  variant: PhoneVariant
  id: string
  error?: string | null
  disabled?: boolean
  /** Giriş və "PIN-i unutdum" üçün "username" — brauzer saxladığı hesabı təklif etsin. */
  autoComplete?: string
  boxClassName?: string
}

const PLACEHOLDER: Record<PhoneVariant, string> = {
  local: '0XX XXX XX XX',
  international: '50 123 45 67',
}

export function PhoneInput({
  value,
  onChange,
  variant,
  id,
  error,
  disabled,
  autoComplete = 'tel-national',
  boxClassName,
}: PhoneInputProps) {
  const errorId = `${id}-error`
  const prefixId = `${id}-prefix`

  function handleChange(event: ChangeEvent<HTMLInputElement>) {
    onChange(cleanPhone(event.target.value, variant))
  }

  // Ekran oxuyucu sahəni oxuyanda bu elementləri də oxusun: "+994" ön hissəsi və xəta mətni.
  const describedBy: string[] = []
  if (variant === 'international') describedBy.push(prefixId)
  if (error) describedBy.push(errorId)

  // maxLength qəsdən YOXDUR: "+994 50 123 45 67" yapışdırılanda brauzer onu əvvəlcə maxLength-ə qədər
  // kəsərdi və rəqəmlər itərdi. Uzunluğu cleanPhone təmizlədikdən SONRA kəsir.
  const input = (
    <input
      id={id}
      className="inp"
      type="tel"
      inputMode="numeric"
      autoComplete={autoComplete}
      placeholder={PLACEHOLDER[variant]}
      value={formatPhone(value, variant)}
      onChange={handleChange}
      disabled={disabled}
      aria-invalid={error ? true : undefined}
      aria-describedby={describedBy.length > 0 ? describedBy.join(' ') : undefined}
    />
  )

  return (
    <div>
      {variant === 'international' ? (
        <div className={boxClassName} style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
          <span id={prefixId} className="phone-prefix">
            +994
          </span>
          {input}
        </div>
      ) : (
        <div className={boxClassName}>{input}</div>
      )}
      <FieldError id={errorId} message={error} />
    </div>
  )
}
