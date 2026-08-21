import { useEffect, useRef, useState } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import { forgotPin, forgotPinVerify } from '../api/auth'
import { getDeviceFingerprint } from '../lib/device'
import { platform } from '../lib/geo'
import { classifyAdminRequest, classifyVerify, NETWORK_MESSAGE } from './forgotPinOutcome'

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

  const [phase, setPhase] = useState<Phase>('ask')
  const [identifier, setIdentifier] = useState(prefilled)
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
      setError('Bu telefonda kamera açılmır. Administratora sorğu göndərin.')
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
      if (camFailedOnceRef.current) setError('Hələ də alınmır. Administratora sorğu göndərin.')
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

  function startFlow() {
    if (!identifier.trim()) {
      setError('Telefon nömrəsi və ya email daxil edin')
      return
    }
    setError(null)
    setPhase('camera')
  }

  function retryPhoto() {
    setError(null)
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

  // Fallback when face+device can't verify: file a request for the admin to reset by hand.
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

  const card = 'w-full max-w-sm rounded-3xl border border-slate-100 bg-white p-6 shadow-sm'
  const wrap = 'flex min-h-screen flex-col items-center justify-center bg-slate-50 p-4 text-slate-900'
  const primaryBtn = 'mt-3 w-full rounded-2xl bg-blue-600 py-3 font-bold text-white disabled:opacity-50'
  const quietBtn = 'mt-2 w-full rounded-2xl bg-slate-100 py-3 font-semibold text-slate-700 disabled:opacity-50'
  const backLink = (
    <button onClick={() => navigate('/login')} className="mt-4 w-full text-sm font-semibold text-slate-500 underline underline-offset-2">
      Girişə qayıt
    </button>
  )
  // Every branch that has a button also shows what went wrong with it — a button that reports nothing
  // reads as a broken app.
  const errorNote = error ? <p className="mt-2 text-center text-sm text-red-600">{error}</p> : null
  const adminButton = (className: string) => (
    <button onClick={() => void sendAdminRequest()} disabled={busy} className={className}>
      {busy ? 'Göndərilir…' : 'Administratora sorğu göndər'}
    </button>
  )

  if (phase === 'success' && pin) {
    return (
      <div className={wrap}>
        <div className={card}>
          <div className="text-center text-5xl">✅</div>
          <h1 className="mt-2 text-center text-xl font-bold">Sizi tanıdıq</h1>
          <div className="mt-4 rounded-2xl bg-green-50 p-5 text-center">
            <div className="text-sm font-semibold text-slate-500">Yeni müvəqqəti PIN-iniz</div>
            <div className="mt-1 text-4xl font-extrabold tracking-[0.3em] text-green-700">{pin}</div>
          </div>
          <p className="mt-4 text-center text-sm text-slate-600">
            Bu PIN-lə daxil olun — sonra sistem sizdən <b>öz PIN-inizi</b> təyin etməyi istəyəcək.
          </p>
          <button
            onClick={() => navigate('/login', { state: { identifier: identifier.trim() } })}
            className="mt-5 w-full rounded-2xl bg-blue-600 py-4 text-lg font-bold text-white"
          >
            Girişə keç
          </button>
        </div>
      </div>
    )
  }

  if (phase === 'requested') {
    return (
      <div className={wrap}>
        <div className={card}>
          <div className="text-center text-5xl">📨</div>
          <h1 className="mt-2 text-center text-xl font-bold">Sorğu göndərildi</h1>
          {/* The server answers the same 200 whether it filed the request, did not know the number, or
              hit its per-IP limit — so this screen must not promise that an admin will act on it. It
              shows the number that was sent (a typo on a phone keypad is the likeliest input error
              here) and names a route that works either way. */}
          <p className="mt-3 text-center text-sm text-slate-600">
            Nömrəniz: <b>{identifier.trim()}</b>
          </p>
          <p className="mt-2 text-center text-sm text-slate-600">
            Nömrə səhvdirsə, geri qayıdıb düzgün yazın və yenidən göndərin.
          </p>
          <p className="mt-2 text-center text-sm text-slate-600">
            Administratorunuz PIN-inizi sıfırlayıb sizə yeni müvəqqəti PIN verəcək. Bu gün cavab
            gəlməsə, rəhbərinizə və ya administratora birbaşa deyin.
          </p>
          <button
            onClick={() => {
              setError(null)
              setPhase('ask')
            }}
            className="mt-5 w-full rounded-2xl bg-slate-100 py-3 font-semibold text-slate-700"
          >
            Nömrəni dəyiş
          </button>
          {backLink}
        </div>
      </div>
    )
  }

  if (phase === 'failed') {
    return (
      <div className={wrap}>
        <div className={card}>
          <div className="text-center text-5xl">🙁</div>
          <h1 className="mt-2 text-center text-xl font-bold">Sizi tanıya bilmədik</h1>
          {/* The server deliberately does not say which factor failed, so neither do we. But the common
              case is the device gate — a new phone is the usual reason to be on this screen at all — and
              a new phone can NEVER pass, however many selfies are taken. So the admin request leads and
              the retry is the quiet one; it used to be the other way round. */}
          <p className="mt-3 text-center text-sm text-slate-600">
            Ola bilsin ki, bu telefon hesabınıza bağlı deyil — məsələn, yeni telefon işlədirsiniz. Belə
            halda şəkli neçə dəfə çəksəniz də nəticə dəyişməyəcək. Ən qısa yol — administratora sorğu
            göndərmək.
          </p>
          {adminButton('mt-5 w-full rounded-2xl bg-blue-600 py-3 font-bold text-white disabled:opacity-50')}
          <button onClick={retryPhoto} className={quietBtn}>
            Şəkli yenidən çək
          </button>
          {errorNote}
          {backLink}
        </div>
      </div>
    )
  }

  if (phase === 'unreachable') {
    return (
      <div className={wrap}>
        <div className={card}>
          <div className="text-center text-5xl">📶</div>
          <h1 className="mt-2 text-center text-xl font-bold">Yoxlama başa çatmadı</h1>
          <p className="mt-3 text-center text-sm text-slate-600">
            {trouble} Düzəlməsə, administratora sorğu göndərin — PIN-inizi o sıfırlayacaq.
          </p>
          <button onClick={retryPhoto} className="mt-5 w-full rounded-2xl bg-blue-600 py-3 font-bold text-white">
            Yenidən cəhd et
          </button>
          {adminButton(quietBtn)}
          {errorNote}
          {backLink}
        </div>
      </div>
    )
  }

  if (phase === 'checking') {
    return (
      <div className={wrap}>
        <div className={card}>
          <p className="animate-pulse text-center text-lg font-semibold">Yoxlanılır…</p>
          {/* The wait is bounded by the request's own deadline (api/auth.ts), so this screen always
              moves on by itself — say so, and still carry an exit for anyone who would rather leave. */}
          <p className="mt-2 text-center text-sm text-slate-500">
            Bu bir neçə saniyə çəkə bilər. Cavab gəlməsə, ekran özü dəyişəcək və yenidən cəhd edə
            biləcəksiniz.
          </p>
          {backLink}
        </div>
      </div>
    )
  }

  if (phase === 'camera') {
    return (
      <div className={wrap}>
        <div className={card}>
          <h1 className="text-center text-xl font-bold">Üzünüzü təsdiqləyin</h1>
          <p className="mt-1 text-center text-sm text-slate-500">
            Üzünüzü aydın kameraya tutun — bu, sizin olduğunuzu təsdiqləyir.
          </p>
          <div className="mt-4 overflow-hidden rounded-2xl border border-slate-200 bg-slate-900" style={{ aspectRatio: '3 / 4' }}>
            <video ref={videoRef} playsInline muted autoPlay className="h-full w-full -scale-x-100 object-cover" />
          </div>
          {camError ? (
            <>
              {/* "Kamera açılmadı" read as "my phone is broken". It is a permission, it is fixable from
                  the phone — but only if we name the exact menu, the way CameraHelp does on the scan
                  screen — and it needs a button that actually re-opens the camera afterwards. */}
              <p className="mt-3 text-center text-sm font-semibold text-amber-800">
                Telefon kameraya icazə vermir.
              </p>
              <p className="mt-1 text-left text-sm leading-relaxed text-amber-700">{cameraPermissionPath()}</p>
              <p className="mt-1 text-center text-sm text-amber-700">
                İcazə verdikdən sonra aşağıdakı düyməni basın. Alınmasa, administratora sorğu göndərin.
              </p>
              <button onClick={() => void startCamera()} disabled={camBusy} className={primaryBtn}>
                {camBusy ? 'Açılır…' : 'Kameranı yenidən aç'}
              </button>
              {adminButton(quietBtn)}
              {errorNote}
            </>
          ) : (
            <>
              <button
                onClick={() => void captureAndVerify()}
                disabled={!camReady}
                className="mt-3 w-full rounded-2xl bg-blue-600 py-4 text-lg font-bold text-white disabled:opacity-50"
              >
                📷 Şəkil çək və yoxla
              </button>
              {/* The working camera keeps ONE button — but the moment taking the photo goes wrong,
                  the way out appears under the explanation instead of leaving them tapping. */}
              {errorNote}
              {error && adminButton(quietBtn)}
            </>
          )}
          {backLink}
        </div>
      </div>
    )
  }

  // phase === 'ask'
  return (
    <div className={wrap}>
      <div className={card}>
        <h1 className="text-center text-xl font-bold">PIN-i unutdum</h1>
        <p className="mt-2 text-center text-sm text-slate-500">
          Telefon nömrənizi yazın, sonra üzünüzü təsdiqləyin — yeni PIN dərhal veriləcək.
        </p>
        {errorNote}
        <input
          className="mt-4 w-full rounded-xl border border-slate-200 bg-white px-3 py-3 text-center text-lg focus:border-blue-500 focus:outline-none focus:ring-2 focus:ring-blue-200"
          type="tel"
          inputMode="tel"
          placeholder="Telefon nömrəsi"
          value={identifier}
          onChange={(e) => setIdentifier(e.target.value)}
          onKeyDown={(e) => e.key === 'Enter' && startFlow()}
        />
        <button onClick={startFlow} className="mt-4 w-full rounded-2xl bg-blue-600 py-4 text-lg font-bold text-white">
          Davam et
        </button>
        {backLink}
      </div>
    </div>
  )
}
