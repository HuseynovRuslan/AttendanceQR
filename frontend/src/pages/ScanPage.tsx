import { useEffect, useRef, useState } from 'react'
import { Html5Qrcode } from 'html5-qrcode'
import { apiRequest } from '../api/client'
import { getDeviceFingerprint } from '../lib/device'
import { useAuth } from '../auth/AuthContext'

type Card = { tone: 'green' | 'red' | 'yellow'; title: string; detail?: string }
type Phase = 'scanning' | 'processing' | 'done'

const READER_ID = 'reader'

export function ScanPage() {
  const { logout } = useAuth()
  const scannerRef = useRef<Html5Qrcode | null>(null)
  const busyRef = useRef(false)
  const [phase, setPhase] = useState<Phase>('scanning')
  const [cameraError, setCameraError] = useState<string | null>(null)
  const [result, setResult] = useState<Card | null>(null)

  useEffect(() => {
    void startCamera()
    return () => {
      void stopCamera()
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

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

  async function onDecoded(text: string) {
    if (busyRef.current) return
    busyRef.current = true
    await stopCamera()
    setPhase('processing')
    await submitScan(text)
    setPhase('done')
  }

  async function submitScan(qrToken: string) {
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

  const showCamera = phase !== 'done' && !cameraError

  return (
    <div className="min-h-screen flex flex-col bg-slate-900 text-white">
      <header className="flex items-center justify-between px-4 py-3 border-b border-slate-800">
        <span className="font-semibold">AttendanceQR · Skan</span>
        <button
          onClick={logout}
          className="text-sm text-slate-300 hover:text-white bg-slate-800 rounded-lg px-3 py-1.5"
        >
          Çıxış
        </button>
      </header>

      <main className="flex-1 flex flex-col items-center justify-center p-4 gap-5">
        {/* Camera container stays mounted so html5-qrcode can always find it. */}
        <div className={showCamera ? 'w-full max-w-sm' : 'hidden'}>
          <div id={READER_ID} className="w-full overflow-hidden rounded-2xl bg-black" />
          <p className="text-center text-slate-300 mt-3">QR kodu kameraya tutun</p>
        </div>

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

// Checkout response carries no duration, so read today's record from /me and compute it.
async function workedDurationText(recordId: string): Promise<string | undefined> {
  try {
    const { status, data } = await apiRequest<MeRecord[]>('/api/attendance/me')
    if (status !== 200 || !Array.isArray(data)) return undefined
    const record = data.find((r) => r.recordId === recordId)
    if (!record?.checkInAtUtc || !record.checkOutAtUtc) return undefined
    const minutes = Math.round(
      (new Date(record.checkOutAtUtc).getTime() - new Date(record.checkInAtUtc).getTime()) / 60_000,
    )
    const h = Math.floor(minutes / 60)
    const m = minutes % 60
    return `${h} saat ${m} dəqiqə işlədiniz`
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
      return { tone: 'red', title: 'Bu cihaz hesabınıza bağlı deyil' }
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
