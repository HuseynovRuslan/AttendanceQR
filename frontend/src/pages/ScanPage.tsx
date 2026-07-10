import { useEffect, useRef, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { Html5Qrcode } from 'html5-qrcode'
import { apiRequest } from '../api/client'
import { getMyAttendance, reportScanFailure, type AttendanceRecord } from '../api/attendance'
import { getDeviceFingerprint } from '../lib/device'
import { FAILURE_REASON, getPosition, POOR_ACCURACY_METERS, type GeoFailKind } from '../lib/geo'
import { GpsHelp } from '../components/GpsHelp'

type Card = {
  tone: 'green' | 'red' | 'yellow'
  title: string
  detail?: string
  /** Quiet second line: what happens next, when the employee needs to know. */
  note?: string
  /** Nothing left to do here. Offering "scan again" after a successful check-in is what made people
   *  scan a second time and land on TooSoonToCheckOut — so success only ever offers "close". */
  final?: boolean
  /** The check-in selfie, shown back to the employee — the capture is disclosed, not covert. */
  photo?: string
  showDeviceChangeLink?: boolean
}
type Phase = 'scanning' | 'photo' | 'processing' | 'done'

// How long the front-camera preview stays up after the first real frame. Long enough to look at the
// lens, short enough that nobody feels delayed — there is no shutter button to press.
const PHOTO_HOLD_MS = 1200
// Keep the middle of the frame. The person holding the phone is centred; the queue behind them is
// not, and full-frame captures kept picking up two and three faces.
const PHOTO_CROP = 0.85
const PHOTO_SIZE = 480
// The scan is pointless without a position, so we settle this before the camera ever opens.
type GeoState = { kind: 'checking' } | { kind: 'ready'; accuracy: number } | { kind: 'failed'; fail: GeoFailKind }
type TodayInfo =
  | { kind: 'loading' }
  | { kind: 'none' }
  | { kind: 'in-progress'; checkInAtUtc: string }
  | { kind: 'completed'; checkInAtUtc: string; checkOutAtUtc: string }

const READER_ID = 'reader'

export function ScanPage() {
  const navigate = useNavigate()
  const scannerRef = useRef<Html5Qrcode | null>(null)
  // Photo audit: a hidden <video> we briefly attach the front camera to — on demand, AFTER the QR
  // (back) camera is released, since iOS Safari allows only one camera at a time — to grab a single
  // silent selfie frame at check-in. Best-effort: if the front camera is unavailable we skip the
  // photo and the check-in proceeds unaffected.
  const selfieVideoRef = useRef<HTMLVideoElement | null>(null)
  const busyRef = useRef(false)
  // True while a scan result is on screen — keeps the today-status reload (which flips today.kind)
  // from re-running the camera effect and wiping the result message. Cleared when scanning restarts.
  const scanDoneRef = useRef(false)
  const [phase, setPhase] = useState<Phase>('scanning')
  const [cameraError, setCameraError] = useState<string | null>(null)
  const [result, setResult] = useState<Card | null>(null)
  const [today, setToday] = useState<TodayInfo>({ kind: 'loading' })
  const [geo, setGeo] = useState<GeoState>({ kind: 'checking' })
  // True once the front camera is actually producing frames, so the preview says "look at the
  // camera" rather than showing a black circle while it warms up.
  const [photoLive, setPhotoLive] = useState(false)

  // Today's status decides whether the camera should even start — no point opening it if the
  // day is already complete (the backend would just reject with AlreadyCompleted anyway).
  useEffect(() => {
    void loadTodayStatus()
  }, [])

  // Settle the position up front. This both surfaces a broken GPS *before* the employee aims at the
  // poster (instead of after a failed scan) and warms the fix, so the scan itself answers instantly.
  useEffect(() => {
    void checkGeo()
  }, [])

  useEffect(() => {
    if (today.kind === 'loading') return
    if (today.kind === 'completed') {
      void stopCamera()
      return
    }
    // Without a position the scan would be rejected anyway — show the fix instructions, not a camera.
    if (geo.kind !== 'ready') {
      void stopCamera()
      return
    }
    // A scan just finished and its result is showing — don't auto-restart the camera (that would
    // clear the message). The user restarts via "Yenidən skan et".
    if (scanDoneRef.current) return
    void startCamera()
    return () => {
      void stopCamera()
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [today.kind, geo.kind])

  // Resolve the phone's position and record the outcome. A failure here is reported to the server
  // (deduplicated there) so the employee appears in the admin "Problemlər" screen — previously a
  // GPS failure never reached /scan and so left no trace at all.
  async function checkGeo() {
    setGeo({ kind: 'checking' })
    const position = await getPosition()

    if (!position.ok) {
      setGeo({ kind: 'failed', fail: position.kind })
      void reportScanFailure(FAILURE_REASON[position.kind]).catch(() => {})
      return
    }

    const accuracy = Math.round(position.coords.accuracy)
    setGeo({ kind: 'ready', accuracy })
    // Too coarse to mean much against a 150 m radius. Deliberately not blocking during the pilot —
    // we warn the employee, note it for the admin, and let the scan through.
    if (accuracy > POOR_ACCURACY_METERS) void reportScanFailure('GpsInaccurate', accuracy).catch(() => {})
  }

  function retryGeo() {
    scanDoneRef.current = false
    setResult(null)
    setPhase('scanning')
    void checkGeo()
  }

  async function loadTodayStatus() {
    try {
      const { status, data } = await getMyAttendance()
      if (status !== 200 || !Array.isArray(data)) {
        setToday({ kind: 'none' })
        return
      }
      const todayStr = new Date().toISOString().slice(0, 10)
      const record = data.find((r) => r.attendanceDate === todayStr)
      setToday(recordToTodayInfo(record))
    } catch {
      setToday({ kind: 'none' })
    }
  }

  async function startCamera() {
    scanDoneRef.current = false
    setCameraError(null)
    setResult(null)
    setPhase('scanning')
    busyRef.current = false

    // Let the reader element become visible before the camera attaches.
    await new Promise((r) => requestAnimationFrame(() => r(null)))

    try {
      const scanner = new Html5Qrcode(READER_ID, { verbose: false })
      scannerRef.current = scanner
      await scanner.start(
        { facingMode: 'environment' },
        { fps: 10, qrbox: { width: 250, height: 250 } },
        onDecoded,
        undefined,
      )
    } catch {
      setCameraError(
        'Kameraya çıxış yoxdur. Brauzerdə kamera icazəsi verin. Kamera yalnız HTTPS və ya localhost-da işləyir.',
      )
    }
  }

  async function stopCamera() {
    const scanner = scannerRef.current
    scannerRef.current = null
    if (!scanner) return
    try {
      await scanner.stop()
    } catch {
      /* already stopped */
    }
    try {
      scanner.clear()
    } catch {
      /* ignore */
    }
  }

  // --- selfie (photo audit) front camera ------------------------------------

  // Opens the front camera ON DEMAND (the caller must have released the QR/back camera first — iOS
  // Safari allows only one camera at a time) and shows the employee what it sees for a moment before
  // taking the frame. The capture is DISCLOSED, not covert: the phone lights its camera indicator
  // anyway, and someone who knows they are being photographed both holds the phone properly (one
  // face, straight on) and is actually deterred from scanning for a colleague. Returns a data URL,
  // or null if anything is unavailable — the check-in never depends on it.
  async function captureSelfie(): Promise<string | null> {
    if (!navigator.mediaDevices?.getUserMedia) return null
    const video = selfieVideoRef.current
    if (!video) return null
    let stream: MediaStream | null = null
    try {
      stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: 'user' }, audio: false })
      video.srcObject = stream
      await video.play().catch(() => {})
      // Wait for a real frame (+ a short settle for exposure/focus) so it isn't black, with a timeout.
      await waitForVideoFrame(video, 2500)
      if (video.videoWidth === 0) return null

      setPhotoLive(true)
      await delay(PHOTO_HOLD_MS)
      return await frameToJpeg(video)
    } catch {
      // No front camera / permission denied — skip the photo, check-in proceeds without it.
      return null
    } finally {
      stream?.getTracks().forEach((t) => t.stop())
      video.srcObject = null
      setPhotoLive(false)
    }
  }

  async function onDecoded(text: string) {
    if (busyRef.current) return
    busyRef.current = true
    // Release the QR (back) camera FIRST — iOS Safari won't open the front camera while another is
    // active. Then show feedback and grab the selfie (check-in only; check-out never captures one).
    await stopCamera()
    const willPhotograph = today.kind === 'none'
    setPhase(willPhotograph ? 'photo' : 'processing')
    const photoBase64 = willPhotograph ? await captureSelfie() : null
    setPhase('processing')
    await submitScan(text, photoBase64)
    setPhase('done')
    // Keep the result on screen: mark done so reloading today's status doesn't restart the camera.
    scanDoneRef.current = true
    void loadTodayStatus()
  }

  async function submitScan(qrToken: string, photoBase64: string | null) {
    // Warmed by the pre-flight check, so this normally returns a cached fix immediately. If the
    // employee revoked the permission between the two, fall back to the same instructions.
    const position = await getPosition()
    if (!position.ok) {
      void reportScanFailure(FAILURE_REASON[position.kind]).catch(() => {})
      setGeo({ kind: 'failed', fail: position.kind })
      setResult(null)
      return
    }
    const coords = position.coords

    try {
      const { status, data } = await apiRequest<ScanResponse>('/api/attendance/scan', {
        method: 'POST',
        body: {
          qrToken,
          deviceFingerprint: getDeviceFingerprint(),
          latitude: coords.latitude,
          longitude: coords.longitude,
          // Omit entirely when there's no photo so the field stays optional on the wire.
          ...(photoBase64 ? { photoBase64 } : {}),
        },
      })

      if (status === 200 && data?.action === 'CheckIn') {
        setResult({
          tone: 'green',
          title: 'Giriş qeydə alındı',
          // No on-time/late verdict: every employee keeps their own hours, so one location-wide
          // shift makes "Gecikmə" a wrong label rather than a useful one.
          detail: `Saat ${fmtTime(data.checkInAtUtc)}`,
          note: 'İş bitəndə çıxış üçün yenidən skan edin.',
          final: true,
          photo: photoBase64 ?? undefined,
        })
        return
      }
      if (status === 200 && data?.action === 'CheckOut') {
        const worked = data.recordId ? await workedDurationText(data.recordId) : undefined
        setResult({
          tone: 'green',
          title: 'Çıxış qeydə alındı',
          detail: worked ?? `Saat ${fmtTime(data.checkOutAtUtc)}`,
          note: 'Sabaha qədər!',
          final: true,
        })
        return
      }
      setResult(errorResult(status, data))
    } catch {
      setResult({ tone: 'red', title: 'Şəbəkə xətası', detail: 'Serverə qoşulmaq mümkün olmadı.' })
    }
  }

  // Only while actually scanning — the QR frame must give way to the selfie preview, not sit behind it.
  const showCamera =
    today.kind !== 'loading' && today.kind !== 'completed' && geo.kind === 'ready' && phase === 'scanning' && !cameraError

  return (
    <div className="min-h-screen flex flex-col bg-slate-900 text-white">
      <header className="flex items-center justify-between border-b border-slate-800 px-4 py-3">
        <span className="font-semibold">QR skan</span>
        <button
          onClick={() => navigate('/home')}
          className="rounded-lg bg-slate-800 px-3 py-1.5 text-sm text-slate-300 transition hover:text-white"
        >
          Bağla
        </button>
      </header>

      <main className="flex-1 flex flex-col items-center justify-center p-4 gap-5">
        <TodayBanner today={today} />

        {today.kind === 'completed' && (
          <div className="w-full max-w-sm rounded-2xl p-6 text-center bg-green-500 text-white shadow-lg">
            <div className="text-6xl font-bold mb-3">✓</div>
            <h2 className="text-xl font-bold">Bu gün tamamlandı</h2>
            <p className="mt-2 text-base opacity-90">
              {fmtTime(today.checkInAtUtc)} – {fmtTime(today.checkOutAtUtc)}
              {' · '}
              {formatDuration(minutesBetween(today.checkInAtUtc, today.checkOutAtUtc))}
            </p>
          </div>
        )}

        {today.kind !== 'completed' && geo.kind === 'checking' && (
          <p className="text-lg text-slate-300 animate-pulse">Məkan yoxlanılır…</p>
        )}

        {today.kind !== 'completed' && geo.kind === 'failed' && (
          <GpsHelp kind={geo.fail} onRetry={retryGeo} />
        )}

        {/* Position obtained, but too coarse to sit comfortably inside a 150 m radius. Scanning is
            still allowed — this only nudges the employee somewhere with a clearer view of the sky. */}
        {showCamera && geo.kind === 'ready' && geo.accuracy > POOR_ACCURACY_METERS && (
          <div className="w-full max-w-sm rounded-xl bg-yellow-400/15 border border-yellow-400/40 px-4 py-3 text-center text-sm text-yellow-200">
            GPS dəqiqliyi zəifdir (±{geo.accuracy} m). Skan işləyəcək, amma açıq yerdə daha dəqiq olar.
          </div>
        )}

        {/* Camera container stays mounted so html5-qrcode can always find it. */}
        <div className={showCamera ? 'w-full max-w-sm' : 'hidden'}>
          <div id={READER_ID} className="w-full overflow-hidden rounded-2xl bg-black" />
          <p className="text-center text-slate-300 mt-3">QR kodu kameraya tutun</p>
          {today.kind === 'none' && (
            <p className="text-center text-xs text-slate-500 mt-1">Girişdə şəkil çəkilir</p>
          )}
        </div>

        {/* Front-camera preview for the check-in selfie. Stays mounted (hidden) so captureSelfie()
            can always read a frame, and is SHOWN while capturing — the photo is disclosed, and an
            employee looking at the lens produces one clean face instead of the queue behind them.
            The circle matches the centre crop frameToJpeg() takes, so what they see is what is kept. */}
        <div className={phase === 'photo' ? 'flex w-full max-w-sm flex-col items-center gap-3' : 'hidden'}>
          <div className="relative h-56 w-56 overflow-hidden rounded-full border-4 border-white/70 bg-black shadow-lg">
            <video
              ref={selfieVideoRef}
              className="h-full w-full -scale-x-100 object-cover"
              playsInline
              muted
              autoPlay
            />
          </div>
          <p className="text-lg font-semibold">{photoLive ? 'Şəklə baxın…' : 'Kamera hazırlanır…'}</p>
          <p className="text-sm text-slate-400">Giriş şəkli çəkilir</p>
        </div>

        {phase === 'processing' && (
          <p className="text-lg animate-pulse">Yoxlanılır…</p>
        )}

        {cameraError && (
          <ResultCard
            card={{ tone: 'red', title: 'Kamera xətası', detail: cameraError }}
            onRetry={startCamera}
            onClose={() => navigate('/home')}
          />
        )}

        {phase === 'done' && result && (
          <ResultCard card={result} onRetry={startCamera} onClose={() => navigate('/home')} />
        )}
      </main>
    </div>
  )
}

