import { useEffect, useRef, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
// Type only — the runtime library is imported dynamically (see loadScanner) so its ~150 kB (gzipped)
// of QR-decoder code stays OUT of the eager bundle every employee downloads to reach Login/Home. It is
// prefetched the moment ScanPage mounts, so by the time the camera opens (after the device + GPS
// checks) it is already in hand, and the service worker caches it after the first scan.
import type { Html5Qrcode } from 'html5-qrcode'
import { apiRequest } from '../api/client'
import {
  getMyToday,
  getMyDeviceStatus,
  type AttendanceRecord,
} from '../api/attendance'
import { reportFailure, flushFailures } from '../lib/scanFailures'
import { successFeedback, errorFeedback, primeFeedbackOnGesture } from '../lib/feedback'
import { getDeviceFingerprint } from '../lib/device'
import { shouldShowPushGate } from '../lib/push'
import { enqueueScan, isServerUnavailable, scansFor, type QueuedScan } from '../lib/offlineQueue'
import { qrlessRoute, recallQrless, rememberQrless } from '../lib/qrless'
import { decodeJwt } from '../lib/jwt'
import { ForeignQrDetector, looksLikeQrToken } from '../lib/qrShape'
import { todayStr } from '../lib/att'
import { getToken } from '../api/client'
import { PushEnablePrompt } from '../components/PushEnablePrompt'
import { PushGate } from '../components/PushGate'
import { ScanChecklist, type ScanChecks } from '../components/ScanChecklist'
import { distanceMeters, FAILURE_REASON, getPosition, POOR_ACCURACY_METERS, type GeoFailKind } from '../lib/geo'
import { GpsHelp } from '../components/GpsHelp'
import { CameraHelp, cameraFailKind, CAMERA_FAIL_REASON, type CameraFailKind } from '../components/CameraHelp'
import { PhotoIntro } from '../components/PhotoIntro'
import { checkForFace } from '../lib/faceCheck'
import { getMyProfile, type MyProfile } from '../api/attendance'
import { fmtTime } from '../lib/format'

type Card = {
  tone: 'green' | 'red' | 'yellow'
  title: string
  detail?: string
  /** Quiet second line: what happens next, when the employee needs to know. */
  note?: string
  /** Prominent notice pill (e.g. "Gecikdiniz" / "Tez çıxdınız") — informational, no action. */
  warn?: string
  /** Nothing left to do here. Offering "scan again" after a successful check-in is what made people
   *  scan a second time and land on TooSoonToCheckOut — so success only ever offers "close". */
  final?: boolean
  /** The check-in selfie, shown back to the employee — the capture is disclosed, not covert. */
  photo?: string
  showDeviceChangeLink?: boolean
  /** What the retry button says. Defaults to «Yenidən skan et»; a QR-less check-in has no poster to
   *  scan, and telling somebody to scan again sends them looking for one. */
  retryLabel?: string
  /** Past days left open (checked in, never out). Shown as the running COST of forgetting to scan
   *  out — those days count as zero hours. Information only: nothing is auto-closed, nothing asked. */
  openDays?: number
  /** Offer to switch the checkout reminder on right here (check-in only). */
  offerPush?: boolean
}
type Phase = 'scanning' | 'intro' | 'photo' | 'recheck' | 'processing' | 'done'

// The employee reads what is about to happen before the front camera opens. Auto-advances so a
// hesitant person cannot block the queue by never tapping "Hazıram".
const INTRO_MS = 6000

// How long the front-camera preview stays up after the first real frame. It has to outlast reading
// the words on screen and settling into position — at 1.2s the shot was taken before people had even
// looked up. A ring and a seconds counter run alongside so nobody is surprised. There is still no
// shutter button to press: the photo is taken automatically at zero. 5s felt too long in daily use;
// 2s is the deliberate trade-off — long enough to look up, short enough not to hold the queue.
const PHOTO_HOLD_MS = 1_500
// Keep the middle of the frame. The person holding the phone is centred; the queue behind them is
// not, and full-frame captures kept picking up two and three faces.
// A face-shaped (portrait) crop + output, matching the oval preview the employee sees.
// Tight (was 0.92): zoom into the face so it fills the photo instead of a small, far figure. The
// preview video is scaled by SELFIE_PREVIEW_ZOOM to show roughly the same framing (WYSIWYG); the crop
// is kept a touch tighter than the preview so no bystander the employee didn't see slips in.
const PHOTO_CROP = 0.68
const SELFIE_PREVIEW_ZOOM = 1.4
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

// Load the QR library on demand, once. Kicked off at mount (in parallel with the device + GPS checks)
// so it's ready before the camera opens, and awaited at the actual point of use as a safety net. The
// promise is cached so repeated scans never re-fetch. Failure to load surfaces as a normal camera
// error, exactly like a getUserMedia failure would.
let scannerModule: Promise<typeof import('html5-qrcode')> | null = null
function loadScanner(): Promise<typeof import('html5-qrcode')> {
  if (!scannerModule) scannerModule = import('html5-qrcode')
  return scannerModule
}

export function ScanPage() {
  const navigate = useNavigate()
  const scannerRef = useRef<Html5Qrcode | null>(null)
  // Photo audit: a hidden <video> we briefly attach the front camera to — on demand, AFTER the QR
  // (back) camera is released, since iOS Safari allows only one camera at a time — to grab a single
  // silent selfie frame at check-in. Best-effort: if the front camera is unavailable we skip the
  // photo and the check-in proceeds unaffected.
  const selfieVideoRef = useRef<HTMLVideoElement | null>(null)
  const busyRef = useRef(false)
  // Camera-noise filter — see lib/qrShape.ts. The detector lives in a ref because it is per
  // camera session, not per render; `foreignQr` is the one bit the UI needs.
  const foreignRef = useRef(new ForeignQrDetector())
  const [foreignQr, setForeignQr] = useState(false)
  // True while this page is mounted — so a start() that resolves AFTER the employee tapped "Bağla"
  // releases the camera instead of leaving it held (a held camera is what turns the next scan black).
  const mountedRef = useRef(true)
  // A start is in flight — blocks a second overlapping start() from stacking a second video (and a
  // second, black stream) onto #reader.
  const startingRef = useRef(false)
  // True while a scan result is on screen — keeps the today-status reload (which flips today.kind)
  // from re-running the camera effect and wiping the result message. Cleared when scanning restarts.
  const scanDoneRef = useRef(false)
  // Notification gate. 'undecided' until today's status is known — only then can we tell a check-IN
  // (where the ask belongs) from a check-OUT (where it is pure friction). Rationed further by
  // shouldShowPushGate: once a day, and only for the first few days.
  const [pushGate, setPushGate] = useState<'undecided' | 'show' | 'skip'>('undecided')
  const [phase, setPhase] = useState<Phase>('scanning')
  const [cameraError, setCameraError] = useState<CameraFailKind | null>(null)
  // Torch (flashlight) — only some phones/browsers expose it on the back camera. `torchAvailable`
  // gates the button so it never shows where it can't work (iOS Safari, most laptops); `torchOn`
  // tracks its state. For a gardener scanning a poster at 08:00 in winter dark this is the difference
  // between a scan and a phone call.
  const [torchAvailable, setTorchAvailable] = useState(false)
  const [torchOn, setTorchOn] = useState(false)
  const [result, setResult] = useState<Card | null>(null)
  const [today, setToday] = useState<TodayInfo>({ kind: 'loading' })
  const [geo, setGeo] = useState<GeoState>({ kind: 'checking' })
  // Seconds left on the GPS wait, shown on the checklist. Only appears once the fix is taking long
  // enough to be worth explaining — a countdown that starts at 45 makes a two-second check look slow.
  const [geoWait, setGeoWait] = useState<number | null>(null)
  // The visible pre-scan verification (device → location → camera). An overlay while it runs.
  // Starts false: runChecks turns it on the moment the checks really begin. It used to start true, so
  // any wait before that (today's status loading, the notification gate) showed a checklist with three
  // dead rows and no progress — which reads as a frozen app.
  const [verifying, setVerifying] = useState(false)
  const [checks, setChecks] = useState<ScanChecks>({ device: 'idle', location: 'idle', camera: 'idle' })
  // Set when the geofence pre-check finds the employee outside their workplace radius — surfaced
  // before scanning (with a "scan anyway" escape, since the QR's own location is the final word).
  const [radiusFail, setRadiusFail] = useState<{ distance: number; name: string } | null>(null)
  // Which company branch the phone is standing at, once the pre-check has found one. Named so the
  // screen can say «Nərimanov Ofis filialındasınız» — the sentence that tells somebody sent to
  // another site that scanning here is fine.
  const [atBranch, setAtBranch] = useState<string | null>(null)
  // A QR-less branch, fence passed: the screen STOPS here and waits for a tap. Auto-navigate from home
  // plus an intro that advances by itself plus an auto-captured selfie would otherwise record a
  // check-in that nobody chose — open the app in the yard forty minutes early to read an announcement,
  // and you have arrived. At a poster the deliberate act is aiming the camera; this button is its
  // equivalent. `me` is carried with it because the profile state may not have rendered yet.
  const [qrlessReady, setQrlessReady] = useState<{ me: MyProfile | null; branch: string | null } | null>(null)
  // Neither the profile nor the phone's memory of it answered in time: the camera opens (the default
  // every scan has ever had), and one line under it says why a person with no poster sees one.
  const [branchUnknown, setBranchUnknown] = useState(false)
  // True once the front camera is actually producing frames, so the preview says "look at the
  // camera" rather than showing a black circle while it warms up.
  const [photoLive, setPhotoLive] = useState(false)
  // Set when the phone found no face in the selfie. The check-in still goes through — this only
  // offers a retake, because a camera that refuses to record attendance costs someone a day's pay.
  const [profile, setProfile] = useState<MyProfile | null>(null)
  // The branch decides whether there is a poster to scan at all, so the pre-check must know the
  // profile BEFORE it opens the QR camera — and the profile arrives on its own request. A promise
  // rather than the state: runChecks starts the moment today's status is known, which can be before
  // that request returns, and losing that race would open a QR camera at a branch that has no QR.
  const profileReadyRef = useRef<Promise<MyProfile | null>>(Promise.resolve(null))
  const [noFacePhoto, setNoFacePhoto] = useState<string | null>(null)
  const [recheckMode, setRecheckMode] = useState<'ask' | 'final'>('ask')
  const recheckChoiceRef = useRef<((retake: boolean) => void) | null>(null)
  const photoProgress = useCaptureProgress(photoLive, PHOTO_HOLD_MS)
  const secondsLeft = Math.max(1, Math.ceil(((1 - photoProgress) * PHOTO_HOLD_MS) / 1000))

  // Resolves the intro screen early when the employee taps "Hazıram"; the timeout resolves it anyway.
  const introSkipRef = useRef<(() => void) | null>(null)
  // Captures the selfie the instant the employee taps "Çək", instead of waiting the full hold.
  const captureNowRef = useRef<(() => void) | null>(null)
  const introProgress = useCaptureProgress(phase === 'intro', INTRO_MS)
  const introSecondsLeft = Math.max(1, Math.ceil(((1 - introProgress) * INTRO_MS) / 1000))

  // Track mount so an in-flight camera start can bail if the employee already left the screen.
  useEffect(() => {
    mountedRef.current = true
    // Send any scan failure that was captured while the phone was offline last time.
    void flushFailures()
    // Unlock the success chime on the first touch, so it can sound when the scan completes.
    primeFeedbackOnGesture()
    // Start fetching the QR library now, while the device/GPS checks run — so it's ready by the time
    // the camera opens instead of adding a wait there. Fire-and-forget; startCamera awaits it anyway.
    void loadScanner()
    return () => {
      mountedRef.current = false
      void stopCamera()
    }
  }, [])

  // Today's status decides whether the camera should even start — no point opening it if the
  // day is already complete (the backend would just reject with AlreadyCompleted anyway).
  useEffect(() => {
    void loadTodayStatus()
    // Photo settings: whether this employee is exempt, and whether their last check-in showed no
    // face. Best-effort — a failure here must not delay or block the scan, so nothing awaits it.
    profileReadyRef.current = getMyProfile()
      .then((r) => {
        const me = r.status === 200 && r.data && 'fullName' in r.data ? r.data : null
        if (me) {
          setProfile(me)
          // The branch fact, kept on the phone for the next open with no signal.
          rememberQrless(decodeJwt(getToken() ?? '')?.sub ?? null, me.qrlessCheckIn === true)
        }
        return me
      })
      // TOTAL — it can never reject. The pre-check awaits this before the camera, and a rejected
      // promise there threw out of runChecks and left every offline phone at every poster branch
      // with a black reader and no retry: the exact regression of the offline queue this app was
      // built to prevent. A failed profile means "not known", never "not scannable".
      .catch(() => null)
  }, [])

  // Decide the gate as soon as today's status is known: ask only before a check-IN, and only when the
  // day/age allowance permits. Everything else skips straight to the scanner.
  useEffect(() => {
    if (today.kind === 'loading' || pushGate !== 'undecided') return
    setPushGate(today.kind === 'none' && shouldShowPushGate() ? 'show' : 'skip')
  }, [today.kind, pushGate])

  // Run the pre-scan verification once today's status is known (and re-run on an explicit retry).
  // The day being already complete needs no camera at all.
  useEffect(() => {
    if (today.kind === 'loading') return
    if (today.kind === 'completed') {
      setVerifying(false)
      void stopCamera()
      return
    }
    // A scan just finished and its result is on screen — don't re-verify (that would wipe it). The
    // employee restarts via "Yenidən skan et", which calls runChecks itself.
    if (scanDoneRef.current) return
    // Wait for the notification gate. runChecks ends by attaching html5-qrcode to the reader element,
    // which is hidden while the gate is up — attaching then fails and, since nothing re-ran when the
    // gate closed, the camera never opened at all. Depending on pushGate makes it start the moment
    // the gate goes away.
    if (pushGate !== 'skip') return
    void runChecks()
    return () => {
      void stopCamera()
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [today.kind, pushGate])

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
    setAtBranch(null)
    setQrlessReady(null)
    setBranchUnknown(false)
    setPhase('scanning')
    scanDoneRef.current = false
    busyRef.current = false
    setGeo({ kind: 'checking' })
    setChecks({ device: 'run', location: 'run', camera: 'idle' })

    // Device and GPS don't depend on each other — run them together so the employee waits for the
    // slower of the two, not the sum. Both are advisory-tolerant: a slow/failed device check passes.
    const [dev, position] = await Promise.all([
      Promise.race([
        getMyDeviceStatus(getDeviceFingerprint()).catch(() => null),
        delay(3000).then(() => null),
      ]),
      getPosition((left) => setGeoWait(left <= 38 ? left : null)),
    ])
    setGeoWait(null)

    // 1) Device — advisory only; null (slow/failed) is treated as a pass.
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
    // Every branch the scan would accept. The server has always taken any branch of the same company
    // (the QR names one, and the geofence is checked against THAT), so the pre-check must judge
    // against all of them too — or it contradicts the server. Older servers send only `location`.
    const branches =
      dev && dev.status === 200 && dev.data && 'locations' in dev.data && dev.data.locations?.length
        ? dev.data.locations
        : assignedLocation ? [assignedLocation] : []
    setChecks((c) => ({ ...c, device: deviceStep }))
    if (deviceStep === 'fail') {
      await delay(400)
      setVerifying(false)
      setResult({ tone: 'red', title: 'Cihaz ləğv edilib', detail: 'Administrator ilə əlaqə saxlayın.', final: true })
      setPhase('done')
      scanDoneRef.current = true
      return
    }

    // 2) Location — the real gate. A failure shows GpsHelp.
    if (!position.ok) {
      setChecks((c) => ({ ...c, location: 'fail' }))
      setGeo({ kind: 'failed', fail: position.kind })
      reportFailure(FAILURE_REASON[position.kind])
      await delay(900)
      setVerifying(false)
      return
    }
    const accuracy = Math.round(position.coords.accuracy)
    setGeo({ kind: 'ready', accuracy })
    if (accuracy > POOR_ACCURACY_METERS) reportFailure('GpsInaccurate', accuracy)

    // Geofence pre-check: is the phone inside ANY company branch? Caught here so they don't scan and
    // get rejected. The scan still checks against the QR's own location server-side, so this is
    // advisory — a "scan anyway" escape stays for the case the phone's position is simply wrong.
    //
    // It used to compare against the ASSIGNED branch only. A worker sent to help at another site
    // opened the app and read «İş yerində deyilsiniz» with a distance to a branch they were not at;
    // there was a "scan anyway" link under it, and nobody pressed it — the sentence had already told
    // them scanning was not allowed, so they went home unrecorded. Now the nearest branch decides,
    // and when they are inside one the screen names it instead.
    // The fence the phone is in, for the routing below — null when there is no branch list at all.
    let hit: { id: string | null; name: string } | null = null
    if (branches.length > 0) {
      const ranked = branches
        .map((b) => ({
          ...b,
          dist: Math.round(distanceMeters(position.coords.latitude, position.coords.longitude, b.latitude, b.longitude)),
        }))
        .sort((a, b) => a.dist - b.dist)
      const inside = ranked.find((b) => b.dist <= b.radiusMeters)
      hit = inside ? { id: inside.id ?? null, name: inside.name } : null
      if (!inside) {
        const nearest = ranked[0]
        setChecks((c) => ({ ...c, location: 'fail' }))
        await delay(900)
        setVerifying(false)
        setRadiusFail({ distance: nearest.dist, name: nearest.name })
        return
      }
      setAtBranch(inside.name)
    }
    setChecks((c) => ({ ...c, location: 'ok', camera: 'run' }))

    // No poster at this branch: there is nothing to point the camera at. The branch itself says so
    // (profile.qrlessCheckIn), the fence has just passed, and from here the flow is the SAME one a
    // decoded QR would enter — selfie, retake check, submit — with no token for the server to read.
    //
    // Three things this wait is NOT: unbounded (3 s, like the device check — a wedged profile request
    // must not hold the camera shut for 640 people at 08:00), fatal (the promise cannot reject), or the
    // only source (the phone remembers the fact from the last profile it saw, so a QR-less worker with
    // no signal still gets the selfie path). And the route is decided by PLACE, the way the server
    // decides it — see qrlessRoute — so a driver helping at a branch that HAS a poster gets the camera.
    const myId = decodeJwt(getToken() ?? '')?.sub ?? null
    const me = await Promise.race([profileReadyRef.current, delay(3000).then(() => null)])
    const known = me ? me.qrlessCheckIn === true : recallQrless(myId)
    if (known === null) setBranchUnknown(true)
    if (qrlessRoute({ known, ownLocationId: me?.locationId, insideId: hit?.id }) === 'selfie') {
      setChecks((c) => ({ ...c, camera: 'ok' }))
      setVerifying(false)
      setQrlessReady({ me, branch: hit?.name ?? null })
      return
    }

    // 3) Camera — the reader is now visible (phase 'scanning' + geo ready), so html5-qrcode attaches.
    await new Promise((r) => requestAnimationFrame(() => r(null)))
    await startCamera()
    await delay(150)
    if (scannerRef.current) {
      setChecks((c) => ({ ...c, camera: 'ok' }))
      await delay(200)
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
    // The server is the final word on the fence either way; at a QR-less branch it is reached
    // without a poster, so the escape hatch offers the same «Giriş et» button — still a tap.
    const myId = decodeJwt(getToken() ?? '')?.sub ?? null
    const known = profile ? profile.qrlessCheckIn === true : recallQrless(myId)
    if (qrlessRoute({ known, ownLocationId: profile?.locationId, insideId: null }) === 'selfie')
      setQrlessReady({ me: profile, branch: null })
    else void startCamera()
  }

  /**
   * The day, corrected by what is still sitting on this phone.
   *
   * Every fallback in loadTodayStatus lands on 'none' — a timeout, a non-200, a thrown fetch — which
   * is right when the server genuinely has nothing, and wrong in the one case that matters: the
   * employee checked in with no signal, so the check-in is in IndexedDB and the server has never
   * heard of it. The screen then treated their CHECK-OUT as a check-in and asked for a selfie, which
   * is what got reported.
   *
   * Only ever 'none' → 'in-progress'. It must NOT go on to 'completed', however many taps are
   * queued: 'completed' stops the camera, so a queued check-out the server later declines — too soon
   * after the check-in, wrong device — would leave somebody standing at the poster unable to scan at
   * all. The rule is that a scan is never blocked by anything optional, and a guess about a reply
   * that has not arrived is as optional as it gets. Guessing "at work" costs at most a selfie nobody
   * needed; guessing "finished" costs a day's pay.
   */
  function withQueued(info: TodayInfo, queued: QueuedScan[]): TodayInfo {
    if (info.kind !== 'none') return info
    const today = todayStr()
    const first = queued
      .map((q) => q.clientTimestampUtc)
      .filter((t) => t.slice(0, 10) === today)
      .sort()[0]
    return first ? { kind: 'in-progress', checkInAtUtc: first } : info
  }

  async function loadTodayStatus() {
    // Scoped to the signed-in account, exactly as the drain is: on a shared brigade phone the queue
    // holds other people's taps, and reading them as this employee's would be the same
    // misattribution the queue's own employeeId stamp exists to prevent.
    const queued = await scansFor(decodeJwt(getToken() ?? '')?.sub ?? null).catch(() => [] as QueuedScan[])
    const settle = (info: TodayInfo) => setToday(withQueued(info, queued))
    try {
      // Bounded: a request that never settles used to leave `today` on 'loading' forever, and the
      // whole scan flow waits on that — the screen simply never moved. Falling back to 'none' lets the
      // checks (and the camera) start; the server is the authority on check-in vs check-out anyway.
      // Only today's row — a single indexed lookup, so history size no longer delays camera-start.
      const res = await Promise.race([
        getMyToday(),
        delay(8000).then(() => null),
      ])
      if (!res) {
        settle({ kind: 'none' })
        return
      }
      const { status, data } = res
      if (status !== 200) {
        settle({ kind: 'none' })
        return
      }
      settle(recordToTodayInfo(data ?? undefined))
    } catch {
      settle({ kind: 'none' })
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
          const { Html5Qrcode } = await loadScanner()
          // A fresh camera session starts with a clean slate — a hint earned aiming at last
          // attempt's wrong code must not greet this one.
          foreignRef.current.reset()
          setForeignQr(false)
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
          const kind = cameraFailKind(err)
          setCameraError(kind)
          // Surface it to the admin's Problems screen — a phone whose camera won't open is a scan that
          // silently never happened, otherwise visible only as a phone call. The KIND goes with it:
          // these all used to arrive as one 'CameraBlocked', so the screen could say a scan failed but
          // never why, and "have you allowed the camera?" was the only answer anyone could give.
          reportFailure(CAMERA_FAIL_REASON[kind])
          return
        }

        // Left the screen while start() was in flight — release the camera and stop.
        if (!mountedRef.current) {
          await stopCamera()
          return
        }

        // start() resolved; confirm the stream is actually producing frames, not sitting black.
        if (await waitForReaderFrame(3500)) {
          detectTorch() // the track is live now — see whether it can light the flash
          return // success — scannerRef stays set
        }

        // Black. Tear down and loop; after the last attempt fall through to the CameraHelp below.
        await stopCamera()
      }
      if (mountedRef.current) setCameraError('inuse')
    } finally {
      startingRef.current = false
    }
  }

  // Whether the running back-camera track can toggle its torch. Best-effort: getRunningTrackCapabilities
  // throws if the scanner isn't running, and many devices/browsers simply don't report `torch`.
  function detectTorch() {
    try {
      const caps = scannerRef.current?.getRunningTrackCapabilities() as (MediaTrackCapabilities & { torch?: boolean }) | undefined
      setTorchAvailable(caps?.torch === true)
    } catch {
      setTorchAvailable(false)
    }
  }

  // Toggle the flash on the live scan track. Never touches the scan pipeline — a failure just hides
  // the button (the torch isn't usable) and scanning continues exactly as before.
  async function toggleTorch() {
    const scanner = scannerRef.current
    if (!scanner) return
    const next = !torchOn
    try {
      await scanner.applyVideoConstraints({ advanced: [{ torch: next }] } as unknown as MediaTrackConstraints)
      setTorchOn(next)
    } catch {
      setTorchAvailable(false)
      setTorchOn(false)
    }
  }

  async function stopCamera() {
    const scanner = scannerRef.current
    scannerRef.current = null
    // The torch dies with the track; forget it so a fresh start re-detects on the new stream.
    setTorchAvailable(false)
    setTorchOn(false)
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
      // Auto-capture after the hold, OR the moment the employee taps "Çək" — so a ready person doesn't
      // wait out the full window, while a hesitant one is still captured automatically.
      await new Promise<void>((resolve) => {
        let done = false
        const finish = () => {
          if (done) return
          done = true
          clearTimeout(timer)
          captureNowRef.current = null
          resolve()
        }
        const timer = setTimeout(finish, PHOTO_HOLD_MS)
        captureNowRef.current = finish
      })
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

  /** Shows the retake prompt and resolves with what they chose. Never rejects — either answer
   *  continues the check-in. In 'final' mode there is nothing left to choose: it states what will be
   *  recorded and waits for an acknowledgement. */
  function askForRetake(photo: string, mode: 'ask' | 'final'): Promise<boolean> {
    return new Promise((resolve) => {
      setNoFacePhoto(photo)
      setRecheckMode(mode)
      setPhase('recheck')
      recheckChoiceRef.current = (retake) => {
        recheckChoiceRef.current = null
        setNoFacePhoto(null)
        resolve(retake)
      }
    })
  }

  async function onDecoded(text: string) {
    if (busyRef.current) return
    // A decode that does not even have the token's shape never leaves the phone. No error, no
    // stopped camera — the next frame is another chance, which is how a scanner should feel. It
    // used to walk the worker through the whole selfie flow and then fail red on the server's
    // «TokenMalformed» (Bibiheybət, 17 cəhd bir səhərdə). Only a STABLE read of the same foreign
    // code earns a hint, and even then the camera keeps running — the right poster is usually a
    // hand's width away.
    if (!looksLikeQrToken(text)) {
      if (foreignRef.current.seen(text)) setForeignQr(true)
      return
    }
    await proceed(text)
  }

  /**
   * Everything after the poster has been read — or, at a QR-less branch, in place of reading one.
   * `token` is '' for QR-less: the server then uses the employee's own branch (Location.QrlessCheckIn).
   * `me` is passed explicitly by the pre-check because it may run before the profile state has
   * rendered; the decoded-QR path leaves it to the state it already has.
   */
  async function proceed(token: string, me: MyProfile | null = profile) {
    setQrlessReady(null)
    busyRef.current = true
    foreignRef.current.reset()
    setForeignQr(false)
    // Release the QR (back) camera FIRST — iOS Safari won't open the front camera while another is
    // active. Then show feedback and grab the selfie (check-in only; check-out never captures one).
    await stopCamera()
    // An exempted employee never sees the camera: opening one and then throwing the frame away
    // would only teach everyone watching that the step is skippable.
    const willPhotograph = today.kind === 'none' && me?.photoRequired !== false
    if (willPhotograph) {
      setPhase('intro')
      await waitForIntro()
      setPhase('photo')
    } else {
      setPhase('processing')
    }
    let photoBase64 = willPhotograph ? await captureSelfie() : null

    // Ask on the spot, while they are still standing there with the phone in their hand. Finding out
    // a week later in an audit changes nobody's habit — they don't remember the day, and by then the
    // photo is one of forty. Capped at one retake: a second prompt reads as nagging, and anyone
    // pointing the camera away on purpose has already got the message.
    // The check can take a second or two over a weak connection; leaving the capture screen up would
    // read as "the camera is still working" while nothing is happening.
    if (photoBase64) setPhase('processing')

    if (photoBase64 && (await checkForFace(photoBase64)) === 'noface') {
      if (await askForRetake(photoBase64, 'ask')) {
        setPhase('photo')
        photoBase64 = (await captureSelfie()) ?? photoBase64
        setPhase('processing')
        // The retake is checked too. A second faceless photo is a deliberate one, and letting it
        // through in silence reads as the system giving up — which is how this spread in the first
        // place. No third prompt: they are told what was recorded, and the check-in proceeds.
        if ((await checkForFace(photoBase64)) === 'noface')
          await askForRetake(photoBase64, 'final')
      }
    }

    setPhase('processing')
    await submitScan(token, photoBase64)
    setPhase('done')
    // Keep the result on screen: mark done so reloading today's status doesn't restart the camera.
    scanDoneRef.current = true
    void loadTodayStatus()
  }

  async function submitScan(qrToken: string, photoBase64: string | null) {
    // '' is the QR-less check-in — no poster was read, the server uses the employee's own branch.
    const qrless = qrToken === ''
    // Warmed by the pre-flight check, so this normally returns a cached fix immediately. If the
    // employee revoked the permission between the two, fall back to the same instructions.
    const position = await getPosition()
    if (!position.ok) {
      reportFailure(FAILURE_REASON[position.kind])
      setGeo({ kind: 'failed', fail: position.kind })
      setResult(null)
      return
    }
    const coords = position.coords

    // One id per tap, sent even on the first (online) try: if the response is lost and the scan is
    // later re-sent from the offline queue, the server de-duplicates on this id instead of recording
    // a second check-in. clientTimestampUtc is only used by the server if the scan syncs offline.
    const clientScanId = crypto.randomUUID()
    const clientTimestampUtc = new Date().toISOString()

    // Saves the tap on the device — same id, same selfie, same timestamp — so the replay is the SAME
    // scan, not a new one. Shared by every path where the server could not judge the scan: network
    // down, request timed out, or the server itself answering 502/503/504 (a deploy window used to
    // reject these taps outright — the one way a real clock-in could still be lost).
    async function saveLocally(reason: 'network' | 'server') {
      try {
        await enqueueScan({
          clientScanId,
          qrToken,
          deviceFingerprint: getDeviceFingerprint(),
          latitude: coords.latitude,
          longitude: coords.longitude,
          photoBase64: photoBase64 ?? undefined,
          clientTimestampUtc,
          queuedAtMs: Date.now(),
          // Stamp WHOSE scan this is. The queue is device storage, not session storage, so on a
          // shared site phone the next person to sign in would otherwise replay this as their own.
          // '?? unknown' rather than undefined: undefined means "queued before this field existed"
          // and is replayable by anyone, so a momentary decode failure must not mint a fresh unowned
          // item and reopen the misattribution this stamp closes.
          employeeId: decodeJwt(getToken() ?? '')?.sub ?? 'unknown',
        })
        // Saved on the device IS a success for the employee — same confident buzz as a live scan.
        successFeedback()
        setResult({
          tone: 'green',
          // Lead with what HAPPENED, not with what broke.
          //
          // It used to open «Serverlə əlaqə müvəqqəti kəsilib» — an accurate sentence about our
          // infrastructure, printed in the largest text on a screen belonging to somebody who only
          // wants to know whether they are marked present. They read a fault report and came to the
          // office asking whether there was a problem. There isn't: the tap is saved and will be
          // sent, which is the whole point of the queue, so that is the headline now.
          //
          // The cause stays on the card, at the bottom, in the quiet line — it is true, it matters
          // when someone reports a bad day, and the distinction is real: a backend down behind a
          // live proxy answers 502 without CORS headers, which the browser raises as the same
          // exception as no signal at all.
          title: 'Qeyd olundu ✓',
          detail: 'Skan telefonunuzda saxlanıldı.',
          note: reason === 'server'
            ? 'İnternet qayıdanda özü göndəriləcək — heç nə itmir. Tətbiqi bağlaya bilərsiniz. (Server müvəqqəti əlçatmazdır.)'
            : 'İnternet qayıdanda özü göndəriləcək — heç nə itmir. Tətbiqi bağlaya bilərsiniz. (Əlaqə kəsilib.)',
          final: true,
          photo: photoBase64 ?? undefined,
        })
      } catch {
        // Both the scan AND the offline save failed — the worst case, and the one that used to leave
        // no trace at all. Queue a failure report so it reaches the Problems screen once back online.
        reportFailure('NetworkError')
        errorFeedback()
        setResult({ tone: 'red', title: 'Şəbəkə xətası', detail: 'Serverə qoşulmaq mümkün olmadı.' })
      }
    }

    try {
      const { status, data } = await apiRequest<ScanResponse>('/api/attendance/scan', {
        method: 'POST',
        // A wedged connection must become a queued scan, not an endless spinner: past this deadline
        // the fetch throws and the catch below saves the tap. The server records in ~40ms — anything
        // near 20s is infrastructure, and the idempotency id makes the later replay safe even if the
        // server DID write before the deadline hit.
        timeoutMs: 20_000,
        body: {
          qrToken,
          deviceFingerprint: getDeviceFingerprint(),
          latitude: coords.latitude,
          longitude: coords.longitude,
          // Omit entirely when there's no photo so the field stays optional on the wire.
          ...(photoBase64 ? { photoBase64 } : {}),
          clientScanId,
        },
      })

      // The server did not JUDGE the scan — it was simply not there to answer (deploy window,
      // crashed backend behind the proxy). Same treatment as no network at all.
      if (isServerUnavailable(status)) {
        await saveLocally('server')
        return
      }

      if (status === 200 && data?.action === 'CheckIn') {
        successFeedback()
        const face = faceLine(data.faceMatch ?? (qrless ? 'Pending' : undefined))
        setResult({
          tone: 'green',
          title: 'Giriş qeydə alındı',
          detail: `Saat ${fmtTime(data.checkInAtUtc, '')}${face.tick ? ` · ${face.tick}` : ''}`,
          note: qrless
            ? 'İş bitəndə çıxış üçün yenidən «Çıxış et»ə toxunun.'
            : 'İş bitəndə çıxış üçün yenidən skan edin.',
          // A face that did not verify outranks lateness: it is the rarer and the graver notice, and
          // it is the one the manager will be looking at. Otherwise just tell them they were late (vs
          // their own hours, else the location's) — no reason asked.
          warn: face.warn ?? (data.late ? 'Gecikdiniz' : undefined),
          final: true,
          photo: photoBase64 ?? undefined,
          openDays: data.openDays,
          // Check-in is the moment to ask: they're at work, looking at the screen, and the reminder
          // they're being offered fires later the same day.
          offerPush: true,
        })
        return
      }
      if (status === 200 && data?.action === 'CheckOut') {
        successFeedback()
        const worked = data.recordId ? await workedDurationText(data.recordId) : undefined
        setResult({
          tone: 'green',
          title: 'Çıxış qeydə alındı',
          detail: worked ?? `Saat ${fmtTime(data.checkOutAtUtc, '')}`,
          note: 'Sabaha qədər!',
          warn: data.earlyDeparture ? 'Tez çıxdınız' : undefined,
          final: true,
          // Offered here too — more chances to get it switched on; it self-hides once it is.
          offerPush: true,
        })
        return
      }
      const card = errorResult(status, data, coords.accuracy)
      // A hard rejection (wrong device, inactive account) buzzes so it's felt; soft/yellow states
      // (QR expired, "too soon") stay silent — they aren't failures worth a jolt.
      if (card.tone === 'red') errorFeedback()
      setResult(
        qrless && data?.error === 'TokenMalformed'
          // The server no longer treats this branch as poster-less (the flag was switched off, or this
          // phone's profile is stale). Named plainly, with the one person who can put it right — the
          // raw code «TokenMalformed» would only tell them the app is broken.
          ? {
              tone: 'red',
              title: 'Bu filialda üz ilə giriş bağlanıb',
              detail: 'Filialın QR-siz giriş ayarı dəyişib.',
              note: 'Rəhbərinizə deyin — ya poster asılmalı, ya da ayar geri açılmalıdır.',
              final: true,
            }
          : qrless ? { ...card, retryLabel: 'Yenidən cəhd et' } : card,
      )
    } catch {
      // No connection, or the 20s deadline fired — instead of failing, save the scan on the device
      // and sync it when the server is reachable again. GPS + selfie were already captured, so
      // nothing is lost; only the round-trip is deferred.
      await saveLocally('network')
    }
  }

  // Only while actually scanning — the QR frame must give way to the selfie preview, not sit behind it.
  // Don't open the camera behind the notification gate — nothing should be filming while the employee
  // is looking at a permission prompt.
  const showCamera =
    pushGate === 'skip' && today.kind !== 'loading' && today.kind !== 'completed' && geo.kind === 'ready' && phase === 'scanning' && !cameraError && !radiusFail && !qrlessReady

  return (
    <div className="relative min-h-screen flex flex-col bg-[#080C14] text-white overflow-hidden selection:bg-blue-500/30">
      {/* QRLog Ambient background light glow */}
      <div className="pointer-events-none absolute -top-40 left-1/2 -translate-x-1/2 h-96 w-96 rounded-full bg-blue-600/15 blur-[110px]" />
      <div className="pointer-events-none absolute bottom-0 right-0 h-80 w-80 rounded-full bg-indigo-600/10 blur-[130px]" />

      <header className="sticky top-0 z-20 flex items-center justify-between border-b border-white/[0.06] bg-[#080C14]/75 px-5 py-3.5 backdrop-blur-xl">
        <div className="flex items-center gap-2.5">
          <span className="relative flex h-2.5 w-2.5">
            <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-blue-400 opacity-70" />
            <span className="relative inline-flex h-2.5 w-2.5 rounded-full bg-blue-500" />
          </span>
          <span className="text-sm font-bold tracking-tight text-white">QR Skan</span>
        </div>
        <button
          onClick={() => navigate('/home')}
          className="rounded-full border border-white/10 bg-white/[0.05] px-4 py-1.5 text-xs font-semibold text-slate-300 backdrop-blur-md transition-all hover:bg-white/10 hover:text-white active:scale-95 cursor-pointer"
        >
          Bağla
        </button>
      </header>

      <main className="relative flex-1 flex flex-col items-center justify-center p-4 gap-5">
        {/* Notifications are asked for here, before the scanner — the scan is the only moment an
            employee opens the app, so it's the only moment this can be asked. An overlay rather than a
            branch, so the page underneath is untouched; it steps aside by itself where push cannot work
            (iOS Safari tab, previously refused) rather than blocking someone out of recording work. */}
        {pushGate === 'show' && (
          <div className="absolute inset-0 z-30 flex items-center justify-center bg-slate-950/95 p-4 backdrop-blur-xl">
            <PushGate onDone={() => setPushGate('skip')} />
          </div>
        )}

        {verifying && today.kind !== 'completed' && (
          <ScanChecklist
            checks={checks}
            waitingHint={geoWait !== null ? `Peyk siqnalı gözlənilir — ${geoWait} san. Açıq yerdə dayanın.` : null}
          />
        )}

        <TodayBanner today={today} />

        {today.kind === 'completed' && (
          <div className="relative w-full max-w-sm overflow-hidden rounded-3xl border border-blue-500/30 bg-gradient-to-b from-blue-950/80 to-slate-900/90 p-6 text-center text-white shadow-2xl backdrop-blur-2xl">
            <div className="mx-auto mb-3 flex h-14 w-14 items-center justify-center rounded-2xl border border-blue-400/30 bg-blue-500/20 text-2xl font-black text-blue-400 shadow-[0_0_20px_rgba(37,99,235,0.3)]">
              ✓
            </div>
            <h2 className="text-xl font-extrabold text-white">Bu gün tamamlandı</h2>
            <p className="mt-2 text-sm text-slate-300 font-medium">
              {fmtTime(today.checkInAtUtc, '')} – {fmtTime(today.checkOutAtUtc, '')}
              {' · '}
              <span className="text-blue-400 font-bold">{formatDuration(minutesBetween(today.checkInAtUtc, today.checkOutAtUtc))}</span>
            </p>
          </div>
        )}

        {phase === 'intro' && (
          <PhotoIntro
            secondsLeft={introSecondsLeft}
            onReady={() => introSkipRef.current?.()}
            lastUnverified={profile?.lastCheckInUnverified === true}
          />
        )}

        {today.kind !== 'completed' && geo.kind === 'failed' && (
          <GpsHelp kind={geo.fail} onRetry={() => void runChecks()} />
        )}

        {radiusFail && (
          <div className="relative w-full max-w-sm overflow-hidden rounded-3xl border border-rose-500/30 bg-gradient-to-b from-rose-950/70 to-slate-900/90 p-6 text-center shadow-2xl backdrop-blur-2xl">
            <div className="mx-auto mb-3 flex h-14 w-14 items-center justify-center rounded-2xl border border-rose-500/30 bg-rose-500/20 text-2xl text-rose-400 shadow-[0_0_20px_rgba(244,63,94,0.3)]">
              📍
            </div>
            <h2 className="text-lg font-extrabold text-white">Yeriniz təsdiqlənmədi</h2>
            {/* Same rewrite as the post-scan card (locationCard), and for the same reason: this one
                is shown BEFORE the QR is even read, so it is the first thing a worker standing at
                their post is told. It states what was measured, not a verdict on them, and shows the
                phone's own margin — which is usually the whole explanation. */}
            <p className="mt-2 text-xs font-medium text-slate-300 leading-relaxed">
              Ən yaxın filial <strong className="text-white font-bold">{radiusFail.name}</strong> — təxminən <strong className="text-white font-bold">{radiusFail.distance} m</strong> aralıda göründünüz
              {geo.kind === 'ready' && <> · GPS dəqiqliyi ±{geo.accuracy} m</>}.
            </p>
            <p className="mt-2 text-xs font-medium text-slate-400 leading-relaxed">
              {geo.kind === 'ready' && geo.accuracy > POOR_ACCURACY_METERS
                ? 'Telefon yerinizi dəqiq tapa bilmir. Açıq havaya çıxıb 10–15 saniyə gözləyin.'
                : 'İş yerindəsinizsə, açıq yerə çıxıb yenidən yoxlayın.'}
            </p>
            <button
              onClick={() => void runChecks()}
              className="mt-5 w-full rounded-2xl bg-gradient-to-r from-rose-500 to-rose-600 py-3.5 text-sm font-bold text-white shadow-lg shadow-rose-500/25 transition active:scale-[0.98] cursor-pointer"
            >
              Yenidən yoxla
            </button>
            <button
              onClick={scanAnyway}
              className="mt-2 w-full py-2 text-xs font-medium text-slate-400 transition hover:text-white cursor-pointer"
            >
              Yenə də skan et
            </button>
          </div>
        )}

        {/* The branch the phone is standing at. One line, and it answers the question people used
            to walk away over: "am I allowed to scan here?" — yes, and here is where "here" is. */}
        {showCamera && atBranch && (
          <div className="w-full max-w-sm rounded-2xl border border-emerald-500/30 bg-emerald-500/10 px-4 py-2.5 text-center text-xs font-semibold text-emerald-200 backdrop-blur-md">
            📍 {atBranch} filialındasınız — skan edə bilərsiniz
          </div>
        )}

        {/* No poster here — the one deliberate act a check-in needs is this button. */}
        {qrlessReady && phase === 'scanning' && !result && (
          <div className="relative w-full max-w-sm overflow-hidden rounded-3xl border border-emerald-500/30 bg-gradient-to-b from-emerald-950/60 to-slate-900/90 p-6 text-center shadow-2xl backdrop-blur-2xl">
            <div className="mx-auto mb-3 flex h-14 w-14 items-center justify-center rounded-2xl border border-emerald-500/30 bg-emerald-500/20 text-2xl">
              📍
            </div>
            <h2 className="text-lg font-extrabold text-white">
              {qrlessReady.branch ? `${qrlessReady.branch} filialındasınız` : 'Filialınızın ərazisindəsiniz'}
            </h2>
            <p className="mt-2 text-xs font-medium text-slate-300 leading-relaxed">
              Bu filialda QR posteri yoxdur.{' '}
              {today.kind === 'none' ? 'Giriş selfi və GPS ilə qeydə alınır.' : 'Çıxış GPS ilə qeydə alınır.'}
            </p>
            <button
              onClick={() => void proceed('', qrlessReady.me)}
              className="mt-5 w-full rounded-2xl bg-gradient-to-r from-emerald-500 to-teal-600 py-3.5 text-base font-bold text-white shadow-lg shadow-emerald-500/25 transition active:scale-[0.98] cursor-pointer"
            >
              {today.kind === 'none' ? 'Giriş et' : 'Çıxış et'}
            </button>
          </div>
        )}

        {/* The camera opened because the branch fact never arrived — say so, once, under it. */}
        {showCamera && branchUnknown && (
          <div className="w-full max-w-sm rounded-2xl border border-amber-500/30 bg-amber-500/10 px-4 py-3 text-center text-xs font-medium text-amber-200 backdrop-blur-md">
            Filial məlumatı yüklənmədi. Posteriniz yoxdursa, internet qayıdanda yenidən cəhd edin.
          </div>
        )}

        {/* Position obtained, but too coarse to sit comfortably inside a 150 m radius. Scanning is
            still allowed — this only nudges the employee somewhere with a clearer view of the sky. */}
        {showCamera && geo.kind === 'ready' && geo.accuracy > POOR_ACCURACY_METERS && (
          <div className="w-full max-w-sm rounded-2xl border border-amber-500/30 bg-amber-500/10 px-4 py-3 text-center text-xs font-medium text-amber-200 backdrop-blur-md">
            GPS dəqiqliyi zəifdir (±{geo.accuracy} m). Skan işləyəcək, amma açıq yerdə daha dəqiq olar.
          </div>
        )}

        {/* Camera container stays mounted so html5-qrcode can always find it. */}
        <div className={showCamera ? 'w-full max-w-sm' : 'hidden'}>
          <div id={READER_ID} className="w-full overflow-hidden rounded-3xl border border-white/10 bg-black shadow-2xl" />
          {/* The same wrong code, frame after frame — they are aiming at a QR that is not ours.
              A hint, not an error: the camera never stops. */}
          {foreignQr && (
            <div className="mt-3 rounded-2xl border border-amber-500/30 bg-amber-500/10 px-4 py-3 text-center text-xs font-medium text-amber-200">
              Bu kod QRLog posterinin kodu deyil — divardakı davamiyyət posterini skan edin
            </div>
          )}
          <p className="text-center text-xs font-medium text-slate-300 mt-3">QR kodu kameraya tutun</p>
          {today.kind === 'none' && (
            <p className="text-center text-[11px] text-slate-500 mt-1">Girişdə şəkil çəkilir</p>
          )}
          {/* Flashlight — shown only where the phone actually supports it (see detectTorch). For an
              early-morning scan in winter dark, this is what lets the camera see the poster at all. */}
          {torchAvailable && (
            <button
              onClick={() => void toggleTorch()}
              className={`mt-4 flex w-full items-center justify-center gap-2 rounded-2xl py-3.5 text-sm font-bold transition active:scale-[0.98] cursor-pointer ${
                torchOn ? 'bg-amber-400 text-slate-950 shadow-[0_0_20px_rgba(245,158,11,0.4)]' : 'border border-amber-400/30 bg-white/5 text-amber-300'
              }`}
            >
              🔦 {torchOn ? 'Fənəri söndür' : 'Fənəri yandır'}
            </button>
          )}
        </div>

        {/* Front-camera preview for the check-in selfie. Stays mounted (hidden) so captureSelfie()
            can always read a frame, and is SHOWN while capturing — the photo is disclosed, and an
            employee looking at the lens produces one clean face instead of the queue behind them.
            The circle matches the centre crop frameToJpeg() takes, so what they see is what is kept. */}
        <div className={phase === 'photo' ? 'flex w-full max-w-sm flex-col items-center gap-4' : 'hidden'}>
          <div className="relative h-64 w-52">
            {/* Oval (face-shaped) frame so the employee lines their face up inside it. */}
            <div className="h-full w-full overflow-hidden rounded-[50%] border-2 border-blue-500/40 bg-black shadow-[0_0_35px_rgba(37,99,235,0.3)]">
              <video
                ref={selfieVideoRef}
                className="h-full w-full object-cover"
                // Mirror (selfie) + zoom into the centre so the preview matches the tighter crop.
                style={{ transform: `scaleX(-1) scale(${SELFIE_PREVIEW_ZOOM})` }}
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
            <div className="w-full text-center space-y-2">
              <p className="text-xl font-extrabold text-white">Ekrana baxın</p>
              <p className="text-xs font-medium text-slate-300">Üzünüzü ovalın mərkəzinə salın və tərpənməyin</p>
              <p className="text-4xl font-black tabular-nums text-blue-400">{secondsLeft}</p>
              {/* Hazır olan dərhal çəksin — gözləməsin. Basmasa, sayğac özü çəkir. */}
              <button
                onClick={() => captureNowRef.current?.()}
                className="group relative mt-2 flex w-full items-center justify-center gap-2 overflow-hidden rounded-2xl bg-gradient-to-r from-blue-600 via-blue-500 to-indigo-600 py-3.5 text-base font-extrabold text-white shadow-[0_4px_25px_rgba(37,99,235,0.4)] transition-all active:scale-[0.98] cursor-pointer"
              >
                <svg className="h-5 w-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2.2} strokeLinecap="round" strokeLinejoin="round">
                  <path d="M23 19a2 2 0 0 1-2 2H3a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h4l2-3h6l2 3h4a2 2 0 0 1 2 2z" />
                  <circle cx="12" cy="13" r="4" />
                </svg>
                <span>Çək</span>
              </button>
            </div>
          ) : (
            <p className="text-sm font-medium text-slate-400 animate-pulse">Kamera hazırlanır…</p>
          )}
        </div>

        {phase === 'recheck' && noFacePhoto && (
          <div className="flex w-full max-w-sm flex-col items-center gap-4">
            <img
              src={noFacePhoto}
              alt=""
              className="h-40 w-40 rounded-2xl object-cover opacity-70"
            />
            {recheckMode === 'ask' ? (
              <>
                <p className="text-xl font-bold text-amber-400">⚠️ Üzünüz görünmür</p>
                <p className="text-center text-base text-slate-300">
                  Şəkildə üz aşkarlanmadı. Kameranı üzünüzə tutub yenidən çəkin.
                </p>
                <button
                  onClick={() => recheckChoiceRef.current?.(true)}
                  className="w-full rounded-2xl bg-white py-4 text-base font-bold text-slate-900"
                >
                  Yenidən çək
                </button>
                {/* Deliberately small and plain, never removed: a dark shift, a cracked lens or a
                    face the detector simply misses must not stop someone recording that they came
                    to work. */}
                <button
                  onClick={() => recheckChoiceRef.current?.(false)}
                  className="text-sm text-slate-400 underline underline-offset-4"
                >
                  Yenə də göndər
                </button>
              </>
            ) : (
              <>
                <p className="text-xl font-bold text-amber-400">Şəkil təsdiqlənmədi</p>
                <p className="text-center text-base text-slate-300">
                  Şəkildə yenə üz görünmür. Girişiniz qeydə alınacaq, lakin{' '}
                  <b className="text-amber-300">təsdiqlənməmiş</b> sayılacaq və rəhbərinizin
                  siyahısında görünəcək.
                </p>
                <button
                  onClick={() => recheckChoiceRef.current?.(false)}
                  className="w-full rounded-2xl bg-white py-4 text-base font-bold text-slate-900"
                >
                  Başa düşdüm
                </button>
              </>
            )}
          </div>
        )}

        {phase === 'processing' && (
          <p className="text-lg animate-pulse">Yoxlanılır…</p>
        )}

        {cameraError && <CameraHelp kind={cameraError} onRetry={() => void runChecks()} />}

        {phase === 'done' && result && (
          <ResultCard card={result} onRetry={() => void runChecks()} onClose={() => navigate('/home')} />
        )}
      </main>
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
      <path d={d} fill="none" stroke="rgba(255,255,255,0.12)" strokeWidth="5" />
      <path
        d={d}
        fill="none"
        stroke="#3b82f6"
        strokeWidth="5"
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
    <div className="inline-flex items-center gap-2 rounded-full border border-white/[0.08] bg-white/[0.04] px-4 py-1.5 text-xs font-medium text-slate-300 shadow-sm backdrop-blur-md">
      {today.kind === 'none' && (
        <>
          <span className="h-1.5 w-1.5 rounded-full bg-amber-400" />
          <span>Bu gün hələ giriş etməmisiniz</span>
        </>
      )}
      {today.kind === 'in-progress' && (
        <>
          <span className="h-1.5 w-1.5 rounded-full bg-emerald-400 animate-pulse" />
          <span>
            Giriş: <strong className="text-white font-semibold">{fmtTime(today.checkInAtUtc, '')}</strong> — hələ çıxış etməmisiniz
          </span>
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
  // While the notification ask is on screen it owns the primary button — "Bağla" shrinks to a link so
  // the obvious next tap is turning the reminder on, not dismissing it. Never blocks: skipping is
  // always one tap away, because a hard gate would lock out anyone on iOS Safari or who once refused.
  const [askingPush, setAskingPush] = useState(false)
  const isGreen = card.tone === 'green'
  const isYellow = card.tone === 'yellow'

  const icon = isGreen ? '✓' : isYellow ? '!' : '✕'

  return (
    <div
      className={`relative w-full max-w-sm overflow-hidden rounded-3xl border p-6 text-center shadow-2xl backdrop-blur-2xl transition-all ${
        isGreen
          ? 'border-emerald-500/30 bg-gradient-to-b from-emerald-950/80 via-slate-900/90 to-slate-900 text-white shadow-[0_25px_50px_rgba(16,185,129,0.15)]'
          : isYellow
          ? 'border-amber-500/30 bg-gradient-to-b from-amber-950/80 via-slate-900/90 to-slate-900 text-white shadow-[0_25px_50px_rgba(245,158,11,0.15)]'
          : 'border-rose-500/30 bg-gradient-to-b from-rose-950/80 via-slate-900/90 to-slate-900 text-white shadow-[0_25px_50px_rgba(244,63,94,0.15)]'
      }`}
    >
      <div
        className={`mx-auto mb-4 flex h-16 w-16 items-center justify-center rounded-2xl text-2xl font-black ${
          isGreen
            ? 'border border-emerald-500/40 bg-emerald-500/20 text-emerald-400 shadow-[0_0_20px_rgba(16,185,129,0.3)]'
            : isYellow
            ? 'border border-amber-500/40 bg-amber-500/20 text-amber-400 shadow-[0_0_20px_rgba(245,158,11,0.3)]'
            : 'border border-rose-500/40 bg-rose-500/20 text-rose-400 shadow-[0_0_20px_rgba(244,63,94,0.3)]'
        }`}
      >
        {icon}
      </div>

      <h2 className="text-2xl font-extrabold tracking-tight text-white">{card.title}</h2>

      {card.warn && (
        <p className="mt-2 inline-block rounded-full border border-amber-500/30 bg-amber-500/15 px-3 py-1 text-xs font-bold text-amber-300">
          {card.warn}
        </p>
      )}
      {card.detail && <p className="mt-2 text-sm font-medium text-slate-200">{card.detail}</p>}
      {card.note && <p className="mt-1 text-xs text-slate-400">{card.note}</p>}

      {/* The running cost of forgetting to check out — shown at the one moment the employee is
          certainly looking at the screen. No auto-close, no reason asked; just the number. */}
      {card.openDays ? (
        <div className="mt-4 rounded-2xl border border-amber-500/20 bg-amber-500/10 p-3.5 text-left">
          <div className="text-xs font-bold text-amber-300">⚠️ {card.openDays} gün çıxış etməmisiniz</div>
          <div className="mt-1 text-[11px] text-slate-300">
            O günlər <strong className="text-white">0 saat</strong> sayılıb. İş bitəndə çıxışı skan etməyi unutmayın.
          </div>
        </div>
      ) : null}

      {/* The moment the employee is certainly looking at the screen — so this is where the checkout
          reminder gets switched on, not in a menu nobody opens. Self-hides once it's on. */}
      {card.offerPush && <PushEnablePrompt dark onShown={setAskingPush} />}

      {/* Showing the photo back closes the loop: the employee sees exactly what was stored. */}
      {card.photo && (
        <img
          src={card.photo}
          alt="Giriş şəkli"
          className="mx-auto mt-4 h-20 w-20 rounded-full object-cover ring-2 ring-emerald-400/50 shadow-lg"
        />
      )}

      {/* The only button on a settled result is "close". Anything else invites the second scan. */}
      {card.final ? (
        askingPush ? (
          <button onClick={onClose} className="mt-4 w-full py-2 text-xs font-semibold text-slate-400 underline underline-offset-4 hover:text-white cursor-pointer">
            İndi yox, bağla
          </button>
        ) : (
          <button
            onClick={onClose}
            className="mt-6 flex w-full items-center justify-center rounded-2xl border border-white/10 bg-white/10 hover:bg-white/15 py-3.5 font-bold text-white transition active:scale-[0.98] cursor-pointer"
          >
            Bağla
          </button>
        )
      ) : (
        <button
          onClick={onRetry}
          className="mt-6 flex w-full items-center justify-center rounded-2xl bg-gradient-to-r from-emerald-500 via-teal-500 to-emerald-600 py-3.5 font-bold text-white shadow-lg shadow-emerald-500/25 transition active:scale-[0.98] cursor-pointer"
        >
          {card.retryLabel ?? 'Yenidən skan et'}
        </button>
      )}

      {card.showDeviceChangeLink && (
        <Link
          to="/device-change-request"
          className="mt-3 block w-full rounded-2xl border border-white/10 bg-white/5 hover:bg-white/10 py-3 text-xs font-semibold text-slate-300 transition text-center"
        >
          Bu mənim yeni telefonumdur
        </Link>
      )}

      {/* A rejected scan is the moment support is needed — hand them the assistant right here, with
          the rejection already in the audit log for it to read. Red results only: a success needs no
          help, and one more button on it would just invite a second scan. */}
      {card.tone === 'red' && (
        <Link to="/help" className="mt-3 block w-full py-2 text-sm font-semibold underline opacity-80">
          💬 Kömək al
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
  // Backend flags: the check-in was late / the check-out early (vs the employee's own hours, else the
  // location's) — the app then asks for a reason (skippable).
  late?: boolean
  earlyDeparture?: boolean
  checkInAtUtc?: string
  checkOutAtUtc?: string
  error?: string
  distanceMeters?: number
  minutes?: number
  /** Past days this employee left open (checked in, never out) — each counts as zero hours. */
  openDays?: number
  /** QR-less check-in only: the face verdict decided before this reply
   *  (Ok · Mismatch · NoFace · MultiFace · NoReference · Error). Absent on a poster scan. */
  faceMatch?: string
  faceScore?: number | null
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

/**
 * What the app says when the geofence refused a scan.
 *
 * Its predecessor was one line — «İş yerində deyilsiniz», with a distance under it — and it was the
 * app's worst sentence: a flat contradiction of what the reader could see out of their own eyes,
 * with nothing to do about it. People came to the office to ask what was wrong with them. Measured
 * over five weeks, they were often right to argue: of the taps refused this way, a ninth were
 * inside the fence once the phone's own error margin is allowed for.
 *
 * So the card states the MEASUREMENT rather than a verdict on the person — "you appeared N m away",
 * because that is all the server actually knows — and prints the phone's accuracy beside it. That
 * second number is what makes an impossible reading legible: ±800 m next to "1276 m" explains
 * itself instantly, and points at the one thing the reader can change.
 */
/**
 * What the check-in card says about the face verdict a QR-less check-in returns. A verified face earns
 * a quiet tick on the detail line; only the verdicts the person can act on get the warning pill. None
 * of them changes the outcome — the check-in is already recorded — which is exactly why the words
 * must be plain: «rəhbər görəcək» is the consequence, said to their face.
 */
function faceLine(verdict: string | undefined): { tick?: string; warn?: string } {
  switch (verdict) {
    case 'Ok': return { tick: 'Üz təsdiqləndi ✓' }
    case 'Mismatch': return { warn: 'Üz etalonla uyğun gəlmədi — rəhbər görəcək' }
    case 'NoFace': return { warn: 'Şəkildə üz görünmür' }
    case 'MultiFace': return { warn: 'Şəkildə bir neçə üz var' }
    // The upload worker seeds the reference from this very photo (one clear face was seen in it), so
    // this is news, not a fault.
    case 'NoReference': return { tick: 'İlk şəkliniz nümunə kimi yadda saxlanıldı' }
    // The verdict ran out of its budget inside the request; the worker writes it moments later.
    case 'Pending': return { tick: 'Üz sonra yoxlanacaq' }
    // Nothing to compare — the photo was refused, or the face service is off.
    case 'NotChecked': return { tick: 'Üz yoxlanılmadı' }
    default: return {}
  }
}

function locationCard(distance: number | null | undefined, accuracy?: number): Card {
  // Poor accuracy is the likelier story whenever the phone admits to a wide margin — and a margin
  // wider than the overshoot means the reading cannot decide the question at all.
  const vague = accuracy != null && (accuracy > POOR_ACCURACY_METERS
    || (distance != null && accuracy >= distance / 2))

  const parts = [
    distance != null ? `İş yerindən ~${distance} m göründünüz` : 'Radius xaricində göründünüz',
    accuracy != null ? `GPS dəqiqliyi ±${Math.round(accuracy)} m` : null,
  ].filter(Boolean)

  return {
    tone: 'red',
    title: 'Yeriniz təsdiqlənmədi',
    detail: parts.join(' · '),
    note: vague
      ? 'Telefon yerinizi dəqiq tapa bilmir. Açıq havaya çıxın, 10–15 saniyə gözləyin, sonra yenidən cəhd edin.'
      : 'İş yerindəsinizsə, açıq yerə çıxıb yenidən cəhd edin. Yenə alınmasa, rəhbərinizə bildirin — filialın xəritədəki yeri düzəldilməlidir.',
  }
}

// `final` marks the outcomes where scanning again cannot change anything — the day is already
// recorded, or only an admin can unblock the employee. Everything else genuinely is worth retrying
// (walk closer, re-aim at the poster), so those keep the retry button.
function errorResult(status: number, data: ScanResponse | null, accuracy?: number): Card {
  const err = data?.error
  switch (err) {
    case 'OutsideRadius':
      // It used to open «İş yerində deyilsiniz» — the app telling somebody standing at their post
      // that they are not at work, with a distance they could see was absurd and no way to argue.
      // That is the sentence people came to the office confused about. Two things are true at once
      // and the card now says which: the phone may be wrong about WHERE IT IS, or they really are
      // away. Leading with the accuracy is what makes a "1276 m" reading make sense to the person
      // reading it, and it is the half they can actually do something about.
      return locationCard(data?.distanceMeters, accuracy)
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
    case 'SharedDeviceNotAllowed':
      // Plain and specific: the worker is standing at the poster on someone else's phone. Tell them
      // exactly what is wrong and exactly who fixes it — retrying is what they were doing 75 times.
      return {
        tone: 'red',
        title: 'Bu telefonu işlətmək icazəniz yoxdur',
        detail: 'Bu telefon başqa işçilərə bağlıdır.',
        note: 'Rəhbərinizə deyin — sizə də icazə versinlər. Təkrar skan kömək etməyəcək.',
        final: true,
      }
    case 'DeviceAccountLimit':
      return {
        tone: 'red',
        title: 'Bu telefonda çox hesab var',
        detail: 'Telefona icazə verilən sayda işçi artıq bağlıdır.',
        note: 'Rəhbərinizə deyin. Təkrar skan kömək etməyəcək.',
        final: true,
      }
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
