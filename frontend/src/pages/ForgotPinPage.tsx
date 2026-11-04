import { useEffect, useRef, useState } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import { forgotPin, forgotPinCheck, forgotPinVerify } from '../api/auth'
import { getDeviceFingerprint } from '../lib/device'
import { platform } from '../lib/geo'
import { cleanPhone, formatPhone, phoneError } from '../lib/phone'
import { isValidEmail } from '../lib/validation'
import { IdentifierInput, type IdentifierMode } from '../components/IdentifierInput'
import { AuthLayout } from '../components/auth/AuthLayout'
import { riseOrder } from '../components/auth/riseOrder'
import { shakeClass } from '../components/auth/shake'
import { IconCamera, IconCameraOff, IconCheck, IconSun, IconX } from '../components/icons'
import { classifyAdminRequest, classifyVerify, NETWORK_MESSAGE } from './forgotPinOutcome'
import './ForgotPinPage.css'

const IDENTIFIER_ID = 'forgot-pin-identifier'

/**
 * Self-service "PIN-i unutdum". Two factors, no admin: a selfie matched to the reference photo, from a
 * device already bound to the account. On success the new temporary PIN is shown on the spot. If it
 * can't verify (different phone, camera off, face doesn't match), it falls back to filing a request in
 * the admin queue — so nobody is left stuck either way.
 *
 * The rule this screen lives by: EVERY state must offer a way forward. It is reached only by people who
 * are already locked out, usually on a building site with one bar of signal, and it runs as an installed
 * PWA — there is no browser back button. A spinner with no buttons, a button whose failure is invisible,
 * and a button that repaints the same screen are all dead ends with no exit at all.
 */
type Phase = 'ask' | 'camera' | 'checking' | 'success' | 'failed' | 'unreachable' | 'requested'

/**
 * "Allow the camera" without a path is an instruction this cohort cannot carry out. Same word and the
 * same route the rest of the employee app uses (CameraHelp, GpsHelp, PushEnablePrompt): «Ayarlar»,
 * never «tənzimləmələr» — that is the admin panel's own nav label, not a menu on anybody's phone.
 */
function cameraPermissionPath(): string {
  switch (platform()) {
    case 'ios':
      return 'Ünvan sətrindəki «ᴀA» işarəsinə basın → «Vebsayt Ayarları» → «Kamera» → «İcazə ver». Alınmasa: Ayarlar → Safari → Kamera → «İcazə ver».'
    case 'android':
      return 'Ünvan sətrinin solundakı 🔒 işarəsinə basın → «İcazələr» → «Kamera» → «İcazə ver». Alınmasa: Ayarlar → Tətbiqlər → Chrome → İcazələr → Kamera → «İcazə ver».'
    default:
      return 'Ünvan sətrindəki 🔒 işarəsinə basın → «Kamera» → «İcazə ver».'
  }
}