// --- today status banner ----------------------------------------------------

function TodayBanner({ today }: { today: TodayInfo }) {
  if (today.kind === 'loading' || today.kind === 'completed') return null
  return (
    <div className="w-full max-w-sm rounded-xl bg-slate-800 text-slate-100 px-4 py-3 text-center text-base">
      {today.kind === 'none' && 'Bu gün hələ giriş etməmisiniz'}
      {today.kind === 'in-progress' && (
        <>
          Giriş: <b>{fmtTime(today.checkInAtUtc)}</b> — hələ çıxış etməmisiniz
        </>
      )}
    </div>
  )
}

function recordToTodayInfo(record: AttendanceRecord | undefined): TodayInfo {
  if (!record?.checkInAtUtc) return { kind: 'none' }
  if (!record.checkOutAtUtc) return { kind: 'in-progress', checkInAtUtc: record.checkInAtUtc }
  return { kind: 'completed', checkInAtUtc: record.checkInAtUtc, checkOutAtUtc: record.checkOutAtUtc }
}

// --- result card -----------------------------------------------------------

function ResultCard({ card, onRetry, onClose }: { card: Card; onRetry: () => void; onClose: () => void }) {
  const tone = {
    green: 'bg-green-500 text-white',
    red: 'bg-red-500 text-white',
    yellow: 'bg-yellow-400 text-slate-900',
  }[card.tone]
  const icon = card.tone === 'green' ? '✓' : card.tone === 'yellow' ? '!' : '✕'

  return (
    <div className={`w-full max-w-sm rounded-2xl p-6 text-center shadow-lg ${tone}`}>
      <div className="text-6xl font-bold mb-3">{icon}</div>
      <h2 className="text-xl font-bold">{card.title}</h2>
      {card.detail && <p className="mt-2 text-base opacity-90">{card.detail}</p>}
      {card.note && <p className="mt-1 text-sm opacity-75">{card.note}</p>}

      {/* Showing the photo back closes the loop: the employee sees exactly what was stored. */}
      {card.photo && (
        <img
          src={card.photo}
          alt="Giriş şəkli"
          className="mx-auto mt-4 h-20 w-20 rounded-full object-cover ring-2 ring-white/60"
        />
      )}

      {/* The only button on a settled result is "close". Anything else invites the second scan. */}
      {card.final ? (
        <button
          onClick={onClose}
          className="mt-6 w-full bg-black/15 hover:bg-black/25 rounded-lg py-3 font-semibold transition"
        >
          Bağla
        </button>
      ) : (
        <button
          onClick={onRetry}
          className="mt-6 w-full bg-black/15 hover:bg-black/25 rounded-lg py-3 font-semibold transition"
        >
          Yenidən skan et
        </button>
      )}

      {card.showDeviceChangeLink && (
        <Link
          to="/device-change-request"
          className="mt-3 block w-full bg-black/15 hover:bg-black/25 rounded-lg py-3 font-semibold transition"
        >
          Bu mənim yeni telefonumdur
        </Link>
      )}
    </div>
  )
}

