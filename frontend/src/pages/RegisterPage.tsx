import { useEffect, useState, type CSSProperties, type FormEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { registerCompany, resendCode, sendCode, verifyCode, type RegistrationData } from '../api/register'
import { AuthLayout } from '../components/auth/AuthLayout'
import { riseOrder } from '../components/auth/riseOrder'
import { shakeClass } from '../components/auth/shake'
import { FieldError } from '../components/FieldError'
import { OtpInput } from '../components/OtpInput'
import { PhoneInput } from '../components/PhoneInput'
import { PinEyeButton } from '../components/PinEyeButton'
import { IconArrowLeft, IconCheck, IconX } from '../components/icons'
import { LANDING_PRIVACY_URL } from '../lib/host'
import { formatPhone, phoneError } from '../lib/phone'
import { isValidEmail, personNameError } from '../lib/validation'
import './RegisterPage.css'

type Step = 'form' | 'code' | 'done'
type FieldName = 'companyName' | 'firstName' | 'lastName' | 'email' | 'phone' | 'pin' | 'pinRepeat'
type FormErrors = Partial<Record<FieldName, string>>

const FIELD_IDS: Record<FieldName, string> = {
  companyName: 'register-company',
  firstName: 'register-first-name',
  lastName: 'register-last-name',
  email: 'register-email',
  phone: 'register-phone',
  pin: 'register-pin',
  pinRepeat: 'register-pin-repeat',
}
const FIELD_ORDER: FieldName[] = ['companyName', 'firstName', 'lastName', 'email', 'phone', 'pin', 'pinRepeat']
const CODE_ID = 'register-code'
const CODE_ERROR_ID = 'register-code-error'
const DONE_TITLE_ID = 'register-done-title'

const PIN_LENGTH = 4
const CODE_LENGTH = 6
const MIN_COMPANY_NAME_LENGTH = 2
const RESEND_SECONDS = 60
const NETWORK_ERROR = 'Serverə qoşulmaq mümkün olmadı'
const EMAIL_ERROR = 'E-poçt ünvanını düzgün yazın'

/* Validation */
function validateForm(data: RegistrationData, pinRepeat: string): FormErrors {
  const errors: FormErrors = {}

  if (data.companyName.length < MIN_COMPANY_NAME_LENGTH) {
    errors.companyName = `Şirkət adı ən azı ${MIN_COMPANY_NAME_LENGTH} simvol olmalıdır`
  }

  const firstNameProblem = personNameError(data.firstName, 'Ad')
  if (firstNameProblem) errors.firstName = firstNameProblem

  const lastNameProblem = personNameError(data.lastName, 'Soyad')
  if (lastNameProblem) errors.lastName = lastNameProblem

  if (!isValidEmail(data.email)) errors.email = EMAIL_ERROR

  const phoneProblem = phoneError(data.phone, 'international')
  if (phoneProblem) errors.phone = phoneProblem

  if (data.pin.length !== PIN_LENGTH) errors.pin = `PIN ${PIN_LENGTH} rəqəm olmalıdır`
  if (pinRepeat !== data.pin) errors.pinRepeat = 'PIN-lər eyni deyil'

  return errors
}

/* Email: no spaces, lower case */
function cleanEmail(text: string): string {
  return text.replace(/\s/g, '').toLowerCase()
}

/* Countdown label: 60 → "00:60" */
function formatCountdown(seconds: number): string {
  return `00:${String(seconds).padStart(2, '0')}`
}

export function RegisterPage() {
  const navigate = useNavigate()
  const [step, setStep] = useState<Step>('form')

  /* Form fields */
  const [companyName, setCompanyName] = useState('')
  const [firstName, setFirstName] = useState('')
  const [lastName, setLastName] = useState('')
  const [email, setEmail] = useState('')
  const [phone, setPhone] = useState('')
  const [pin, setPin] = useState('')
  const [pinRepeat, setPinRepeat] = useState('')
  const [errors, setErrors] = useState<FormErrors>({})
  const [formShakes, setFormShakes] = useState(0)

  /* Code step */
  const [code, setCode] = useState('')
  const [codeError, setCodeError] = useState<string | null>(null)
  const [codeShakes, setCodeShakes] = useState(0)
  const [secondsLeft, setSecondsLeft] = useState(0)
  const [resending, setResending] = useState(false)
  const [resendNotice, setResendNotice] = useState(false)
  const [sceneAlertKey, setSceneAlertKey] = useState(0)

  const [serverError, setServerError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [focusTargetId, setFocusTargetId] = useState<string | null>(null)

  const registrationData: RegistrationData = {
    companyName: companyName.trim(),
    firstName: firstName.trim(),
    lastName: lastName.trim(),
    email,
    phone,
    pin,
  }

  /* Focus after a step change */
  useEffect(() => {
    if (focusTargetId === null) return
    document.getElementById(focusTargetId)?.focus()
    setFocusTargetId(null)
  }, [focusTargetId])

  /* Resend countdown */
  const countingDown = step === 'code' && secondsLeft > 0
  useEffect(() => {
    if (!countingDown) return
    const timerId = window.setInterval(() => {
      setSecondsLeft((previous) => Math.max(previous - 1, 0))
    }, 1000)
    return () => window.clearInterval(timerId)
  }, [countingDown])

  function clearError(field: FieldName) {
    setErrors((previous) => ({ ...previous, [field]: undefined }))
  }

  /* Shake class for a field that has an error */
  function fieldShake(field: FieldName): string | undefined {
    return errors[field] ? shakeClass(formShakes) : undefined
  }

  function checkEmailOnBlur() {
    if (email === '') return
    if (!isValidEmail(email)) setErrors((previous) => ({ ...previous, email: EMAIL_ERROR }))
  }

  async function handleSendCode(event: FormEvent) {
    event.preventDefault()
    setServerError(null)

    const found = validateForm(registrationData, pinRepeat)
    setErrors(found)
    for (const field of FIELD_ORDER) {
      if (found[field]) {
        setFormShakes((count) => count + 1)
        document.getElementById(FIELD_IDS[field])?.focus()
        return
      }
    }

    setBusy(true)
    try {
      await sendCode(phone)
    } catch {
      setServerError(NETWORK_ERROR)
      return
    } finally {
      setBusy(false)
    }

    setCode('')
    setCodeError(null)
    setResendNotice(false)
    setSecondsLeft(RESEND_SECONDS)
    setStep('code')
    setFocusTargetId(CODE_ID)
  }

  function showCodeError(message: string) {
    setCodeError(message)
    setCodeShakes((count) => count + 1)
    document.getElementById(CODE_ID)?.focus()
  }

  async function handleConfirm(event: FormEvent) {
    event.preventDefault()
    setServerError(null)

    if (code.length !== CODE_LENGTH) {
      showCodeError(`Kodun ${CODE_LENGTH} rəqəmini də yazın`)
      return
    }

    setBusy(true)
    try {
      const correct = await verifyCode(phone, code)
      if (!correct) {
        setCode('')
        setSceneAlertKey((count) => count + 1)
        showCodeError('Kod yanlışdır. Yenidən yoxlayın.')
        return
      }
      await registerCompany(registrationData)
      // TODO(backend): sign the new admin in and open the admin panel instead of the "done" step
      setStep('done')
      setFocusTargetId(DONE_TITLE_ID)
    } catch {
      setServerError(NETWORK_ERROR)
    } finally {
      setBusy(false)
    }
  }

  async function handleResend() {
    if (secondsLeft > 0 || resending) return
    setServerError(null)
    setResendNotice(false)
    setResending(true)
    try {
      await resendCode(phone)
      setSecondsLeft(RESEND_SECONDS)
      setResendNotice(true)
    } catch {
      setServerError(NETWORK_ERROR)
    } finally {
      setResending(false)
    }
  }

  function backToForm(focusFieldId: string) {
    setStep('form')
    setServerError(null)
    setFocusTargetId(focusFieldId)
  }

  const serverErrorBox = serverError && (
    <div role="alert" className="auth-inline-alert">
      <IconX aria-hidden="true" />
      <span>{serverError}</span>
    </div>
  )

  /* Done step */
  if (step === 'done') {
    return (
      <AuthLayout variant="code">
        <div key="done">
          <div className="reg-done-icon auth-rise" style={riseOrder(1)} aria-hidden="true">
            <IconCheck />
          </div>
          <h1 id={DONE_TITLE_ID} className="auth-title auth-rise" style={riseOrder(2)} tabIndex={-1}>
            Nömrə təsdiqləndi
          </h1>
          <div role="status" className="fb fb-ok reg-done-note auth-rise" style={riseOrder(3)}>
            Şirkətiniz qeydiyyatdan keçdi. İndi telefon nömrəniz və PIN-inizlə daxil ola bilərsiniz.
          </div>
          <div className="auth-rise" style={riseOrder(4)}>
            <button
              type="button"
              className="btn btn-primary btn-bl btn-lg"
              onClick={() => navigate('/login', { state: { identifier: '0' + phone } })}
            >
              Girişə keç
            </button>
          </div>
        </div>
      </AuthLayout>
    )
  }

  /* Code step */
  if (step === 'code') {
    return (
      <AuthLayout variant="code" sceneAlertKey={sceneAlertKey}>
        <form key="code" onSubmit={handleConfirm} noValidate aria-labelledby="register-code-title">
          <button
            type="button"
            className="auth-link auth-back auth-rise"
            style={riseOrder(1)}
            onClick={() => backToForm(FIELD_IDS.companyName)}
          >
            <IconArrowLeft aria-hidden="true" />
            Geri
          </button>
          <h1 id="register-code-title" className="auth-title auth-rise" style={riseOrder(2)}>
            Kodu daxil edin
          </h1>
          <p className="auth-sub auth-rise" style={riseOrder(3)}>
            <b>+994 {formatPhone(phone, 'international')}</b> nömrəsinə {CODE_LENGTH} rəqəmli kod göndərdik.
          </p>

          <div className="auth-field auth-rise" style={riseOrder(4)}>
            <label className="form-label" htmlFor={CODE_ID}>
              Təsdiq kodu
            </label>
            <div className={codeError ? `auth-digits ${shakeClass(codeShakes)}` : 'auth-digits'}>
              <OtpInput
                id={CODE_ID}
                length={CODE_LENGTH}
                value={code}
                onChange={(next) => {
                  setCode(next)
                  setCodeError(null)
                }}
                label="Kod"
                autoComplete="one-time-code"
                hasError={codeError !== null}
                errorId={CODE_ERROR_ID}
              />
            </div>
            <FieldError id={CODE_ERROR_ID} message={codeError} />
          </div>

          <div className="auth-submit auth-rise" style={riseOrder(5)}>
            {serverErrorBox}
            <SubmitButton busy={busy} label="Təsdiqlə" busyLabel="Yoxlanılır…" />
          </div>

          <p className="reg-resend auth-rise" style={riseOrder(6)}>
            Kod gəlmədi?{' '}
            <button
              type="button"
              className="auth-link"
              onClick={() => void handleResend()}
              disabled={secondsLeft > 0 || resending}
            >
              {resending ? 'Göndərilir…' : 'Yenidən göndər'}
              {secondsLeft > 0 && ` (${formatCountdown(secondsLeft)})`}
            </button>
          </p>
          <div role="status">
            {resendNotice && <div className="fb fb-info reg-resend-note">Yeni kod göndərildi.</div>}
          </div>

          <p className="auth-foot auth-rise" style={riseOrder(7)}>
            <button type="button" className="auth-link" onClick={() => backToForm(FIELD_IDS.phone)}>
              Nömrəni dəyiş
            </button>
          </p>
        </form>
      </AuthLayout>
    )
  }

  /* Form step */
  return (
    <AuthLayout variant="register">
      <form key="form" onSubmit={handleSendCode} noValidate aria-labelledby="register-title">
        <h1 id="register-title" className="auth-title auth-rise" style={riseOrder(1)}>
          Şirkəti qeydiyyatdan keçirin
        </h1>
        <p className="auth-sub auth-rise" style={riseOrder(2)}>
          Şirkət və rəhbər məlumatlarını yazın, telefon nömrənizi təsdiqləyin.
        </p>

        <TextField
          id={FIELD_IDS.companyName}
          label="Şirkət adı"
          autoComplete="organization"
          value={companyName}
          error={errors.companyName}
          boxClassName={fieldShake('companyName')}
          className="auth-rise"
          style={riseOrder(3)}
          onChange={(text) => {
            setCompanyName(text)
            clearError('companyName')
          }}
        />
        <div className="auth-row auth-rise" style={riseOrder(4)}>
          <TextField
            id={FIELD_IDS.firstName}
            label="Ad"
            autoComplete="given-name"
            value={firstName}
            error={errors.firstName}
            boxClassName={fieldShake('firstName')}
            onChange={(text) => {
              setFirstName(text)
              clearError('firstName')
            }}
          />
          <TextField
            id={FIELD_IDS.lastName}
            label="Soyad"
            autoComplete="family-name"
            value={lastName}
            error={errors.lastName}
            boxClassName={fieldShake('lastName')}
            onChange={(text) => {
              setLastName(text)
              clearError('lastName')
            }}
          />
        </div>

        <TextField
          id={FIELD_IDS.email}
          label="E-poçt"
          type="email"
          inputMode="email"
          autoComplete="email"
          placeholder="ad@sirket.az"
          value={email}
          error={errors.email}
          boxClassName={fieldShake('email')}
          className="auth-rise"
          style={riseOrder(5)}
          onBlur={checkEmailOnBlur}
          onChange={(text) => {
            setEmail(cleanEmail(text))
            clearError('email')
          }}
        />

        <div className="auth-field auth-rise" style={riseOrder(6)}>
          <label className="form-label" htmlFor={FIELD_IDS.phone}>
            Telefon nömrəsi
          </label>
          <PhoneInput
            id={FIELD_IDS.phone}
            variant="international"
            value={phone}
            error={errors.phone}
            boxClassName={fieldShake('phone')}
            onChange={(digits) => {
              setPhone(digits)
              clearError('phone')
            }}
          />
        </div>

        <PinField
          id={FIELD_IDS.pin}
          label="PIN (4 rəqəm)"
          screenReaderLabel="PIN"
          value={pin}
          error={errors.pin}
          boxClassName={fieldShake('pin')}
          style={riseOrder(7)}
          onChange={(next) => {
            setPin(next)
            clearError('pin')
          }}
        />
        <PinField
          id={FIELD_IDS.pinRepeat}
          label="PIN-i təkrarlayın"
          screenReaderLabel="PIN-in təkrarı"
          value={pinRepeat}
          error={errors.pinRepeat}
          boxClassName={fieldShake('pinRepeat')}
          style={riseOrder(8)}
          onChange={(next) => {
            setPinRepeat(next)
            clearError('pinRepeat')
          }}
        />

        <div className="auth-submit auth-rise" style={riseOrder(9)}>
          {serverErrorBox}
          <SubmitButton busy={busy} label="Kod göndər" busyLabel="Göndərilir…" />
        </div>

        <p className="reg-legal auth-rise" style={riseOrder(10)}>
          Davam etməklə{' '}
          <a href={LANDING_PRIVACY_URL} target="_blank" rel="noopener">
            Məxfilik siyasəti
          </a>{' '}
          ilə razılaşırsınız.
        </p>

        <p className="auth-foot auth-rise" style={riseOrder(11)}>
          Artıq hesabınız var? <Link to="/login">Daxil olun</Link>
        </p>
      </form>
    </AuthLayout>
  )
}

/* Submit button */
function SubmitButton({ busy, label, busyLabel }: { busy: boolean; label: string; busyLabel: string }) {
  return (
    <button type="submit" disabled={busy} className="btn btn-primary btn-bl btn-lg">
      {busy && <span className="auth-spinner" aria-hidden="true" />}
      {busy ? busyLabel : label}
    </button>
  )
}

/* Text field */
function TextField({
  id,
  label,
  value,
  onChange,
  onBlur,
  error,
  autoComplete,
  type = 'text',
  inputMode,
  placeholder,
  className = '',
  boxClassName,
  style,
}: {
  id: string
  label: string
  value: string
  onChange: (text: string) => void
  onBlur?: () => void
  error?: string
  autoComplete: string
  type?: 'text' | 'email'
  inputMode?: 'text' | 'email'
  placeholder?: string
  className?: string
  boxClassName?: string
  style?: CSSProperties
}) {
  const errorId = `${id}-error`
  return (
    <div className={`auth-field ${className}`} style={style}>
      <label className="form-label" htmlFor={id}>
        {label}
      </label>
      <div className={boxClassName}>
        <input
          id={id}
          className="inp"
          type={type}
          inputMode={inputMode}
          autoComplete={autoComplete}
          autoCapitalize={type === 'email' ? 'none' : undefined}
          spellCheck={type === 'email' ? false : undefined}
          placeholder={placeholder}
          value={value}
          onChange={(event) => onChange(event.target.value)}
          onBlur={onBlur}
          aria-required="true"
          aria-invalid={error ? true : undefined}
          aria-describedby={error ? errorId : undefined}
        />
      </div>
      <FieldError id={errorId} message={error} />
    </div>
  )
}

/* PIN field */
function PinField({
  id,
  label,
  screenReaderLabel,
  value,
  onChange,
  error,
  boxClassName = '',
  style,
}: {
  id: string
  label: string
  screenReaderLabel: string
  value: string
  onChange: (next: string) => void
  error?: string
  boxClassName?: string
  style?: CSSProperties
}) {
  const [visible, setVisible] = useState(false)
  const errorId = `${id}-error`
  return (
    <div className="auth-field auth-rise" style={style}>
      <label className="form-label" htmlFor={id}>
        {label}
      </label>
      <div className={`reg-pin-row ${boxClassName}`}>
        <div className="auth-digits reg-pin">
          <OtpInput
            id={id}
            length={PIN_LENGTH}
            value={value}
            onChange={onChange}
            masked={!visible}
            label={screenReaderLabel}
            autoComplete="new-password"
            hasError={error !== undefined}
            errorId={errorId}
          />
        </div>
        <PinEyeButton visible={visible} onToggle={() => setVisible((previous) => !previous)} />
      </div>
      <FieldError id={errorId} message={error} />
    </div>
  )
}
