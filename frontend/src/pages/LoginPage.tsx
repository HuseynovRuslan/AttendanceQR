import { useEffect, useRef, useState, type FormEvent } from 'react'
import { endedSessionNotice, forgetEndedSessionNotice } from '../api/client'
import { Link, useLocation, useNavigate } from 'react-router-dom'
import { login, appLogin } from '../api/auth'
import { isAppMode, isNativeApp } from '../lib/host'
import { useAuth } from '../auth/AuthContext'
import { decodeJwt, roleHome } from '../lib/jwt'
import { listProfiles } from '../lib/profiles'
import { cleanPhone, phoneError } from '../lib/phone'
import { isValidEmail } from '../lib/validation'
import { Avatar } from '../components/Avatar'
import { PinInput } from '../components/PinInput'
import { FieldError } from '../components/FieldError'
import { IdentifierInput, type IdentifierMode } from '../components/IdentifierInput'
import { IconQr, IconX } from '../components/icons'
import { AuthLayout } from '../components/auth/AuthLayout'
import { riseOrder } from '../components/auth/riseOrder'
import { shakeClass } from '../components/auth/shake'
import './LoginPage.css'

// Sahələrin id-ləri: <label htmlFor>, xəta mətni və fokus üçün.
const IDENTIFIER_ID = 'login-identifier'
const PIN_LABEL_ID = 'login-pin-label'
const PIN_ERROR_ID = 'login-pin-error'

