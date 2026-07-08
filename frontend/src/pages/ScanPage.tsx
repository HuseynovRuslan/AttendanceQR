import { useEffect, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { Html5Qrcode } from 'html5-qrcode'
import { apiRequest } from '../api/client'
import { getMyAttendance, type AttendanceRecord } from '../api/attendance'
import { getDeviceFingerprint } from '../lib/device'
import { EmployeeNav } from '../components/EmployeeNav'

type Card = { tone: 'green' | 'red' | 'yellow'; title: string; detail?: string; showDeviceChangeLink?: boolean }
type Phase = 'scanning' | 'processing' | 'done'
type TodayInfo =
  | { kind: 'loading' }
  | { kind: 'none' }
  | { kind: 'in-progress'; checkInAtUtc: string }
  | { kind: 'completed'; checkInAtUtc: string; checkOutAtUtc: string }

const READER_ID = 'reader'

export function ScanPage() {
  const scannerRef = useRef<Html5Qrcode | null>(null)
  // Photo audit: a separate, always-warm front-camera stream we grab a single silent frame from at
  // the moment a check-in QR is decoded. Kept apart from html5-qrcode's back camera. Entirely
  // best-effort — if the front camera is unavailable (permission denied, or a device that can't run
  // two cameras at once) we simply skip the photo and the check-in proceeds unaffected.
  const selfieStreamRef = useRef<MediaStream | null>(null)
  const selfieVideoRef = useRef<HTMLVideoElement | null>(null)
  const busyRef = useRef(false)
  const [phase, setPhase] = useState<Phase>('scanning')
  const [cameraError, setCameraError] = useState<string | null>(null)
  const [result, setResult] = useState<Card | null>(null)
  const [today, setToday] = useState<TodayInfo>({ kind: 'loading' })

  // Today's status decides whether the camera should even start — no point opening it if the
  // day is already complete (the backend would just reject with AlreadyCompleted anyway).
  useEffect(() => {
    void loadTodayStatus()
  }, [])

  useEffect(() => {
    if (today.kind === 'loading') return
    if (today.kind === 'completed') {
      void stopCamera()
      stopSelfieCamera()
      return
    }
    void startCamera()
    // Warm the front camera in parallel so a frame is sharp and instant when the QR decodes.
    void startSelfieCamera()
    return () => {
      void stopCamera()
      stopSelfieCamera()
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [today.kind])

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

  async function startSelfieCamera() {
    if (selfieStreamRef.current) return
    if (!navigator.mediaDevices?.getUserMedia) return
    try {
      const stream = await navigator.mediaDevices.getUserMedia({
        video: { facingMode: 'user' },
        audio: false,
      })
      selfieStreamRef.current = stream
      const video = selfieVideoRef.current
      if (video) {
        video.srcObject = stream
        await video.play().catch(() => {})
      }
    } catch {
      // No front camera / permission denied / device can't run two cameras — skip photo silently.
      selfieStreamRef.current = null
    }
  }

  function stopSelfieCamera() {
    const stream = selfieStreamRef.current
    selfieStreamRef.current = null
    stream?.getTracks().forEach((t) => t.stop())
    const video = selfieVideoRef.current
    if (video) video.srcObject = null
  }

  // Grab one frame, shrink to ~640px wide, encode WebP @0.7 (~30–60 KB), return a data URL — or
  // null if the stream isn't ready. Must run while the stream is still live.
  async function captureSelfie(): Promise<string | null> {
    const video = selfieVideoRef.current
    if (!video || !selfieStreamRef.current || video.videoWidth === 0) return null
    try {
      const targetWidth = Math.min(640, video.videoWidth)
      const scale = targetWidth / video.videoWidth
      const canvas = document.createElement('canvas')
      canvas.width = targetWidth
      canvas.height = Math.round(video.videoHeight * scale)
      const ctx = canvas.getContext('2d')
      if (!ctx) return null
      ctx.drawImage(video, 0, 0, canvas.width, canvas.height)
      const blob = await new Promise<Blob | null>((resolve) =>
        canvas.toBlob((b) => resolve(b), 'image/webp', 0.7),
      )
      if (!blob) return null
      return await blobToDataUrl(blob)
    } catch {
      return null
    }
  }

  async function onDecoded(text: string) {
    if (busyRef.current) return
    busyRef.current = true
    // Capture the selfie ONLY for a check-in (no record yet today) and while the stream is still
    // live. Check-out never captures a photo. Best-effort: null if the camera wasn't available.
    const photoBase64 = today.kind === 'none' ? await captureSelfie() : null
    await stopCamera()
    stopSelfieCamera()
    setPhase('processing')
    await submitScan(text, photoBase64)
    setPhase('done')
    void loadTodayStatus()
  }

  async function submitScan(qrToken: string, photoBase64: string | null) {
    let coords: GeolocationCoordinates
    try {
      coords = await getCurrentPosition()
    } catch {
      setResult({
        tone: 'red',
        title: 'GPS icazəsi lazımdır',
        detail: 'Radius yoxlanışı üçün məkan (GPS) icazəsi verin — skan GPS-siz işləmir.',
      })
      return
    }

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
          detail: `Saat ${fmtTime(data.checkInAtUtc)} · ${statusAz(data.status)}`,
        })
        return
      }
      if (status === 200 && data?.action === 'CheckOut') {
        const worked = data.recordId ? await workedDurationText(data.recordId) : undefined
        setResult({
          tone: 'green',
          title: 'Çıxış qeydə alındı',
          detail: worked ?? `Saat ${fmtTime(data.checkOutAtUtc)}`,
        })
        return
      }
      setResult(errorResult(status, data))
    } catch {
      setResult({ tone: 'red', title: 'Şəbəkə xətası', detail: 'Serverə qoşulmaq mümkün olmadı.' })
    }
  }

  const showCamera = today.kind !== 'loading' && today.kind !== 'completed' && phase !== 'done' && !cameraError

  return (
    <div className="min-h-screen flex flex-col bg-slate-900 text-white">
      <EmployeeNav title="AttendanceQR · Skan" />

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

        {/* Camera container stays mounted so html5-qrcode can always find it. */}
        <div className={showCamera ? 'w-full max-w-sm' : 'hidden'}>
          <div id={READER_ID} className="w-full overflow-hidden rounded-2xl bg-black" />
          <p className="text-center text-slate-300 mt-3">QR kodu kameraya tutun</p>
        </div>

        {/* Hidden front-camera feed for the silent check-in selfie (photo audit). Always mounted so
            captureSelfie() can read a frame; never shown to the employee. */}
        <video ref={selfieVideoRef} className="hidden" playsInline muted autoPlay />

        {phase === 'processing' && (
          <p className="text-lg animate-pulse">Yoxlanılır…</p>
        )}

        {cameraError && (
          <ResultCard card={{ tone: 'red', title: 'Kamera xətası', detail: cameraError }} onRetry={startCamera} />
        )}

        {phase === 'done' && result && <ResultCard card={result} onRetry={startCamera} />}
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

function ResultCard({ card, onRetry }: { card: Card; onRetry: () => void }) {
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
      <button
        onClick={onRetry}
        className="mt-6 w-full bg-black/15 hover:bg-black/25 rounded-lg py-3 font-semibold transition"
      >
        Yenidən skan et
      </button>
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

function getCurrentPosition(): Promise<GeolocationCoordinates> {
  return new Promise((resolve, reject) => {
    if (!navigator.geolocation) {
      reject(new Error('no geolocation'))
      return
    }
    navigator.geolocation.getCurrentPosition(
      (pos) => resolve(pos.coords),
      (err) => reject(err),
      { enableHighAccuracy: true, timeout: 10_000 },
    )
  })
}

function fmtTime(iso?: string): string {
  if (!iso) return ''
  return new Date(iso).toLocaleTimeString('az-AZ', { hour: '2-digit', minute: '2-digit' })
}

function statusAz(status?: string): string {
  if (status === 'Late') return 'Gecikmə'
  if (status === 'OnTime') return 'Vaxtında'
  return status ?? ''
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

function errorResult(status: number, data: ScanResponse | null): Card {
  const err = data?.error
  switch (err) {
    case 'OutsideRadius':
      return {
        tone: 'red',
        title: 'İş yerində deyilsiniz',
        detail: data?.distanceMeters != null ? `Məsafə: ${data.distanceMeters} m` : 'Radius xaricindəsiniz',
      }
    case 'DeviceMismatch':
      return {
        tone: 'red',
        title: 'Bu cihaz hesabınıza bağlı deyil',
        showDeviceChangeLink: true,
      }
    case 'NoDeviceBound':
      return { tone: 'red', title: 'Cihaz hesabınıza bağlı deyil', detail: 'Admin ilə əlaqə saxlayın.' }
    case 'TokenExpired':
    case 'TokenReused':
      return { tone: 'yellow', title: 'QR kod köhnəlib', detail: 'Yenidən skan edin.' }
    case 'AlreadyCompleted':
      return { tone: 'yellow', title: 'Bu gün tamamlanıb', detail: 'Giriş və çıxış artıq qeydə alınıb.' }
    case 'EmployeeNotFoundOrInactive':
      return { tone: 'red', title: 'Hesab aktiv deyil' }
    case 'LocationNotFound':
      return { tone: 'red', title: 'Məkan tapılmadı' }
    default:
      // QR signature/format failures and anything else — show the reason the backend returned.
      return { tone: 'yellow', title: 'QR kod qəbul edilmədi', detail: err ?? `HTTP ${status}` }
  }
}