// --- helpers ---------------------------------------------------------------

interface ScanResponse {
  action?: 'CheckIn' | 'CheckOut'
  recordId?: string
  status?: string
  checkInAtUtc?: string
  checkOutAtUtc?: string
  error?: string
  distanceMeters?: number
  minutes?: number
}

interface MeRecord {
  recordId: string
  checkInAtUtc?: string
  checkOutAtUtc?: string
}

function blobToDataUrl(blob: Blob): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader()
    reader.onloadend = () => resolve(reader.result as string)
    reader.onerror = () => reject(reader.error)
    reader.readAsDataURL(blob)
  })
}

// Resolve once the video has a real frame (videoWidth > 0) plus a brief exposure/focus settle, or
// after a timeout — so an on-demand front-camera capture isn't a black frame.
function waitForVideoFrame(video: HTMLVideoElement, timeoutMs: number): Promise<void> {
  return new Promise((resolve) => {
    const start = performance.now()
    function tick() {
      if (video.videoWidth > 0) {
        setTimeout(resolve, 250)
        return
      }
      if (performance.now() - start >= timeoutMs) {
        resolve()
        return
      }
      requestAnimationFrame(tick)
    }
    tick()
  })
}

function fmtTime(iso?: string): string {
  if (!iso) return ''
  return new Date(iso).toLocaleTimeString('az-AZ', { hour: '2-digit', minute: '2-digit' })
}