export function ForgotPinPage() {
  const navigate = useNavigate()
  const location = useLocation()
  const prefilled = (location.state as { identifier?: string } | null)?.identifier ?? ''
  const prefilledIsEmail = prefilled.includes('@')

  const [phase, setPhase] = useState<Phase>('ask')
  // Server (forgot-pin, check, verify) hesabı login kimi telefon VƏ YA e-poçtla tapır, ona görə hər ikisi
  // mümkündür. Dəyərlər ayrı saxlanılır ki, seçim dəyişəndə yazılan itməsin.
  const [mode, setMode] = useState<IdentifierMode>(prefilledIsEmail ? 'email' : 'phone')
  const [phone, setPhone] = useState(prefilledIsEmail ? '' : cleanPhone(prefilled, 'local'))
  const [email, setEmail] = useState(prefilledIsEmail ? prefilled : '')
  const [identifierError, setIdentifierError] = useState<string | null>(null)
  // Serverə gedən dəyər: telefon seçilibsə boşluqsuz rəqəmlər, e-poçt seçilibsə e-poçt.
  const identifier = mode === 'phone' ? phone : email.trim()
  const [pin, setPin] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  /** Why we could not get an answer — shown on the 'unreachable' screen. */
  const [trouble, setTrouble] = useState<string>(NETWORK_MESSAGE)

  const videoRef = useRef<HTMLVideoElement | null>(null)
  const streamRef = useRef<MediaStream | null>(null)
  const [camReady, setCamReady] = useState(false)
  const [camError, setCamError] = useState(false)
  const [camBusy, setCamBusy] = useState(false)
  /* Photo shown while checking */
  const [snapshot, setSnapshot] = useState<string | null>(null)

  /* Error feedback */
  const [identifierShakes, setIdentifierShakes] = useState(0)
  const [sceneAlertKey, setSceneAlertKey] = useState(0)
  useEffect(() => {
    if (phase === 'failed') setSceneAlertKey((count) => count + 1)
  }, [phase])
  /** Has the camera already failed once? A second identical failure needs different words. */
  const camFailedOnceRef = useRef(false)

  useEffect(() => {
    if (phase !== 'camera') return
    void startCamera()
    return () => stopCamera()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [phase])

  async function startCamera() {
    // Re-entrant now: this runs again when the employee taps "Kameranı yenidən aç" after allowing the
    // permission, so drop any stream we already hold instead of leaking its tracks (the camera light
    // would stay on, and a second getUserMedia can fail on a device that thinks it is still busy).
    stopCamera()
    setCamError(false)
    setError(null)
    if (!navigator.mediaDevices?.getUserMedia) {
      // There is no camera API here at all: an in-app browser, a page not served over https, or a
      // device with no front camera. Re-opening cannot ever work, so say so — otherwise the tap
      // repaints a pixel-identical screen and the button reads as broken.
      setCamError(true)
      setError('Bu telefonda kamera açılmır. Rəhbərinizə sorğu göndərin.')
      return
    }
    setCamBusy(true)
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: 'user' }, audio: false })
      streamRef.current = stream
      const v = videoRef.current
      if (v) {
        v.srcObject = stream
        await v.play().catch(() => {})
      }
      setCamReady(true)
      camFailedOnceRef.current = false
    } catch {
      setCamError(true)
      setCamReady(false)
      // Repeating "allow it and press the button again" to somebody who has just done exactly that is
      // the same dead end in politer words. From the second failure on, point at the route that works.
      if (camFailedOnceRef.current) setError('Hələ də alınmır. Rəhbərinizə sorğu göndərin.')
      camFailedOnceRef.current = true
    } finally {
      setCamBusy(false)
    }
  }

  function stopCamera() {
    streamRef.current?.getTracks().forEach((t) => t.stop())
    streamRef.current = null
    const v = videoRef.current
    if (v) v.srcObject = null
    setCamReady(false)
  }

  async function startFlow() {
    // Sorğudan əvvəl yoxlama: xəta sahənin altında görünür və sorğu getmir.
    let problem: string | null = null
    if (mode === 'phone') {
      problem = phoneError(phone, 'local')
    } else if (!isValidEmail(email)) {
      problem = 'E-poçtu tam yazın, məsələn: ad@sirket.az'
    }
    if (problem) {
      setIdentifierError(problem)
      setIdentifierShakes((count) => count + 1)
      document.getElementById(IDENTIFIER_ID)?.focus()
      return
    }
    setError(null)
    setBusy(true)
    try {
      // Ask first whether the number is known here. Without this the camera opened for everybody and
      // every failure read the same, so a typo looked exactly like "we did not recognise your face" —
      // people retook the selfie again and again for a wrong digit.
      const { status, data } = await forgotPinCheck(identifier.trim())
      if (status === 200 && data && 'known' in data && data.known === false) {
        setError(
          mode === 'phone'
            ? 'Bu nömrə sistemdə yoxdur. Nömrəni yoxlayın və ya rəhbərinizə deyin.'
            : 'Bu e-poçt sistemdə yoxdur. E-poçtu yoxlayın və ya rəhbərinizə deyin.',
        )
        return
      }
      // Anything else — a server hiccup, a rate limit — is not a reason to block a locked-out person:
      // carry on to the camera, where a real account still verifies.
      setPhase('camera')
    } catch {
      setPhase('camera')
    } finally {
      setBusy(false)
    }
  }

  function retryPhoto() {
    setError(null)
    setSnapshot(null)
    setPhase('camera')
  }

  async function captureAndVerify() {
    const v = videoRef.current
    if (!v || v.videoWidth === 0) {
      // A tap must never do nothing: the frame is not ready yet (or the video element lost its
      // stream). Without this the button was simply silent.
      setError('Kamera hələ hazır deyil. Bir neçə saniyə gözləyin və düyməni yenidən basın.')
      return
    }
    const w = Math.min(640, v.videoWidth)
    const scale = w / v.videoWidth
    const canvas = document.createElement('canvas')
    canvas.width = w
    canvas.height = Math.round(v.videoHeight * scale)
    const ctx = canvas.getContext('2d')
    if (!ctx) {
      setError('Şəkil çəkmək alınmadı. Yenidən cəhd edin.')
      return
    }
    ctx.drawImage(v, 0, 0, canvas.width, canvas.height)
    const photo = canvas.toDataURL('image/jpeg', 0.8)
    setSnapshot(photo)
    stopCamera()
    setError(null)
    setPhase('checking')

    // Having no try/catch here used to freeze the screen on "Yoxlanılır…" forever on any dropped
    // request — the likeliest outcome on a site with one bar of signal, and the one with no way out.
    // The other half of that fix lives in api/auth.ts: forgotPinVerify carries a timeoutMs, so a
    // connection that opens and then stalls ALSO ends up here instead of hanging for ever.
    let outcome
    try {
      const { status, data } = await forgotPinVerify(identifier.trim(), getDeviceFingerprint(), photo)
      outcome = classifyVerify(status, data)
    } catch {
      outcome = { kind: 'unreachable' as const, message: NETWORK_MESSAGE }
    }

    if (outcome.kind === 'verified') {
      setPin(outcome.pin)
      setPhase('success')
    } else if (outcome.kind === 'rejected') {
      setPhase('failed')
    } else {
      setTrouble(outcome.message)
      setPhase('unreachable')
    }
  }

  // Fallback when face+device can't verify: file a request for the branch manager (an admin where the
  // branch has none) to reset by hand. They are pushed about it — see StaffAlertKinds.PinResetRequested.
  async function sendAdminRequest() {
    setBusy(true)
    setError(null)
    try {
      const { status, data } = await forgotPin(identifier.trim())
      // apiRequest does not throw on a 500, so this screen used to be shown when the server had
      // refused the call outright and the employee waited for a reset that was never coming.
      const outcome = classifyAdminRequest(status, data)
      if (outcome.kind === 'accepted') setPhase('requested')
      else setError(outcome.message)
    } catch {
      setError(NETWORK_MESSAGE)
    } finally {
      setBusy(false)
    }
  }


  /* Shared pieces */
  const backLink = (
    <div className="auth-center auth-rise" style={riseOrder(7)}>
      <button type="button" className="auth-link" onClick={() => navigate('/login')}>
        Girişə qayıt
      </button>
    </div>
  )
  const errorNote = error ? (
    <div role="alert" className="auth-inline-alert">
      <IconX aria-hidden="true" />
      <span>{error}</span>
    </div>
  ) : null
  const adminButton = (
    <button type="button" onClick={() => void sendAdminRequest()} disabled={busy} className="btn btn-outline btn-bl btn-lg fp-second">
      {busy ? 'Göndərilir…' : 'Rəhbərə sorğu göndər'}
    </button>
  )

  /* Phone step */
  if (phase === 'ask') {
    return (
      <AuthLayout variant="pin" sceneAlertKey={sceneAlertKey}>
        <div key="ask">
          <StepIndicator step={1} />
          <h1 id="forgot-pin-title" className="auth-title auth-rise" style={riseOrder(2)}>
            PIN-i unutdum
          </h1>
          <p className="auth-sub auth-rise" style={riseOrder(3)}>
            Telefon nömrənizi yazın, sonra üzünüzü təsdiqləyin — yeni PIN dərhal veriləcək.
          </p>
          <form
            noValidate
            aria-labelledby="forgot-pin-title"
            onSubmit={(event) => {
              event.preventDefault()
              void startFlow()
            }}
          >
            <IdentifierInput
              id={IDENTIFIER_ID}
              mode={mode}
              onModeChange={(nextMode) => {
                setMode(nextMode)
                setIdentifierError(null)
              }}
              phone={phone}
              onPhoneChange={(digits) => {
                setPhone(digits)
                setIdentifierError(null)
              }}
              email={email}
              onEmailChange={(text) => {
                setEmail(text)
                setIdentifierError(null)
              }}
              error={identifierError}
              switchClassName="auth-rise"
              switchStyle={riseOrder(4)}
              fieldClassName="auth-field auth-rise"
              fieldStyle={riseOrder(5)}
              boxClassName={identifierError ? shakeClass(identifierShakes) : undefined}
            />
            <div className="auth-submit auth-rise" style={riseOrder(6)}>
              {errorNote}
              <button type="submit" disabled={busy} className="btn btn-primary btn-bl btn-lg">
                {busy && <span className="auth-spinner" aria-hidden="true" />}
                {busy ? 'Yoxlanılır…' : 'Davam et'}
              </button>
            </div>
          </form>
          {backLink}
        </div>
      </AuthLayout>
    )
  }

  /* Admin request sent */
  if (phase === 'requested') {
    return (
      <AuthLayout variant="pin" sceneAlertKey={sceneAlertKey}>
        <div key="requested">
          <StepIndicator step={2} />
          <h1 className="auth-title auth-rise" style={riseOrder(2)}>
            Sorğu göndərildi
          </h1>
          {/* The server answers the same 200 whether it filed the request, did not know the number, or
              hit its per-IP limit — so this screen must not promise that an admin will act on it. */}
          <div className="auth-rise" style={riseOrder(3)}>
            <div role="status" className="fb fb-info fp-gap">
              <span>
                {mode === 'phone' ? 'Nömrəniz' : 'E-poçtunuz'}:{' '}
                <b>{mode === 'phone' ? formatPhone(phone, 'local') : email.trim()}</b>
              </span>
            </div>
            <p className="fp-text">
              {mode === 'phone' ? 'Nömrə' : 'E-poçt'} səhvdirsə, geri qayıdıb düzgün yazın və yenidən göndərin.
            </p>
            <p className="fp-text">
              {mode === 'phone' ? 'Nömrə' : 'E-poçt'} düzgündürsə, rəhbərinizə bildiriş gedir: o, PIN-inizi
              sıfırlayıb sizə yeni müvəqqəti PIN deyəcək. Cavab gəlməsə, ona birbaşa yaxınlaşın.
            </p>
          </div>
          <div className="auth-submit auth-rise" style={riseOrder(4)}>
            <button
              type="button"
              className="btn btn-outline btn-bl btn-lg"
              onClick={() => {
                setError(null)
                setPhase('ask')
              }}
            >
              {mode === 'phone' ? 'Nömrəni dəyiş' : 'E-poçtu dəyiş'}
            </button>
          </div>
          {backLink}
        </div>
      </AuthLayout>
    )
  }

  /* Face step */
  const look = cameraLook(phase, camError)

  return (
    <AuthLayout variant="pin" sceneAlertKey={sceneAlertKey}>
      <div key="face">
        <StepIndicator step={2} />
        <h1 className="auth-title auth-rise" style={riseOrder(2)}>
          Üzünüzü təsdiqləyin
        </h1>
        <p className="auth-sub auth-rise" style={riseOrder(3)}>
          Üzünüzü ovalın içində saxlayın. Bu, hesabın sizə aid olduğunu təsdiqləyir.
        </p>

        <div className="auth-rise" style={riseOrder(4)}>
          <div className={`fp-camera fp-camera--${look}`}>
            {phase === 'camera' && (
              <video
                ref={videoRef}
                className="fp-camera__media"
                playsInline
                muted
                autoPlay
                hidden={camError}
              />
            )}
            {phase !== 'camera' && snapshot && <img className="fp-camera__media" src={snapshot} alt="" />}
            {look === 'blocked' ? (
              <div className="fp-camera__blocked" aria-hidden="true">
                <IconCameraOff />
              </div>
            ) : (
              <FaceGuide look={look} />
            )}
          </div>
        </div>

        <div className="auth-rise" style={riseOrder(5)}>
          <div role="status">
            {phase === 'checking' && <p className="fp-text fp-gap">Üzünüz yoxlanılır, bir neçə saniyə gözləyin.</p>}
            {phase === 'success' && pin && (
              <div className="fb fb-ok fp-result fp-gap">
                <IconCheck aria-hidden="true" />
                <div>
                  <b className="fp-result__title">Üzünüz təsdiqləndi.</b>
                  <div className="fp-pin">
                    <span className="fp-pin__label">Yeni müvəqqəti PIN-iniz</span>
                    <span className="fp-pin__value">{pin}</span>
                  </div>
                  <span>
                    Bu PIN-lə daxil olun — sonra sistem sizdən <b>öz PIN-inizi</b> təyin etməyi istəyəcək.
                  </span>
                </div>
              </div>
            )}
          </div>

          {phase === 'camera' && !camError && (
            <p className="fp-tip">
              <IconSun aria-hidden="true" />
              <span>Üzünüz işıqlı olsun, işıq arxadan düşməsin. Eynək və papaqsız çəkin.</span>
            </p>
          )}
          {phase === 'camera' && camError && (
            <div role="alert" className="fb fp-warn fp-gap">
              <div>
                <p>
                  Kameraya icazə verilməyib. Brauzerin ünvan sətrindəki kamera ikonuna basıb icazə verin, sonra
                  səhifəni yeniləyin.
                </p>
                {platform() !== 'other' && <p className="fp-warn__path">{cameraPermissionPath()}</p>}
              </div>
            </div>
          )}
          {phase === 'failed' && (
            <div role="alert" className="auth-inline-alert">
              <IconX aria-hidden="true" />
              <div>
                <p>Üz uyğun gəlmədi. İşıqlı yerdə, üzünüzü ovalın içində saxlayıb yenidən çəkin.</p>
                {/* The server deliberately does not say which factor failed. A new phone is the usual
                    reason to be here, and a new phone can never pass — so the admin request stays one tap away. */}
                <p className="fp-err__hint">
                  Ola bilsin ki, bu telefon hesabınıza bağlı deyil — məsələn, yeni telefon işlədirsiniz. Belə halda
                  rəhbərinizə sorğu göndərin.
                </p>
              </div>
            </div>
          )}
          {phase === 'unreachable' && (
            <div role="alert" className="auth-inline-alert">
              <IconX aria-hidden="true" />
              <span>
                Yoxlama başa çatmadı. {trouble} Düzəlməsə, rəhbərinizə sorğu göndərin — PIN-inizi o sıfırlayacaq.
              </span>
            </div>
          )}
          {errorNote}
        </div>

        <div className="auth-rise" style={riseOrder(6)}>
          {phase === 'camera' && !camError && (
            <>
              <button
                type="button"
                onClick={() => void captureAndVerify()}
                disabled={!camReady}
                className="btn btn-primary btn-bl btn-lg"
              >
                <IconCamera aria-hidden="true" />
                Şəkil çək və yoxla
              </button>
              {error && adminButton}
            </>
          )}
          {phase === 'camera' && camError && (
            <>
              <button type="button" onClick={() => void startCamera()} disabled={camBusy} className="btn btn-primary btn-bl btn-lg">
                {camBusy && <span className="auth-spinner" aria-hidden="true" />}
                {camBusy ? 'Açılır…' : 'Yenidən yoxla'}
              </button>
              {adminButton}
            </>
          )}
          {phase === 'checking' && (
            <button type="button" disabled className="btn btn-primary btn-bl btn-lg">
              <span className="auth-spinner" aria-hidden="true" />
              Yoxlanılır…
            </button>
          )}
          {phase === 'success' && (
            <button
              type="button"
              onClick={() => navigate('/login', { state: { identifier: identifier.trim() } })}
              className="btn btn-primary btn-bl btn-lg"
            >
              Girişə keç
            </button>
          )}
          {phase === 'failed' && (
            <>
              <button type="button" onClick={retryPhoto} className="btn btn-primary btn-bl btn-lg">
                <IconCamera aria-hidden="true" />
                Yenidən çək
              </button>
              {adminButton}
            </>
          )}
          {phase === 'unreachable' && (
            <>
              <button type="button" onClick={retryPhoto} className="btn btn-primary btn-bl btn-lg">
                Yenidən cəhd et
              </button>
              {adminButton}
            </>
          )}
        </div>

        {phase !== 'success' && backLink}
      </div>
    </AuthLayout>
  )
}

