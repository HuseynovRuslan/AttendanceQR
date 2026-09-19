import { useEffect, useRef, useState } from 'react'
import type { Html5Qrcode } from 'html5-qrcode'
import { useNavigate, useParams } from 'react-router-dom'
import { SubPageHeader } from '../components/SubPageHeader'
import { readApp, readSignInQr } from './externalSignInOutcome'

const READER_ID = 'service-signin-reader'

let scannerModule: Promise<typeof import('html5-qrcode')> | null = null
function loadScanner(): Promise<typeof import('html5-qrcode')> {
  if (!scannerModule) scannerModule = import('html5-qrcode')
  return scannerModule
}

/**
 * Xidmətlər → an application of ours that signs people in with QRLog (MEYDAN v1). The app's page on a computer shows a
 * QR; this screen scans it — that QR and nothing else — and hands its code to the same approval screen a phone reaches
 * from the app's "QRLog ilə daxil ol" link. Nothing is approved here: the person still decides there, with one tap.
 *
 * This is not the attendance scanner and records nothing: no attendance row, no photo, no position. The camera opens
 * only when asked, and closes as soon as a sign-in QR is seen or the screen is left.
 */
export function ServiceSignInPage() {
  const { app: appParam } = useParams()
  const app = readApp(appParam)
  const navigate = useNavigate()
  const scannerRef = useRef<Html5Qrcode | null>(null)
  const mountedRef = useRef(true)
  const doneRef = useRef(false)
  const [scanning, setScanning] = useState(false)
  const [cameraFailed, setCameraFailed] = useState(false)
  const [foreignQr, setForeignQr] = useState(false)

  useEffect(() => {
    mountedRef.current = true
    return () => {
      mountedRef.current = false
      void stopCamera()
    }
    // Mount and unmount only: the camera must not be torn down and reopened on a re-render.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  async function startCamera() {
    setCameraFailed(false)
    setForeignQr(false)
    setScanning(true)
    try {
      const { Html5Qrcode } = await loadScanner()
      if (!mountedRef.current) return
      await stopCamera()
      await new Promise((r) => requestAnimationFrame(() => r(null)))
      const scanner = new Html5Qrcode(READER_ID, { verbose: false })
      scannerRef.current = scanner
      await scanner.start({ facingMode: 'environment' }, { fps: 10, qrbox: { width: 250, height: 250 } }, onDecoded, undefined)
      if (!mountedRef.current) await stopCamera()
    } catch {
      // Denied, no camera, or one already held by another app: the person has to change something first.
      await stopCamera()
      if (mountedRef.current) {
        setScanning(false)
        setCameraFailed(true)
      }
    }
  }

  async function stopCamera() {
    const scanner = scannerRef.current
    scannerRef.current = null
    // Release the injected <video>'s tracks first: one left running keeps the camera busy for the attendance scanner.
    document.querySelectorAll<HTMLVideoElement>(`#${READER_ID} video`).forEach((video) => {
      const stream = video.srcObject as MediaStream | null
      stream?.getTracks().forEach((track) => track.stop())
      video.srcObject = null
    })
    if (!scanner) return
    try {
      await scanner.stop()
    } catch {
      /* never started, or already stopped */
    }
    try {
      scanner.clear()
    } catch {
      /* ignore */
    }
  }

  async function onDecoded(text: string) {
    if (doneRef.current || !app) return
    const code = readSignInQr(text, app)
    if (!code) {
      // Someone else's QR, frame after frame. A hint, not an error: the camera keeps looking.
      setForeignQr(true)
      return
    }
    doneRef.current = true
    await stopCamera()
    // The approval screen: the same one the app's own link opens on a phone. Nothing is sent before the tap there.
    navigate(`/signin/${app.key}?code=${code}`, { replace: true })
  }

  if (!app) {
    return (
      <div className="min-h-screen bg-slate-50">
        <SubPageHeader title="Xidmətlər" back="/menu" />
        <main className="mx-auto max-w-md p-4">
          <p className="rounded-3xl border border-amber-200 bg-amber-50 p-6 text-center text-slate-700">Bu xidmət tapılmadı.</p>
        </main>
      </div>
    )
  }

  return (
    <div className="min-h-screen bg-slate-50">
      <SubPageHeader title={app.serviceName} back="/menu" />
      <main className="mx-auto flex max-w-md flex-col gap-4 p-4">
        <section className="rounded-3xl border border-slate-200 bg-white p-6">
          <p className="text-xs font-bold uppercase tracking-wide text-slate-400">Xidmət</p>
          <h2 className="mt-1 text-2xl font-bold text-slate-900">{app.serviceName}</h2>
          <p className="mt-1 text-sm text-slate-500">{app.description}</p>
          <p className="mt-4 text-slate-700">{app.serviceLine}</p>
          {!scanning && (
            <button
              type="button"
              onClick={() => void startCamera()}
              className="mt-5 inline-flex min-h-12 w-full items-center justify-center rounded-2xl bg-slate-900 px-6 font-semibold text-white"
            >
              QR kodu skan et
            </button>
          )}
          <p className="mt-3 text-xs text-slate-500">
            {app.name} saytında “QRLog ilə daxil ol” düyməsini basın — kompüter ekranında QR kod görünəcək. Telefonda
            isə {app.name} sizi birbaşa bura gətirir.
          </p>
        </section>

        {/* Kept mounted so html5-qrcode always finds its container. */}
        <div className={scanning ? 'w-full' : 'hidden'}>
          <div id={READER_ID} className="w-full overflow-hidden rounded-3xl border border-slate-200 bg-black shadow-lg" />
          {foreignQr && (
            <p className="mt-3 rounded-2xl border border-amber-300 bg-amber-50 px-4 py-3 text-center text-xs font-medium text-amber-800" role="status">
              Bu, {app.name}-ın giriş kodu deyil — kompüter ekranındakı QR kodu skan edin.
            </p>
          )}
          <button
            type="button"
            onClick={() => {
              void stopCamera()
              setScanning(false)
            }}
            className="mt-3 inline-flex min-h-12 w-full items-center justify-center rounded-2xl border border-slate-300 bg-white px-6 font-semibold text-slate-700"
          >
            Kameranı bağla
          </button>
        </div>

        {cameraFailed && (
          <section className="rounded-3xl border border-amber-200 bg-amber-50 p-6 text-center" role="alert">
            <h2 className="text-lg font-bold text-slate-900">Kamera açılmadı</h2>
            <p className="mt-2 text-slate-700">Brauzerə kamera icazəsi verin və ya kameranı işlədən digər tətbiqi bağlayın.</p>
            <button
              type="button"
              onClick={() => void startCamera()}
              className="mt-5 inline-flex min-h-12 items-center justify-center rounded-2xl bg-slate-900 px-6 font-semibold text-white"
            >
              Yenidən cəhd et
            </button>
          </section>
        )}

        <p className="px-2 text-center text-xs text-slate-500">
          Bu, davamiyyət qeydi deyil: selfi çəkilmir, məkan yoxlanmır, heç nə qeydə alınmır.
        </p>
      </main>
    </div>
  )
}