function delay(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms))
}

// Crop the CENTRE of the frame and encode JPEG (Safari's canvas cannot encode WebP — it returns
// null). Bystanders queueing behind the employee sit at the edges of a front-camera frame, so
// discarding the edges is what stops two and three faces landing in a check-in photo.
function frameToJpeg(video: HTMLVideoElement): Promise<string | null> {
  const side = Math.round(Math.min(video.videoWidth, video.videoHeight) * PHOTO_CROP)
  const sx = Math.round((video.videoWidth - side) / 2)
  const sy = Math.round((video.videoHeight - side) / 2)

  const canvas = document.createElement('canvas')
  canvas.width = PHOTO_SIZE
  canvas.height = PHOTO_SIZE
  const ctx = canvas.getContext('2d')
  if (!ctx) return Promise.resolve(null)
  ctx.drawImage(video, sx, sy, side, side, 0, 0, PHOTO_SIZE, PHOTO_SIZE)

  return new Promise((resolve) =>
    canvas.toBlob(
      (blob) => (blob ? blobToDataUrl(blob).then(resolve, () => resolve(null)) : resolve(null)),
      'image/jpeg',
      0.75,
    ),
  )
}

function minutesBetween(startIso: string, endIso: string): number {
  return Math.round((new Date(endIso).getTime() - new Date(startIso).getTime()) / 60_000)
}

