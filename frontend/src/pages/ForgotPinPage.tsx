import { useEffect, useRef, useState } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import { forgotPin, forgotPinVerify } from '../api/auth'
import { getDeviceFingerprint } from '../lib/device'

/**
 * Self-service "PIN-i unutdum". Two factors, no admin: a selfie matched to the reference photo, from a
 * device already bound to the account. On success the new temporary PIN is shown on the spot. If it
 * can't verify (different phone, camera off, face doesn't match), it falls back to filing a request in
 * the admin queue — so nobody is left stuck either way.
 */
type Phase = 'ask' | 'camera' | 'checking' | 'success' | 'failed' | 'requested'

export function ForgotPinPage() {
  const navigate = useNavigate()
  const location = useLocation()
  const prefilled = (location.state as { identifier?: string } | null)?.identifier ?? ''

  const [phase, setPhase] = useState<Phase>('ask')
  const [identifier, setIdentifier] = useState(prefilled)
  const [pin, setPin] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const videoRef = useRef<HTMLVideoElement | null>(null)
  const streamRef = useRef<MediaStream | null>(null)
  const [camReady, setCamReady] = useState(false)
  const [camError, setCamError] = useState(false)

  useEffect(() => {
    if (phase !== 'camera') return
    void startCamera()
    return () => stopCamera()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [phase])

  async function startCamera() {
    setCamError(false)
    if (!navigator.mediaDevices?.getUserMedia) {
      setCamError(true)
      return
    }
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: 'user' }, audio: false })
      streamRef.current = stream
      const v = videoRef.current
      if (v) {
        v.srcObject = stream
        await v.play().catch(() => {})
      }
      setCamReady(true)
    } catch {
      setCamError(true)
      setCamReady(false)
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

  async function captureAndVerify() {
    const v = videoRef.current
    if (!v || v.videoWidth === 0) return
    const w = Math.min(640, v.videoWidth)
    const scale = w / v.videoWidth
    const canvas = document.createElement('canvas')
    canvas.width = w
    canvas.height = Math.round(v.videoHeight * scale)
    const ctx = canvas.getContext('2d')
    if (!ctx) return
    ctx.drawImage(v, 0, 0, canvas.width, canvas.height)
    const photo = canvas.toDataURL('image/jpeg', 0.8)
    stopCamera()
    setPhase('checking')

    const { status, data } = await forgotPinVerify(identifier.trim(), getDeviceFingerprint(), photo)
    if (status === 200 && data && 'verified' in data && data.verified && data.pin) {
      setPin(data.pin)
      setPhase('success')
    } else {
      setPhase('failed')
    }
  }

  // Fallback when face+device can't verify: file a request for the admin to reset by hand.
  async function sendAdminRequest() {
    setBusy(true)
    try {
      await forgotPin(identifier.trim())
      setPhase('requested')
    } catch {
      setError('Serverə qoşulmaq mümkün olmadı')
    } finally {
      setBusy(false)
    }
  }

  const card = 'w-full max-w-sm rounded-3xl border border-slate-100 bg-white p-6 shadow-sm'
  const wrap = 'flex min-h-screen flex-col items-center justify-center bg-slate-50 p-4 text-slate-900'
  const backLink = (
    <button onClick={() => navigate('/login')} className="mt-4 w-full text-sm font-semibold text-slate-500 underline underline-offset-2">
      Girişə qayıt
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
          <p className="mt-3 text-center text-sm text-slate-600">
            Administrator PIN-inizi sıfırlayıb sizə yeni müvəqqəti PIN verəcək — onunla daxil olub öz
            PIN-inizi təyin edəcəksiniz.
          </p>
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
          <p className="mt-3 text-center text-sm text-slate-600">
            Üz uyğun gəlmədi, yaxud bu telefon hesabınıza bağlı deyil (məsələn, yeni telefondasınız).
            Yenidən cəhd edin, ya da administratordan sıfırlama istəyin.
          </p>
          <button
            onClick={() => setPhase('camera')}
            className="mt-5 w-full rounded-2xl bg-blue-600 py-3 font-bold text-white"
          >
            Yenidən cəhd et
          </button>
          <button
            onClick={() => void sendAdminRequest()}
            disabled={busy}
            className="mt-2 w-full rounded-2xl bg-slate-100 py-3 font-semibold text-slate-700 disabled:opacity-50"
          >
            {busy ? 'Göndərilir…' : 'Administratora sorğu göndər'}
          </button>
          {error && <p className="mt-2 text-center text-sm text-red-600">{error}</p>}
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
              <p className="mt-3 text-center text-sm text-amber-700">
                Kamera açılmadı. Administratordan sıfırlama istəyə bilərsiniz.
              </p>
              <button
                onClick={() => void sendAdminRequest()}
                disabled={busy}
                className="mt-3 w-full rounded-2xl bg-blue-600 py-3 font-bold text-white disabled:opacity-50"
              >
                {busy ? 'Göndərilir…' : 'Administratora sorğu göndər'}
              </button>
            </>
          ) : (
            <button
              onClick={() => void captureAndVerify()}
              disabled={!camReady}
              className="mt-3 w-full rounded-2xl bg-blue-600 py-4 text-lg font-bold text-white disabled:opacity-50"
            >
              📷 Şəkil çək və yoxla
            </button>
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
        {error && <p className="mt-3 text-center text-sm text-red-600">{error}</p>}
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
