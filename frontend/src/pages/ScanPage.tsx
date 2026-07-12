import { useEffect, useRef, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { Html5Qrcode } from 'html5-qrcode'
import { apiRequest } from '../api/client'
import {
  getMyAttendance,
  getMyDeviceStatus,
  getMissedCheckoutStatus,
  reportScanFailure,
  type AttendanceRecord,
  type MissedCheckoutStatusResp,
} from '../api/attendance'
import { getDeviceFingerprint } from '../lib/device'
import { ScanChecklist, type ScanChecks } from '../components/ScanChecklist'
import { distanceMeters, FAILURE_REASON, getPosition, POOR_ACCURACY_METERS, type GeoFailKind } from '../lib/geo'
import { GpsHelp } from '../components/GpsHelp'
import { CameraHelp, cameraFailKind, type CameraFailKind } from '../components/CameraHelp'
import { PhotoIntro } from '../components/PhotoIntro'
import { MissedCheckoutBanner } from '../components/MissedCheckoutBanner'

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
type Phase = 'scanning' | 'intro' | 'photo' | 'processing' | 'done'

// The employee reads what is about to happen before the front camera opens. Auto-advances so a
// hesitant person cannot block the queue by never tapping "Hazıram".
const INTRO_MS = 6000

// How long the front-camera preview stays up after the first real frame. It has to outlast reading
// the words on screen and settling into position — at 1.2s the shot was taken before people had even
// looked up. A ring and a seconds counter run alongside so nobody is surprised. There is still no
// shutter button to press: the photo is taken automatically at zero.
const PHOTO_HOLD_MS = 5_000
// Keep the middle of the frame. The person holding the phone is centred; the queue behind them is
// not, and full-frame captures kept picking up two and three faces.
// A face-shaped (portrait) crop + output, matching the oval preview the employee sees.
const PHOTO_CROP = 0.92
const PHOTO_W = 420
const PHOTO_H = 540
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
  // True while this page is mounted — so a start() that resolves AFTER the employee tapped "Bağla"
  // releases the camera instead of leaving it held (a held camera is what turns the next scan black).
  const mountedRef = useRef(true)
  // A start is in flight — blocks a second overlapping start() from stacking a second video (and a
  // second, black stream) onto #reader.
  const startingRef = useRef(false)
  // True while a scan result is on screen — keeps the today-status reload (which flips today.kind)
  // from re-running the camera effect and wiping the result message. Cleared when scanning restarts.
  const scanDoneRef = useRef(false)
  const [phase, setPhase] = useState<Phase>('scanning')
  const [cameraError, setCameraError] = useState<CameraFailKind | null>(null)
  const [result, setResult] = useState<Card | null>(null)
  const [today, setToday] = useState<TodayInfo>({ kind: 'loading' })
  // Forgot-checkout gate: an open past day blocks the scan behind a prompt until it's reported or the
  // employee taps "Sonra skan et". `missedReady` guards against starting the camera before we know.
  const [missed, setMissed] = useState<MissedCheckoutStatusResp | null>(null)
  const [missedReady, setMissedReady] = useState(false)
  const [gateCleared, setGateCleared] = useState(false)
  const [geo, setGeo] = useState<GeoState>({ kind: 'checking' })
  // The visible pre-scan verification (device → location → camera). An overlay while it runs.
  const [verifying, setVerifying] = useState(true)
  const [checks, setChecks] = useState<ScanChecks>({ device: 'idle', location: 'idle', camera: 'idle' })
  // Set when the geofence pre-check finds the employee outside their workplace radius — surfaced
  // before scanning (with a "scan anyway" escape, since the QR's own location is the final word).
  const [radiusFail, setRadiusFail] = useState<{ distance: number; name: string } | null>(null)
  // True once the front camera is actually producing frames, so the preview says "look at the
  // camera" rather than showing a black circle while it warms up.
  const [photoLive, setPhotoLive] = useState(false)
  const photoProgress = useCaptureProgress(photoLive, PHOTO_HOLD_MS)
  const secondsLeft = Math.max(1, Math.ceil(((1 - photoProgress) * PHOTO_HOLD_MS) / 1000))

  // Resolves the intro screen early when the employee taps "Hazıram"; the timeout resolves it anyway.
  const introSkipRef = useRef<(() => void) | null>(null)
  const introProgress = useCaptureProgress(phase === 'intro', INTRO_MS)
  const introSecondsLeft = Math.max(1, Math.ceil(((1 - introProgress) * INTRO_MS) / 1000))

  // Track mount so an in-flight camera start can bail if the employee already left the screen.
  useEffect(() => {
    mountedRef.current = true
    return () => {
      mountedRef.current = false
      void stopCamera()
    }
  }, [])

  // Today's status decides whether the camera should even start — no point opening it if the
  // day is already complete (the backend would just reject with AlreadyCompleted anyway).
  useEffect(() => {
    void loadTodayStatus()
  }, [])

  // Is there a forgotten-checkout day to deal with before scanning? Fail-open: any error just lets the
  // scan proceed (never trap someone at the gate over this check).
  useEffect(() => {
    void getMissedCheckoutStatus()
      .then((r) => {
        if (r.status === 200 && r.data && 'openDay' in r.data) setMissed(r.data)
      })
      .finally(() => setMissedReady(true))
  }, [])

  // The scan may start once we know there's nothing to block on: no open day, one already pending, or
  // the employee cleared the prompt (reported / "Sonra skan et").
  const scanAllowed = missedReady && (!missed?.openDay || missed.pending || gateCleared)

  // Run the pre-scan verification once today's status is known (and re-run on an explicit retry).
  // The day being already complete needs no camera at all.
  useEffect(() => {
    // Held behind the forgot-checkout prompt — don't open the camera yet.
    if (!scanAllowed) return
    if (today.kind === 'loading') return
    if (today.kind === 'completed') {
      setVerifying(false)
      void stopCamera()
      return
    }
    // A scan just finished and its result is on screen — don't re-verify (that would wipe it). The
    // employee restarts via "Yenidən skan et", which calls runChecks itself.
    if (scanDoneRef.current) return
    void runChecks()
    return () => {
      void stopCamera()
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [today.kind, scanAllowed])

  // Failsafe: the checklist overlay must never stick. runChecks always clears `verifying` itself, but
  // if some await hangs unexpectedly, drop the overlay after 25s so the employee is never trapped.
  useEffect(() => {
    if (!verifying) return
    const t = setTimeout(() => setVerifying(false), 25_000)
    return () => clearTimeout(t)
  }, [verifying])

  // The three checks that really gate a scan — device binding, location, camera — run in order and
  // are shown one after another (ScanChecklist) so the process is visible, not a hidden pause.
  // Device is advisory (the server still enforces it on the scan); location is the real gate.
  async function runChecks() {
    setVerifying(true)
    setCameraError(null)
    setResult(null)
    setRadiusFail(null)
    setPhase('scanning')
    scanDoneRef.current = false
    busyRef.current = false
    setGeo({ kind: 'checking' })
    setChecks({ device: 'run', location: 'idle', camera: 'idle' })

    // 1) Device — is this browser context bound to the caller's account? Advisory only, so a slow or
    // failed check must never block the scan: race it against a short timeout and treat null as pass.
    const dev = await Promise.race([
      getMyDeviceStatus(getDeviceFingerprint()).catch(() => null),
      delay(3000).then(() => null),
    ])
    await delay(650)
    const deviceStep =
      dev && dev.status === 200 && dev.data && 'bound' in dev.data
        ? dev.data.revoked
          ? 'fail'
          : dev.data.bound
            ? 'ok'
            : 'warn' // not bound yet — it will be adopted on this scan (at the geofence)
        : 'ok'
    const assignedLocation =
      dev && dev.status === 200 && dev.data && 'location' in dev.data ? dev.data.location : null
    setChecks((c) => ({ ...c, device: deviceStep, location: 'run' }))
    if (deviceStep === 'fail') {
      await delay(1000)
      setVerifying(false)
      setResult({ tone: 'red', title: 'Cihaz ləğv edilib', detail: 'Administrator ilə əlaqə saxlayın.', final: true })
      setPhase('done')
      scanDoneRef.current = true
      return
    }

    // 2) Location — the real gate. A failure shows GpsHelp.
    const position = await getPosition()
    await delay(650)
    if (!position.ok) {
      setChecks((c) => ({ ...c, location: 'fail' }))
      setGeo({ kind: 'failed', fail: position.kind })
      void reportScanFailure(FAILURE_REASON[position.kind]).catch(() => {})
      await delay(900)
      setVerifying(false)
      return
    }
    const accuracy = Math.round(position.coords.accuracy)
    setGeo({ kind: 'ready', accuracy })
    if (accuracy > POOR_ACCURACY_METERS) void reportScanFailure('GpsInaccurate', accuracy).catch(() => {})

    // Geofence pre-check: is the employee within their assigned workplace radius? Caught here so they
    // don't scan and get rejected. The scan still checks against the QR's own location server-side,
    // so this is advisory — a "scan anyway" escape covers the rare case of a different valid location.
    if (assignedLocation) {
      const dist = Math.round(
        distanceMeters(position.coords.latitude, position.coords.longitude, assignedLocation.latitude, assignedLocation.longitude),
      )
      if (dist > assignedLocation.radiusMeters) {
        setChecks((c) => ({ ...c, location: 'fail' }))
        await delay(900)
        setVerifying(false)
        setRadiusFail({ distance: dist, name: assignedLocation.name })
        return
      }
    }
    setChecks((c) => ({ ...c, location: 'ok', camera: 'run' }))

    // 3) Camera — the reader is now visible (phase 'scanning' + geo ready), so html5-qrcode attaches.
    await new Promise((r) => requestAnimationFrame(() => r(null)))
    await startCamera()
    await delay(500)
    if (scannerRef.current) {
      setChecks((c) => ({ ...c, camera: 'ok' }))
      await delay(550)
      setVerifying(false)
    } else {
      // startCamera set cameraError — reveal CameraHelp.
      setChecks((c) => ({ ...c, camera: 'fail' }))
      await delay(700)
      setVerifying(false)
    }
  }

  // Escape hatch from the geofence pre-check: the QR's own location is the final word (the employee
  // may legitimately be at a different location), so let them open the camera and let the server decide.
  function scanAnyway() {
    setRadiusFail(null)
    setChecks((c) => ({ ...c, location: 'ok', camera: 'ok' }))
    void startCamera()
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
    // One start at a time: a second overlapping start() stacks a second (black) video onto #reader.
    if (startingRef.current) return
    startingRef.current = true
    scanDoneRef.current = false
    setCameraError(null)
    setResult(null)
    setPhase('scanning')
    busyRef.current = false

    try {
      // Two attempts: start() can resolve while the device is still held by a prior stream and deliver
      // only black frames. If no real frame arrives we tear the whole thing down and try once more;
      // only then do we give up to CameraHelp. This is what the employee used to do by hand (refresh).
      for (let attempt = 0; attempt < 2; attempt++) {
        // Always clear any previous scanner (and its stream) before attaching a new one.
        await stopCamera()
        if (!mountedRef.current) return

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
        } catch (err) {
          // A real getUserMedia failure (denied / no camera / in use) — no point retrying.
          await stopCamera()
          setCameraError(cameraFailKind(err))
          return
        }

        // Left the screen while start() was in flight — release the camera and stop.
        if (!mountedRef.current) {
          await stopCamera()
          return
        }

        // start() resolved; confirm the stream is actually producing frames, not sitting black.
        if (await waitForReaderFrame(3500)) return // success — scannerRef stays set

        // Black. Tear down and loop; after the last attempt fall through to the CameraHelp below.
        await stopCamera()
      }
      if (mountedRef.current) setCameraError('inuse')
    } finally {
      startingRef.current = false
    }
  }

  async function stopCamera() {
    const scanner = scannerRef.current
    scannerRef.current = null
    // Kill the injected <video>'s stream FIRST — it survives a stop() that throws because start()
    // was still mid-flight, and a leaked track keeps the (single) camera busy → next start() is black.
    releaseReaderTracks()
    if (!scanner) return
    try {
      await scanner.stop()
    } catch {
      /* not started yet, or already stopped — the stream is handled above */
    }
    try {
      scanner.clear()
    } catch {
      /* ignore */
    }
  }

  // --- selfie (photo audit) front camera ------------------------------------

  // Holds on the intro card until the employee taps "Hazıram" or INTRO_MS elapses, whichever comes
  // first. Never rejects: the scan must continue either way.
  function waitForIntro(): Promise<void> {
    return new Promise((resolve) => {
      let settled = false
      const finish = () => {
        if (settled) return
        settled = true
        clearTimeout(timer)
        introSkipRef.current = null
        resolve()
      }
      const timer = setTimeout(finish, INTRO_MS)
      introSkipRef.current = finish
    })
  }

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
    if (willPhotograph) {
      setPhase('intro')
      await waitForIntro()
      setPhase('photo')
    } else {
      setPhase('processing')
    }
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
    today.kind !== 'loading' && today.kind !== 'completed' && geo.kind === 'ready' && phase === 'scanning' && !cameraError && !radiusFail

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

      <main className="relative flex-1 flex flex-col items-center justify-center p-4 gap-5">
        {verifying && today.kind !== 'completed' && <ScanChecklist checks={checks} />}

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

        {phase === 'intro' && (
          <PhotoIntro secondsLeft={introSecondsLeft} onReady={() => introSkipRef.current?.()} />
        )}

        {today.kind !== 'completed' && geo.kind === 'failed' && (
          <GpsHelp kind={geo.fail} onRetry={() => void runChecks()} />
        )}

        {radiusFail && (
          <div className="w-full max-w-sm rounded-2xl bg-slate-800 p-5 text-center shadow-lg">
            <div className="text-5xl">📍</div>
            <h2 className="mt-2 text-lg font-bold text-white">İş yerində deyilsiniz</h2>
            <p className="mt-1 text-sm text-slate-300">
              {radiusFail.name} ərazisindən təxminən <b className="text-white">{radiusFail.distance} m</b> uzaqsınız.
              Yaxınlaşıb yenidən yoxlayın.
            </p>
            <button
              onClick={() => void runChecks()}
              className="mt-5 w-full rounded-lg bg-white py-3 font-semibold text-slate-900 transition hover:bg-slate-200"
            >
              Yenidən yoxla
            </button>
            <button
              onClick={scanAnyway}
              className="mt-2 w-full rounded-lg py-2 text-sm font-medium text-slate-400 transition hover:text-white"
            >
              Yenə də skan et
            </button>
          </div>
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
          <div className="relative h-64 w-52">
            {/* Oval (face-shaped) frame so the employee lines their face up inside it. */}
            <div className="h-full w-full overflow-hidden rounded-[50%] border-2 border-white/20 bg-black shadow-lg">
              <video
                ref={selfieVideoRef}
                className="h-full w-full -scale-x-100 object-cover"
                playsInline
                muted
                autoPlay
              />
            </div>
            {/* Drains only once real frames arrive, so the countdown never runs while the camera is
                still warming up — the employee gets the full PHOTO_HOLD_MS to settle. */}
            <CaptureRing progress={photoProgress} />
          </div>

          {photoLive ? (
            <>
              <p className="text-xl font-bold">Ekrana baxın</p>
              <p className="text-base text-slate-300">Tərpənməyin — şəkil çəkilir</p>
              <p className="text-4xl font-extrabold tabular-nums text-green-400">{secondsLeft}</p>
            </>
          ) : (
            <p className="text-lg font-semibold">Kamera hazırlanır…</p>
          )}
        </div>

        {phase === 'processing' && (
          <p className="text-lg animate-pulse">Yoxlanılır…</p>
        )}

        {cameraError && <CameraHelp kind={cameraError} onRetry={() => void runChecks()} />}

        {phase === 'done' && result && (
          <ResultCard card={result} onRetry={() => void runChecks()} onClose={() => navigate('/home')} />
        )}
      </main>

      {/* Mandatory forgot-checkout gate: an open past day blocks the scan until the employee reports it
          or explicitly taps "Sonra skan et". Covers the checklist/camera so it can't be missed. */}
      {missedReady && !scanAllowed && (
        <div className="fixed inset-0 z-[60] flex flex-col items-center justify-center bg-slate-900/95 p-5">
          <div className="w-full max-w-sm">
            <p className="mb-3 text-center text-lg font-bold text-white">Əvvəlcə dünənki çıxışı bildirin</p>
            <MissedCheckoutBanner onReported={() => setGateCleared(true)} />
            <button
              onClick={() => setGateCleared(true)}
              className="mt-4 w-full rounded-2xl bg-slate-700 py-3 font-semibold text-slate-200 transition active:scale-[0.99]"
            >
              Sonra skan et
            </button>
          </div>
        </div>
      )}
    </div>
  )
}

// --- capture countdown ------------------------------------------------------

/** 0 → 1 over `durationMs`, restarting whenever `active` flips on. Driven by rAF rather than a CSS
 *  transition: it must start on the first real camera frame, not on mount, and the same value feeds
 *  both the ring and the seconds counter. */
function useCaptureProgress(active: boolean, durationMs: number): number {
  const [progress, setProgress] = useState(0)

  useEffect(() => {
    if (!active) {
      setProgress(0)
      return
    }
    let raf = 0
    const start = performance.now()
    const tick = (now: number) => {
      const p = Math.min(1, (now - start) / durationMs)
      setProgress(p)
      if (p < 1) raf = requestAnimationFrame(tick)
    }
    raf = requestAnimationFrame(tick)
    return () => cancelAnimationFrame(raf)
  }, [active, durationMs])

  return progress
}

/** Oval countdown ring tracing the face frame. An ellipse can't be rotated to start at the top without
 *  distorting its shape, so it's drawn as a path that begins at 12 o'clock; pathLength=1 lets the dash
 *  drain by fraction without computing the perimeter. */
function CaptureRing({ progress }: { progress: number }) {
  const cx = 104
  const cy = 128
  const rx = 96
  const ry = 120
  // Ellipse as a path, starting at the top, clockwise.
  const d = `M ${cx} ${cy - ry} A ${rx} ${ry} 0 1 1 ${cx} ${cy + ry} A ${rx} ${ry} 0 1 1 ${cx} ${cy - ry}`

  return (
    <svg viewBox="0 0 208 256" className="pointer-events-none absolute inset-0 h-full w-full">
      <path d={d} fill="none" stroke="rgba(255,255,255,0.15)" strokeWidth="6" />
      <path
        d={d}
        fill="none"
        stroke="#22c55e"
        strokeWidth="6"
        strokeLinecap="round"
        pathLength={1}
        strokeDasharray={1}
        strokeDashoffset={1 - progress}
      />
    </svg>
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

// Resolve true once html5-qrcode's injected <video> is actually producing frames (videoWidth > 0),
// false after a timeout. A start() that resolves but never delivers a frame is the "black camera"
// case — the device is still held by a stream that was never released.
function waitForReaderFrame(timeoutMs: number): Promise<boolean> {
  return new Promise((resolve) => {
    const start = performance.now()
    const tick = () => {
      const video = document.getElementById(READER_ID)?.querySelector('video') as HTMLVideoElement | null
      if (video && video.videoWidth > 0) {
        resolve(true)
        return
      }
      if (performance.now() - start >= timeoutMs) {
        resolve(false)
        return
      }
      requestAnimationFrame(tick)
    }
    tick()
  })
}

// Stop every track on the scanner's <video>, even when html5-qrcode's own stop() can't (it throws if
// start() hadn't finished). A live track holds the one camera the phone allows, so the next start()
// resolves to black until a full reload — this is what stops that from ever being needed.
function releaseReaderTracks() {
  const video = document.getElementById(READER_ID)?.querySelector('video') as HTMLVideoElement | null
  const stream = video?.srcObject as MediaStream | null
  stream?.getTracks().forEach((t) => t.stop())
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
  // Largest centred crop with the portrait (face) aspect, zoomed slightly (PHOTO_CROP), so the face
  // fills the frame the way the oval preview showed it. Bystanders sit outside a portrait crop too.
  const aspect = PHOTO_W / PHOTO_H
  const vw = video.videoWidth
  const vh = video.videoHeight
  let cw = vh * aspect
  let ch = vh
  if (cw > vw) {
    cw = vw
    ch = vw / aspect
  }
  cw = Math.round(cw * PHOTO_CROP)
  ch = Math.round(ch * PHOTO_CROP)
  const sx = Math.round((vw - cw) / 2)
  const sy = Math.round((vh - ch) / 2)

  const canvas = document.createElement('canvas')
  canvas.width = PHOTO_W
  canvas.height = PHOTO_H
  const ctx = canvas.getContext('2d')
  if (!ctx) return Promise.resolve(null)
  ctx.drawImage(video, sx, sy, cw, ch, 0, 0, PHOTO_W, PHOTO_H)

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
