import { useEffect, useRef, useState, type FormEvent } from 'react'
import { Navigate, useNavigate } from 'react-router-dom'
import { setInitialPin } from '../api/auth'
import { setMyReferencePhoto } from '../api/attendance'
import { useAuth } from '../auth/AuthContext'
import { roleHome } from '../lib/jwt'

/**
 * Forced first-login screen for a temp-PIN account (bulk import or an admin reset). Two steps: set your
 * own PIN, then take a reference selfie (the face-audit baseline that a link-activated account would
 * have taken at activation). The selfie is required when the camera works; if the camera can't open,
 * it's skippable and the reference falls back to auto-seeding from the first check-in.
 */
export function SetPinPage() {
  const { saveToken, mustChangePin, role } = useAuth()
  const navigate = useNavigate()

  const [phase, setPhase] = useState<'pin' | 'photo'>('pin')

  // --- PIN step ---
  const [pin, setPin] = useState('')
  const [confirm, setConfirm] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)

  // --- reference selfie step ---
  const videoRef = useRef<HTMLVideoElement | null>(null)
  const streamRef = useRef<MediaStream | null>(null)
  const [photo, setPhoto] = useState<string | null>(null)
  const [camReady, setCamReady] = useState(false)
  const [camError, setCamError] = useState(false)
  const [saving, setSaving] = useState(false)

  // Start the front camera when we reach the photo step; release it on leave.
  useEffect(() => {
    if (phase !== 'photo') return
    void startCamera()
    return () => stopCamera()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [phase])

  // Only guard the PIN step: once the PIN is set, mustChangePin is already false but we stay for the
  // photo step.
  if (!mustChangePin && phase === 'pin') return <Navigate to={roleHome(role)} replace />

  async function onSubmitPin(e: FormEvent) {
    e.preventDefault()
    setError(null)
    if (!/^\d{4}$/.test(pin)) {
      setError('PIN düz 4 rəqəm olmalıdır')
      return
    }
    if (pin !== confirm) {
      setError('PIN-lər uyğun gəlmir')
      return
    }
    setLoading(true)
    try {
      const { status, data } = await setInitialPin(pin)
      if (status === 200 && data && 'token' in data) {
        saveToken(data.token)
        setPhase('photo') // move on to the reference selfie
        return
      }
      const code = data && 'error' in data ? data.error : ''
      setError(code === 'PinInvalid' ? 'PIN düz 4 rəqəm olmalıdır' : 'PIN təyin edilmədi')
    } catch {
      setError('Serverə qoşulmaq mümkün olmadı')
    } finally {
      setLoading(false)
    }
  }

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

  function capturePhoto() {
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
    setPhoto(canvas.toDataURL('image/jpeg', 0.8)) // JPEG — encodable everywhere incl. iOS Safari
    stopCamera()
  }

  function retake() {
    setPhoto(null)
    void startCamera()
  }

  function goHome() {
    stopCamera()
    navigate(roleHome(role), { replace: true })
  }

  async function saveAndFinish() {
    if (!photo) return
    setSaving(true)
    // Best-effort: a storage hiccup shouldn't trap the employee — the reference still auto-seeds from
    // their first check-in if this fails.
    await setMyReferencePhoto(photo).catch(() => {})
    setSaving(false)
    goHome()
  }

  const pinInputClass =
    'w-full rounded-xl border border-slate-200 bg-white px-3 py-3 text-center text-xl tracking-widest focus:border-blue-500 focus:outline-none focus:ring-2 focus:ring-blue-200'

  if (phase === 'photo') {
    return (
      <div className="flex min-h-screen flex-col items-center justify-center bg-slate-50 p-4 text-slate-900">
        <div className="w-full max-w-sm rounded-3xl border border-slate-100 bg-white p-6 shadow-sm">
          <h1 className="text-center text-xl font-bold">Referans şəkli çəkin</h1>
          <p className="mt-1 text-center text-sm text-slate-500">
            Üzünüzü aydın kameraya tutun — bu şəkil davamiyyət yoxlaması üçün nümunədir.
          </p>

          <div
            className="mt-4 overflow-hidden rounded-2xl border border-slate-200 bg-slate-900"
            style={{ aspectRatio: '3 / 4' }}
          >
            {photo ? (
              <img src={photo} alt="Referans" className="h-full w-full object-cover" />
            ) : (
              <video ref={videoRef} playsInline muted autoPlay className="h-full w-full -scale-x-100 object-cover" />
            )}
          </div>

          {camError ? (
            <>
              <p className="mt-3 text-center text-sm text-amber-700">
                Kamera açılmadı — indi keçə bilərsiniz, referans ilk girişinizdə çəkiləcək.
              </p>
              <button
                onClick={goHome}
                className="mt-3 w-full rounded-2xl bg-slate-200 py-3 font-semibold text-slate-700"
              >
                Keç
              </button>
            </>
          ) : photo ? (
            <div className="mt-3 flex gap-2">
              <button onClick={retake} className="flex-1 rounded-2xl bg-slate-100 py-3 font-semibold text-slate-700">
                Yenidən çək
              </button>
              <button
                onClick={saveAndFinish}
                disabled={saving}
                className="flex-1 rounded-2xl bg-blue-600 py-3 font-bold text-white transition disabled:opacity-50"
              >
                {saving ? 'Saxlanır…' : 'Təsdiqlə'}
              </button>
            </div>
          ) : (
            <button
              onClick={capturePhoto}
              disabled={!camReady}
              className="mt-3 w-full rounded-2xl bg-blue-600 py-3 text-lg font-bold text-white transition disabled:opacity-50"
            >
              📷 Şəkil çək
            </button>
          )}
        </div>
      </div>
    )
  }

  return (
    <div className="flex min-h-screen flex-col items-center justify-center bg-slate-50 p-4 text-slate-900">
      <form onSubmit={onSubmitPin} className="w-full max-w-sm rounded-3xl border border-slate-100 bg-white p-6 shadow-sm">
        <h1 className="text-center text-xl font-bold">Öz PIN-inizi təyin edin</h1>
        <p className="mt-2 text-center text-sm text-slate-500">
          Bu ilk girişdir. Təhlükəsizlik üçün müvəqqəti PIN-i öz PIN-inizlə əvəz edin.
        </p>

        {error && (
          <div className="mt-4 rounded-lg bg-red-50 p-3 text-center text-base font-medium text-red-700">{error}</div>
        )}

        <div className="mt-5 flex flex-col gap-4">
          <div>
            <label className="mb-1 block text-sm text-slate-500">Yeni PIN (4 rəqəm)</label>
            <input
              type="password"
              inputMode="numeric"
              maxLength={4}
              required
              autoComplete="new-password"
              value={pin}
              onChange={(e) => setPin(e.target.value.replace(/\D/g, '').slice(0, 4))}
              className={pinInputClass}
            />
          </div>
          <div>
            <label className="mb-1 block text-sm text-slate-500">Yeni PIN-i təkrarlayın</label>
            <input
              type="password"
              inputMode="numeric"
              maxLength={4}
              required
              autoComplete="new-password"
              value={confirm}
              onChange={(e) => setConfirm(e.target.value.replace(/\D/g, '').slice(0, 4))}
              className={pinInputClass}
            />
          </div>

          <button
            type="submit"
            disabled={loading}
            className="w-full rounded-2xl bg-blue-600 py-4 text-lg font-bold text-white transition hover:bg-blue-700 disabled:opacity-50"
          >
            {loading ? 'Yadda saxlanır…' : 'Davam et'}
          </button>
        </div>
      </form>
    </div>
  )
}