function formatDuration(minutes: number): string {
  const h = Math.floor(minutes / 60)
  const m = minutes % 60
  return `${h} saat ${m} dəqiqə işlədiniz`
}

// Checkout response carries no duration, so read today's record from /me and compute it.
async function workedDurationText(recordId: string): Promise<string | undefined> {
  try {
    const { status, data } = await apiRequest<MeRecord[]>('/api/attendance/me')
    if (status !== 200 || !Array.isArray(data)) return undefined
    const record = data.find((r) => r.recordId === recordId)
    if (!record?.checkInAtUtc || !record.checkOutAtUtc) return undefined
    return formatDuration(minutesBetween(record.checkInAtUtc, record.checkOutAtUtc))
  } catch {
    return undefined
  }
}

// `final` marks the outcomes where scanning again cannot change anything — the day is already
// recorded, or only an admin can unblock the employee. Everything else genuinely is worth retrying
// (walk closer, re-aim at the poster), so those keep the retry button.
function errorResult(status: number, data: ScanResponse | null): Card {
  const err = data?.error
  switch (err) {
    case 'OutsideRadius':
      return {
        tone: 'red',
        title: 'İş yerində deyilsiniz',
        detail: data?.distanceMeters != null ? `Məsafə: ${data.distanceMeters} m` : 'Radius xaricindəsiniz',
        note: 'İş yerinə yaxınlaşıb yenidən cəhd edin.',
      }
    case 'DeviceMismatch':
      return {
        tone: 'red',
        title: 'Bu cihaz hesabınıza bağlı deyil',
        note: 'Yenidən skan etmək kömək etməyəcək.',
        final: true,
        showDeviceChangeLink: true,
      }
    case 'NoDeviceBound':
      return { tone: 'red', title: 'Cihaz hesabınıza bağlı deyil', detail: 'Admin ilə əlaqə saxlayın.', final: true }
    case 'TokenExpired':
    case 'TokenReused':
      return { tone: 'yellow', title: 'QR kod köhnəlib', detail: 'Yenidən skan edin.' }
    case 'AlreadyCompleted':
      return {
        tone: 'yellow',
        title: 'Bu gün tamamlanıb',
        detail: 'Giriş və çıxış artıq qeydə alınıb.',
        final: true,
      }
    case 'TooSoonToCheckOut':
      return {
        tone: 'green',
        title: 'Giriş artıq qeydə alınıb',
        detail: `Çıxış üçün ${data?.minutes ?? 5} dəqiqədən sonra skan edin.`,
        note: 'İndi bir şey etmək lazım deyil.',
        final: true,
      }
    case 'EmployeeNotFoundOrInactive':
      return { tone: 'red', title: 'Hesab aktiv deyil', detail: 'Admin ilə əlaqə saxlayın.', final: true }
    case 'LocationNotFound':
    case 'LocationInactive':
      return { tone: 'red', title: 'Məkan tapılmadı', detail: 'Admin ilə əlaqə saxlayın.', final: true }
    default:
      // QR signature/format failures and anything else — show the reason the backend returned.
      return { tone: 'yellow', title: 'QR kod qəbul edilmədi', detail: err ?? `HTTP ${status}` }
  }
}