export function LoginPage() {
  const location = useLocation()
  // Coming back from "PIN-i unutdum" with a freshly issued temporary PIN: that screen hands the number
  // back in the nav state (both directions now), so nobody has to retype a phone number from memory on
  // a keypad while holding a temp PIN in their head.
  // Gələn dəyərdə "@" varsa bu e-poçtdur — e-poçt seçimi ilə açırıq, yoxsa telefon seçimi ilə.
  const initialIdentifier = (location.state as { identifier?: string } | null)?.identifier ?? ''
  const startsWithEmail = initialIdentifier.includes('@')

  // State — komponentin yadda saxladığı və dəyişəndə ekranı yenidən çəkdirən dəyərdir (useState ilə yaradılır).
  // Telefon və e-poçt AYRI saxlanılır ki, "Telefon / E-poçt" seçimi dəyişəndə yazılan itməsin.
  const [mode, setMode] = useState<IdentifierMode>(startsWithEmail ? 'email' : 'phone')
  const [phone, setPhone] = useState(startsWithEmail ? '' : cleanPhone(initialIdentifier, 'local'))
  const [email, setEmail] = useState(startsWithEmail ? initialIdentifier : '')
  const [password, setPassword] = useState('')
  const [identifierError, setIdentifierError] = useState<string | null>(null)
  const [pinError, setPinError] = useState<string | null>(null)
  const [qrNoticeVisible, setQrNoticeVisible] = useState(false)
  // Why this screen is here, when a view session from the group board has just run out — said once.
  const [notice] = useState(endedSessionNotice)
  useEffect(() => { forgetEndedSessionNotice() }, [])
  const [loading, setLoading] = useState(false)

  /* Error feedback */
  const [formAlert, setFormAlert] = useState<string | null>(null)
  const [lockedUntil, setLockedUntil] = useState<number | null>(null)
  const [identifierShakes, setIdentifierShakes] = useState(0)
  const [pinShakes, setPinShakes] = useState(0)
  const [sceneAlertKey, setSceneAlertKey] = useState(0)
  const pinGroupRef = useRef<HTMLDivElement | null>(null)
  const { saveToken, switchProfile } = useAuth()
  // Accounts already on this device. On a personal phone this is empty (logout removes the profile it
  // ended) — on a crew phone it is the way back in without knowing anybody's PIN, which matters most
  // exactly here: the holder lands on this screen when a token is retired mid-shift, and the PINs are
  // the one thing they do not have.
  const saved = listProfiles()
  const navigate = useNavigate()

  // Serverə gedən identifikator: telefon seçilibsə boşluqsuz rəqəmlər, e-poçt seçilibsə boşluqsuz e-poçt.
  // Backend nömrənin son 9 rəqəmini müqayisə edir, ona görə "0501234567" köhnə yazılışlar kimi tapılır.
  const identifier = mode === 'phone' ? phone : email.trim()

  /** Sorğudan əvvəl yoxlama. Xəta varsa onu sahənin altına yazır və false qaytarır. */
  function validateBeforeSubmit(): boolean {
    let nextIdentifierError: string | null = null
    if (mode === 'phone') {
      nextIdentifierError = phoneError(phone, 'local')
    } else if (!isValidEmail(email)) {
      nextIdentifierError = 'E-poçtu tam yazın, məsələn: ad@sirket.az'
    }
    const nextPinError = password === '' ? 'PIN-i daxil edin' : null

    setIdentifierError(nextIdentifierError)
    setPinError(nextPinError)
    if (nextIdentifierError) setIdentifierShakes((count) => count + 1)
    if (nextPinError) setPinShakes((count) => count + 1)
    // İlk xətalı sahəyə fokus veririk ki, istifadəçi (və ekran oxuyucu) xətanı dərhal görsün.
    if (nextIdentifierError) document.getElementById(IDENTIFIER_ID)?.focus()
    else if (nextPinError) focusFirstPinBox()
    return nextIdentifierError === null && nextPinError === null
  }

  function focusFirstPinBox() {
    pinGroupRef.current?.querySelector('input')?.focus()
  }

  /* Wrong phone or PIN */
  function showWrongCredentials(message: string) {
    setPassword('')
    setPinError(message)
    setPinShakes((count) => count + 1)
    setSceneAlertKey((count) => count + 1)
    focusFirstPinBox()
  }

  /* Rate limit lock */
  useEffect(() => {
    if (lockedUntil === null) return
    const timerId = window.setTimeout(() => {
      setLockedUntil(null)
      setFormAlert(null)
    }, lockedUntil - Date.now())
    return () => window.clearTimeout(timerId)
  }, [lockedUntil])

  function changeMode(nextMode: IdentifierMode) {
    setMode(nextMode)
    setIdentifierError(null)
  }

  function changePhone(digits: string) {
    setPhone(digits)
    setIdentifierError(null)
  }

  function changeEmail(text: string) {
    setEmail(text)
    setIdentifierError(null)
  }

  function changePin(next: string) {
    setPassword(next)
    setPinError(null)
  }

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    if (lockedUntil !== null) return
    setFormAlert(null)
    // Yoxlama keçməsə sorğu getmir. loading hələ true edilməyib, ona görə düymə "Yoxlanılır…"-da ilişmir.
    if (!validateBeforeSubmit()) return
    setLoading(true)
    try {
      // The native app (no company subdomain) resolves the tenant from the credentials; the web uses
      // its subdomain. Same request/response shape either way.
      const { status, data } = await (isAppMode() ? appLogin : login)(identifier, password)
      if (status === 200 && data && 'token' in data) {
        saveToken(data.token)
        const from = (location.state as { from?: string } | null)?.from
        const role = decodeJwt(data.token)?.role
        navigate(from ?? roleHome(role), { replace: true })
      } else if (status === 429) {
        const m = data && 'minutes' in data && data.minutes ? data.minutes : 5
        setFormAlert(`Çox sayda cəhd — ${m} dəqiqə sonra yenidən cəhd edin`)
        setLockedUntil(Date.now() + m * 60_000)
      } else {
        // Warn before the cool-off: show the count only when it's getting close.
        const rem = data && 'remaining' in data ? data.remaining : undefined
        showWrongCredentials(
          rem !== undefined && rem <= 3
            ? `Yanlış PIN — ${rem} cəhdiniz qaldı`
            : 'Nömrə və ya PIN yanlışdır',
        )
      }
    } catch {
      setFormAlert('Serverə qoşulmaq mümkün olmadı')
    } finally {
      setLoading(false)
    }
  }

  function openForgotPin() {
    // Forgot-PIN: no dead end. The dedicated flow verifies by face from the bound device and hands out a
    // PIN on the spot, falling back to an admin request if it can't. Yazılmış nömrə/e-poçt da ötürülür.
    navigate('/forgot-pin', { state: { identifier } })
  }

  return (
    <AuthLayout variant="login" sceneAlertKey={sceneAlertKey}>
      <h1 id="login-title" className="auth-title auth-rise" style={riseOrder(1)}>
        Sistemə daxil olun
      </h1>

      {saved.length > 0 && (
        <div className="login-profiles auth-rise" style={riseOrder(1.5)}>
          <div className="login-profiles-title">Bu telefondakı hesablar</div>
          {saved.map((p) => (
            <button
              key={p.employeeId}
              type="button"
              className="login-profile"
              onClick={() => switchProfile(p.employeeId)}
            >
              <Avatar employeeId={p.employeeId} name={p.name} size={36} />
              <span>{p.name}</span>
            </button>
          ))}
        </div>
      )}

      <p className="auth-sub auth-rise" style={riseOrder(2)}>
        Nömrənizi və 4 rəqəmli PIN-inizi yazın
      </p>

      {/* Baxış sessiyası bitib bu ekrana qaytarılıbsa, səbəbi bir dəfə deyirik. Xəta çıxanda gizlənir. */}
      {notice && !formAlert && !pinError && (
        <div className="fb fb-info auth-rise" style={{ ...riseOrder(2.5), marginBottom: 14 }}>
          <span>{notice}</span>
        </div>
      )}

      {/* noValidate: brauzerin öz "Bu sahəni doldurun" balonları söndürülür — xətaları biz sahənin
          altında, Azərbaycan dilində göstəririk. */}
      <form onSubmit={onSubmit} noValidate aria-labelledby="login-title">
        {/* The endpoint resolves the identifier as a phone number OR an email, so the admins who
            sign in with an email keep working. Telefon əsas seçimdir — işçilərin çoxu nömrə ilə
            girir; e-poçt ikinci düymədədir. */}
        <IdentifierInput
          id={IDENTIFIER_ID}
          mode={mode}
          onModeChange={changeMode}
          phone={phone}
          onPhoneChange={changePhone}
          email={email}
          onEmailChange={changeEmail}
          error={identifierError}
          switchClassName="auth-rise"
          switchStyle={riseOrder(3)}
          fieldClassName="auth-field auth-rise"
          fieldStyle={riseOrder(4)}
          boxClassName={identifierError ? shakeClass(identifierShakes) : undefined}
        />

        {/* Four boxes rather than one field, because nothing on the old screen said how much was
            expected and people typed their phone number in again. See PinInput for the escape
            hatch and why the eye toggle is not optional here. */}
        <div className="auth-field auth-rise" style={riseOrder(5)}>
          <label id={PIN_LABEL_ID} className="form-label">
            PIN (4 rəqəm)
          </label>
          {/* PinInput-un özünə toxunmuruq, ona görə başlığı və xətanı onu əhatə edən qrupa bağlayırıq. */}
          <div
            ref={pinGroupRef}
            role="group"
            className={pinError ? `auth-digits is-invalid ${shakeClass(pinShakes)}` : 'auth-digits'}
            aria-labelledby={PIN_LABEL_ID}
            aria-describedby={pinError ? PIN_ERROR_ID : undefined}
          >
            <PinInput value={password} onChange={changePin} />
          </div>
          <FieldError id={PIN_ERROR_ID} message={pinError} />
        </div>

        <div className="auth-submit auth-rise" style={riseOrder(6)}>
          {formAlert && (
            <div role="alert" className="auth-inline-alert">
              <IconX aria-hidden="true" />
              <span>{formAlert}</span>
            </div>
          )}
          <button type="submit" disabled={loading || lockedUntil !== null} className="btn btn-primary btn-bl btn-lg">
            {loading ? 'Yoxlanılır…' : 'Daxil ol'}
          </button>

          {/* QR ilə giriş hələ hazır deyil: düymə heç bir sorğu göndərmir, yalnız xəbərdarlıq göstərir.
              Android tətbiqində göstərmirik. */}
          {!isNativeApp() && (
            <>
              <div className="login-divider">və ya</div>
              <button
                type="button"
                className="btn btn-outline btn-bl btn-lg"
                onClick={() => setQrNoticeVisible(true)}
              >
                <IconQr aria-hidden="true" />
                QR ilə daxil ol
              </button>
            </>
          )}
          {/* role="status" — mətn dəyişəndə ekran oxuyucu onu səsləndirir. Element həmişə yerindədir,
              içi isə yalnız düymə basılandan sonra dolur. */}
          <div role="status">
            {qrNoticeVisible && <div className="fb fb-info login-notice">QR ilə giriş tezliklə əlavə olunacaq</div>}
          </div>
        </div>

        <div className="auth-center auth-rise" style={riseOrder(7)}>
          <button type="button" className="auth-link" onClick={openForgotPin}>
            PIN-i unutdum?
          </button>
        </div>

        {/* Şirkət qeydiyyatı yalnız brauzerdədir. Android tətbiqi işçilər üçündür — hesabı şirkət açır. */}
        <p className="auth-foot auth-rise" style={riseOrder(8)}>
          {isNativeApp() ? (
            'Hesabınız yoxdursa, şirkətinizin rəhbərliyi ilə əlaqə saxlayın'
          ) : (
            <>
              {/* Backend hələ şirkət qeydiyyatını dəstəkləmir, ona görə linki gizlətdik. Qeydiyyat üçün şirkət rəhbərliyi ilə əlaqə saxlayın. */}
              {/* Hesabınız yoxdur? <Link to="/register">Şirkəti qeydiyyatdan keçirin</Link> */}
              {console.log('Backend hələ şirkət qeydiyyatını dəstəkləmir, ona görə linki gizlətdik. Qeydiyyat üçün bizimlə əlaqə saxlayın.')}
            </>
          )}
        </p>
      </form>
    </AuthLayout>
  )
}