/* Camera states */
type CameraLook = 'ready' | 'checking' | 'success' | 'nomatch' | 'blocked'

function cameraLook(phase: Phase, camError: boolean): CameraLook {
  if (phase === 'camera') return camError ? 'blocked' : 'ready'
  if (phase === 'checking') return 'checking'
  if (phase === 'success') return 'success'
  if (phase === 'failed') return 'nomatch'
  return 'ready'
}

/* Step indicator */
function StepIndicator({ step }: { step: 1 | 2 }) {
  return (
    <div className="fp-steps auth-rise" style={riseOrder(1)}>
      <span className="fp-steps__bar is-on" aria-hidden="true" />
      <span className={step === 2 ? 'fp-steps__bar is-on' : 'fp-steps__bar'} aria-hidden="true" />
      <span className="fp-steps__text">Addım {step} / 2</span>
    </div>
  )
}

/* Face guide */
function FaceGuide({ look }: { look: CameraLook }) {
  return (
    <>
      <svg className="fp-guide" viewBox="0 0 100 100" preserveAspectRatio="none" aria-hidden="true">
        <ellipse className="fp-guide__oval" cx="50" cy="45" rx="20" ry="33" vectorEffect="non-scaling-stroke" />
        <path
          className="fp-guide__corners"
          d="M24 16V7h7M69 7h7v9M76 74v9h-7M31 83h-7v-9"
          vectorEffect="non-scaling-stroke"
        />
      </svg>
      {look === 'checking' && (
        <div className="fp-scan-track" aria-hidden="true">
          <div className="fp-scan" />
        </div>
      )}
      {look === 'success' && (
        <div className="fp-badge fp-badge--ok" aria-hidden="true">
          <IconCheck />
        </div>
      )}
      {look === 'nomatch' && (
        <div className="fp-badge fp-badge--bad" aria-hidden="true">
          <IconX />
        </div>
      )}
    </>
  )
}
