import { useEffect, useState, type ReactNode } from 'react'
import { useParams } from 'react-router-dom'
import { QRCodeSVG } from 'qrcode.react'
import { getKioskLocation, getKioskToken } from '../api/kiosk'

export function KioskPage() {
  const { locationId } = useParams()
  const [token, setToken] = useState<string | null>(null)
  const [locationName, setLocationName] = useState<string | null>(null)
  const [locationNotFound, setLocationNotFound] = useState(false)
  const [offline, setOffline] = useState(false)
  const [now, setNow] = useState(() => new Date())
  const [qrSize, setQrSize] = useState(() => computeQrSize())

  // Live clock so an employee can confirm the screen is alive / correct.
  useEffect(() => {
    const t = setInterval(() => setNow(new Date()), 1000)
    return () => clearInterval(t)
  }, [])

  // Keep the QR sized to the viewport (kiosk screens vary).
  useEffect(() => {
    const onResize = () => setQrSize(computeQrSize())
    window.addEventListener('resize', onResize)
    return () => window.removeEventListener('resize', onResize)
  }, [])

  // Location name — fetched once per id (404 ⇒ misconfigured kiosk URL).
  useEffect(() => {
    if (!locationId) return
    let cancelled = false
    setLocationNotFound(false)
    setLocationName(null)
    getKioskLocation(locationId)
      .then(({ status, data }) => {
        if (cancelled) return
        if (status === 200 && data && 'name' in data) setLocationName(data.name)
        else if (status === 404) setLocationNotFound(true)
      })
      .catch(() => {
        /* network error — the token loop surfaces the offline state */
      })
    return () => {
      cancelled = true
    }
  }, [locationId])

  // Token loop: reschedules itself using each response's refreshInSeconds (so a server-side TTL
  // change is honoured), and on failure keeps the last QR + retries — the screen never goes blank.
  useEffect(() => {
    if (!locationId) return
    let cancelled = false
    let timer: number | undefined

    async function load() {
      try {
        const { status, data } = await getKioskToken(locationId!)
        if (cancelled) return
        if (status === 200 && data?.token) {
          setToken(data.token)
          setOffline(false)
          const secs = data.refreshInSeconds > 0 ? data.refreshInSeconds : 55
          timer = window.setTimeout(load, secs * 1000)
        } else {
          setOffline(true)
          timer = window.setTimeout(load, 5000)
        }
      } catch {
        if (cancelled) return
        setOffline(true) // keep showing the last QR
        timer = window.setTimeout(load, 5000)
      }
    }

    void load()
    return () => {
      cancelled = true
      if (timer) clearTimeout(timer)
    }
  }, [locationId])

  if (!locationId) {
    return (
      <Centered>
        <h1 className="text-3xl font-bold">Kiosk</h1>
        <p className="text-slate-400 mt-3">
          URL-də lokasiya ID yoxdur. Nümunə: <code>/kiosk/&lt;locationId&gt;</code>
        </p>
      </Centered>
    )
  }

  if (locationNotFound) {
    return (
      <Centered>
        <div className="text-5xl mb-3">⚠️</div>
        <h1 className="text-3xl font-bold">Lokasiya tapılmadı</h1>
        <p className="text-slate-400 mt-3">Kiosk URL-ini yoxlayın.</p>
      </Centered>
    )
  }

  return (
    <div className="min-h-screen bg-slate-900 text-white flex flex-col items-center justify-between py-10 px-4">
      <header className="text-center">
        <h1 className="text-4xl md:text-5xl font-bold">{locationName ?? '…'}</h1>
        <p className="text-3xl md:text-4xl text-slate-200 mt-3 tabular-nums">
          {now.toLocaleTimeString('az-AZ', { hour: '2-digit', minute: '2-digit', second: '2-digit' })}
        </p>
        <p className="text-slate-400 mt-1 capitalize">
          {now.toLocaleDateString('az-AZ', {
            weekday: 'long',
            day: 'numeric',
            month: 'long',
            year: 'numeric',
          })}
        </p>
      </header>

      <div className="relative">
        <div className="bg-white rounded-3xl p-6 shadow-2xl">
          {token ? (
            <QRCodeSVG value={token} size={qrSize} level="M" bgColor="#ffffff" fgColor="#000000" />
          ) : (
            <div
              style={{ width: qrSize, height: qrSize }}
              className="flex items-center justify-center text-slate-400"
            >
              Yüklənir…
            </div>
          )}
        </div>
        {offline && (
          <div className="absolute -bottom-4 left-1/2 -translate-x-1/2 whitespace-nowrap bg-yellow-400 text-slate-900 text-sm font-semibold rounded-full px-4 py-1.5 shadow-lg">
            ● Bağlantı bərpa olunur…
          </div>
        )}
      </div>

      <p className="text-2xl md:text-3xl text-slate-200 text-center">
        📱 QR kodu telefonunuzla skan edin
      </p>
    </div>
  )
}

function Centered({ children }: { children: ReactNode }) {
  return (
    <div className="min-h-screen bg-slate-900 text-white flex flex-col items-center justify-center text-center p-6">
      {children}
    </div>
  )
}

function computeQrSize(): number {
  const min = Math.min(window.innerWidth, window.innerHeight)
  return Math.max(180, Math.round(min * 0.6))
}
