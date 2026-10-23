import { useEffect, useRef, useState } from 'react'
import type { Html5Qrcode } from 'html5-qrcode'
import { apiRequest } from '../api/client'
import { SubPageHeader } from '../components/SubPageHeader'
import { NETWORK_MESSAGE, classifySignIn, readCode } from './kitabxanaOutcome'

const READER_ID = 'kitabxana-reader'

/** The address the quiz puts in its QR. Nothing else is ever sent onwards. */
const KITABXANA_QR = /^https?:\/\/book\.qrlog\.az\/qr\/([0-9a-f]{8,64})$/i

let scannerModule: Promise<typeof import('html5-qrcode')> | null = null
function loadScanner(): Promise<typeof import('html5-qrcode')> {
  if (!scannerModule) scannerModule = import('html5-qrcode')
  return scannerModule
}

/**
 * Scanning the Kitabxana 2.0 sign-in QR — and nothing else.
 *
 * The attendance scanner also recognises this QR, but it is a scanner for recording work: it checks
 * where the phone is, which device it is, and takes a selfie on the way in. None of that belongs to
 * signing in to a quiz, and an employee who opens the camera from "Xidmətlər" should not first be
 * asked for their location. So this screen opens the camera and does one thing with what it sees.
 *
 * Nothing is recorded here: no attendance row, no photo, no position. The only call it makes is the
 * same vouch the scanner makes, with the code and nothing else.
 */
export function KitabxanaScanPage() {
  const scannerRef = useRef<Html5Qrcode | null>(null)
  const mountedRef = useRef(true)
  const busyRef = useRef(false)
  const [phase, setPhase] = useState<'scanning' | 'sending' | 'done'>('scanning')
  const [cameraFailed, setCameraFailed] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [foreignQr, setForeignQr] = useState(false)

  useEffect(() => {
    mountedRef.current = true
    void startCamera()
    return () => {
      mountedRef.current = false
      void stopCamera()
    }
    // Mount and unmount only: the camera must not be torn down and reopened on a re-render.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  async function startCamera() {
    setCameraFailed(false)
    setError(null)
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
      // Denied, no camera, or one already held by another tab. Nothing here retries by itself: the
      // employee has to change something (allow the camera, close the other app) before it can work.
      await stopCamera()
      if (mountedRef.current) setCameraFailed(true)
    }
  }

  async function stopCamera() {
    const scanner = scannerRef.current
    scannerRef.current = null
    // Release the injected <video>'s tracks first: one left running keeps the camera busy for the
    // attendance scanner, which is the screen people actually depend on.
    document.querySelectorAll<HTMLVideoElement>(`#${READER_ID} video`).forEach((video) => {
      const stream = video.srcObject as MediaStream | null
      stream?.getTracks().forEach((track) => track.stop())
      video.srcObject = null
    })
    if (!scanner) return
    try {
      await scanner.stop()
    } catch {
      /* never started, or already stopped — the tracks above are what matters */
    }
    try {
      scanner.clear()
    } catch {
      /* ignore */
    }
  }

  async function onDecoded(text: string) {
    if (busyRef.current) return
    const match = KITABXANA_QR.exec(text.trim())
    const code = match ? readCode(match[1]) : null
    if (!code) {
      // Someone else's QR, frame after frame. A hint, not an error: the camera keeps looking.
      setForeignQr(true)
      return
    }

    busyRef.current = true
    setForeignQr(false)
    await stopCamera()
    setPhase('sending')
    setError(null)
    try {
      const { status, data } = await apiRequest('/api/kitabxana/sign-in', { method: 'POST', body: { code } })
      const outcome = classifySignIn(status, data)
      if (outcome.kind === 'signed-in') {
        setPhase('done')
        return
      }
      setError(outcome.message)
    } catch {
      setError(NETWORK_MESSAGE)
    }
    // Refused: back to the camera, so a fresh code on the screen can be scanned straight away.
    busyRef.current = false
    setPhase('scanning')
    void startCamera()
  }

  return (
    <div className="min-h-screen bg-slate-900 text-white">
      <div className="bg-white text-slate-900">
        <SubPageHeader title="Kitabxana 2.0" back="/menu" />
      </div>
      <main className="mx-auto flex max-w-md flex-col items-center gap-4 p-4">
        {phase === 'done' ? (
          <section className="w-full rounded-3xl border border-green-400/30 bg-green-500/10 p-6 text-center" aria-live="polite">
            <div className="text-5xl" aria-hidden="true">✅</div>
            <h2 className="mt-3 text-lg font-bold">Kitabxana 2.0-a daxil oldunuz</h2>
            <p className="mt-2 text-slate-200">
              Adınız və nömrəniz ekranda dolduruldu. Davamı həmin ekrandadır.
            </p>
            <a
              href="https://book.qrlog.az"
              className="mt-5 inline-flex min-h-12 items-center justify-center rounded-2xl bg-green-500 px-6 font-semibold text-white"
            >
              Kitabxana 2.0-ı aç
            </a>
          </section>
        ) : (
          <>
            <p className="text-center text-sm text-slate-300">
              Kitabxana 2.0 ekranındakı QR kodu kameraya tutun. Bu, davamiyyət skanı deyil: şəkil çəkilmir, məkan yoxlanmır.
            </p>

            {/* Kept mounted so html5-qrcode always finds its container. */}
            <div className={phase === 'scanning' && !cameraFailed ? 'w-full' : 'hidden'}>
              <div id={READER_ID} className="w-full overflow-hidden rounded-3xl border border-white/10 bg-black shadow-2xl" />
              {foreignQr && (
                <p className="mt-3 rounded-2xl border border-amber-500/30 bg-amber-500/10 px-4 py-3 text-center text-xs font-medium text-amber-200">
                  Bu kod Kitabxana 2.0-ın kodu deyil — yarış ekranındakı QR kodu skan edin
                </p>
              )}
            </div>

            {phase === 'sending' && (
              <p role="status" className="py-10 font-semibold text-slate-200">Təsdiqlənir…</p>
            )}

            {cameraFailed && (
              <section className="w-full rounded-3xl border border-amber-400/30 bg-amber-500/10 p-6 text-center">
                <h2 className="text-lg font-bold">Kamera açılmadı</h2>
                <p className="mt-2 text-slate-200">
                  Brauzerə kamera icazəsi verin və ya kameranı işlədən digər tətbiqi bağlayın.
                </p>
                <button
                  type="button"
                  onClick={() => void startCamera()}
                  className="mt-5 inline-flex min-h-12 items-center justify-center rounded-2xl bg-white px-6 font-semibold text-slate-900"
                >
                  Yenidən cəhd et
                </button>
              </section>
            )}

            {error && (
              <p role="alert" className="w-full rounded-2xl border border-red-400/30 bg-red-500/10 p-4 text-sm font-medium text-red-100">
                {error}
              </p>
            )}
          </>
        )}
      </main>
    </div>
  )
}
