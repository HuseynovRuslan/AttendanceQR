import { useEffect, useRef, useState } from 'react'
import { checkOutFieldVisit, uploadWorkPhoto, type ChecklistItem, type MyFieldVisit } from '../api/fieldVisits'
import { getPosition } from '../lib/geo'
import { fileToWorkPhoto, frameToWorkPhoto, waitForVideoFrame } from '../lib/workPhoto'
import { applyPendingTicks, clearPendingTicks } from '../lib/pendingTicks'
import { IconCamera, IconCheck } from './icons'

/**
 * The check-out step: tick what was done, photograph the WORK, leave.
 *
 * Shared by BOTH entry points on purpose — the /field screen and the home card. They used to each
 * carry their own copy of the check-out call, which is exactly how the home card would quietly keep
 * recording departures with no evidence while every log said success.
 *
 * Three promises this component keeps, in order of importance:
 *  1. The check-out is recorded. A denied camera, a failed upload, an unticked list and an
 *     unavailable GPS all still end with a departure on the record — none of them is a gate.
 *  2. The pay-critical write goes alone. The ~200-byte check-out and the ~250 KB JPEG are two
 *     separate requests, so a photo that fails on a rural uplink cannot take the record with it.
 *  3. The camera points AWAY from the worker. This is the structural difference from a selfie, and
 *     the only camera in the whole field flow.
 */

const GEO_FAILED = 'Yer təyin olunmadı — çıxışınız yenə qeyd olundu'

type Phase = 'framing' | 'preview' | 'sending'

export function FieldCheckoutSheet({
  visit,
  onClose,
  onDone,
}: {
  visit: MyFieldVisit
  onClose: () => void
  /** Called once the DEPARTURE is recorded — the photo upload runs on after this. */
  onDone: (result: { photoPending: boolean }) => void | Promise<void>
}) {
  const videoRef = useRef<HTMLVideoElement | null>(null)
  const streamRef = useRef<MediaStream | null>(null)
  const fileRef = useRef<HTMLInputElement | null>(null)
  // GPS is fetched the moment the sheet opens, in parallel with the camera warming, so the final
  // confirm is instant instead of parking the worker on "Yer təyin olunur…".
  const geoRef = useRef<Promise<Awaited<ReturnType<typeof getPosition>>> | null>(null)

  const [phase, setPhase] = useState<Phase>('framing')
  const [photo, setPhoto] = useState<string | null>(null)
  const [cameraFailed, setCameraFailed] = useState<null | 'denied' | 'other'>(null)
  const [warming, setWarming] = useState(true)
  const [confirmNoPhoto, setConfirmNoPhoto] = useState(false)
  // Seeded through the pending layer, NOT from the raw visit: a tick made on /field whose POST never
  // landed must still count as done here, or this sheet's absolute set would silently undo it.
  const [done, setDone] = useState<Record<string, boolean>>(() =>
    Object.fromEntries(applyPendingTicks(visit).checklist.map((i) => [i.id, i.isDone])),
  )
  const [err, setErr] = useState<string | null>(null)
  const noPhotoTimer = useRef<number | undefined>(undefined)

  // Warm the rear camera + prefetch GPS together, and stop the tracks on the way out — iOS allows
  // exactly one active camera, so a stream left running blocks the scan screen afterwards.
  useEffect(() => {
    let cancelled = false
    geoRef.current = getPosition()
    ;(async () => {
      try {
        const stream = await navigator.mediaDevices.getUserMedia({
          video: { facingMode: { ideal: 'environment' } },
          audio: false,
        })
        if (cancelled) {
          stream.getTracks().forEach((t) => t.stop())
          return
        }
        streamRef.current = stream
        const el = videoRef.current
        if (el) {
          el.srcObject = stream
          await el.play().catch(() => {})
          await waitForVideoFrame(el)
        }
        if (!cancelled) setWarming(false)
      } catch (e) {
        if (cancelled) return
        const name = (e as { name?: string })?.name ?? ''
        setCameraFailed(name === 'NotAllowedError' || name === 'SecurityError' ? 'denied' : 'other')
        setWarming(false)
      }
    })()
    return () => {
      cancelled = true
      window.clearTimeout(noPhotoTimer.current)
      streamRef.current?.getTracks().forEach((t) => t.stop())
      streamRef.current = null
      if (videoRef.current) videoRef.current.srcObject = null
    }
  }, [])

  const pending = visit.checklist.filter((i) => !done[i.id])

  function toggle(item: ChecklistItem) {
    setDone((d) => ({ ...d, [item.id]: !d[item.id] }))
    navigator.vibrate?.(10)
  }

  async function shoot() {
    const el = videoRef.current
    if (!el) return
    const data = await frameToWorkPhoto(el)
    if (!data) {
      setErr('Şəkil alınmadı — yenidən cəhd edin')
      return
    }
    setPhoto(data)
    setPhase('preview')
  }

  async function pickFile(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0]
    e.target.value = ''
    if (!file) return
    const data = await fileToWorkPhoto(file)
    if (!data) {
      setErr('Şəkil alınmadı — yenidən cəhd edin')
      return
    }
    setPhoto(data)
    setPhase('preview')
  }

  /** The departure. Nothing in here may prevent it being written. */
  async function submit() {
    setPhase('sending')
    setErr(null)
    try {
      const geo = await (geoRef.current ?? getPosition())
      const doneItemIds = visit.checklist.filter((i) => done[i.id]).map((i) => i.id)
      const res = await checkOutFieldVisit(visit.id, {
        latitude: geo.ok ? geo.coords.latitude : null,
        longitude: geo.ok ? geo.coords.longitude : null,
        photoBase64: null,
        doneItemIds,
      })
      if (res.status !== 200) {
        setErr('Alınmadı — yenidən cəhd edin')
        setPhase(photo ? 'preview' : 'framing') // the photo survives, so they retry without re-shooting
        return
      }
      // The record is safe — and its payload settled the ticks, so the pending layer has done its
      // job. Cleared HERE rather than in a caller's onDone, so both entry points get it.
      clearPendingTicks(visit.id)
      // The upload runs on its own — the worker is free to pocket the phone.
      if (photo) void uploadWorkPhoto(visit.id, photo).catch(() => {})
      if (!geo.ok) setErr(GEO_FAILED)
      await onDone({ photoPending: photo !== null })
    } catch {
      setErr('Alınmadı — yenidən cəhd edin')
      setPhase(photo ? 'preview' : 'framing')
    }
  }

  function requestNoPhoto() {
    setConfirmNoPhoto(true)
    window.clearTimeout(noPhotoTimer.current)
    noPhotoTimer.current = window.setTimeout(() => setConfirmNoPhoto(false), 4000)
  }

  const sending = phase === 'sending'
  const title = visit.targetLabel || (visit.selfReported ? 'Sərbəst ziyarət' : 'Sahə ziyarəti')

  return (
    <div className="fixed inset-0 z-50">
      <div className="absolute inset-0 bg-slate-900/50 backdrop-blur-sm" onClick={sending ? undefined : onClose} />
      <div className="absolute inset-x-0 bottom-0 max-h-[92vh] overflow-y-auto rounded-t-3xl bg-white p-5 pb-[max(2rem,env(safe-area-inset-bottom))] shadow-2xl">
        <div className="mx-auto mb-4 h-1.5 w-10 rounded-full bg-slate-200" />
        <div className="text-lg font-bold">Çıxışı tamamla</div>
        <div className="mt-0.5 truncate text-sm text-slate-500">{title}</div>

        {/* Unticked work — an offer, never a gate. */}
        {pending.length > 0 && (
          <div className="mt-3 rounded-2xl bg-amber-50 p-3">
            <div className="text-sm font-bold text-amber-800">{pending.length} iş işarələnməyib</div>
            <div className="text-xs text-amber-700">İstəsəniz indi işarələyin — çıxışa mane olmur</div>
            <div className="mt-2 flex flex-col gap-1">
              {pending.slice(0, 3).map((i) => (
                <button
                  key={i.id}
                  onClick={() => toggle(i)}
                  className="flex min-h-[44px] items-center gap-2 rounded-xl bg-white/70 px-2 text-left text-sm font-semibold text-amber-900"
                >
                  <span className="h-5 w-5 shrink-0 rounded-md border-2 border-amber-400" />
                  {i.label}
                </button>
              ))}
              {pending.length > 3 && <div className="text-xs text-amber-700">+{pending.length - 3} daha</div>}
            </div>
          </div>
        )}

        {/* One box; the layers stack rather than replace each other. The <video> is NEVER unmounted:
            srcObject is attached once, so swapping it out for the preview and back would hand the
            retake a fresh, unattached element — the stream stays live but the next shutter reads a
            0-width frame and fails forever. The captured still simply covers it, which also means a
            photo taken through the file fallback is actually visible (the camera-failed panel used
            to win the ternary and hide it). */}
        <div className="relative mt-3 aspect-[4/3] w-full overflow-hidden rounded-2xl bg-black">
          <video ref={videoRef} autoPlay muted playsInline className="h-full w-full object-cover" />

          {!photo && cameraFailed && (
            <div className="absolute inset-0 flex flex-col items-center justify-center gap-1 bg-slate-100 p-4 text-center">
              <div className="text-sm font-semibold text-slate-700">Kamera açılmadı — şəkilsiz də çıxış edə bilərsiniz</div>
              {cameraFailed === 'denied' && (
                <div className="text-xs text-slate-500">Kamera icazəsi bağlıdır — telefon ayarlarından açın</div>
              )}
            </div>
          )}

          {!photo && !cameraFailed && warming && (
            <div className="absolute inset-0 flex flex-col items-center justify-center gap-2 bg-black/60 text-white">
              <span className="h-6 w-6 animate-spin rounded-full border-2 border-white/40 border-t-white" />
              <span className="text-sm">Kamera hazırlanır…</span>
            </div>
          )}

          {photo && <img src={photo} alt="İş şəkli" className="absolute inset-0 h-full w-full object-cover" />}
        </div>
        {!photo && (
          <div className="mt-2 text-center text-xs text-slate-500">
            Görülən işin şəklini çəkin — özünüzün yox, işin.
          </div>
        )}

        {err && <div className="mt-2 text-center text-sm text-red-600">{err}</div>}

        {/* Actions */}
        <div className="mt-4 flex flex-col gap-2">
          {photo ? (
            <>
              <button
                disabled={sending}
                onClick={() => void submit()}
                className="flex w-full items-center justify-center gap-2 rounded-2xl bg-emerald-600 py-3.5 font-bold text-white transition active:scale-[.99] disabled:opacity-60"
              >
                {sending ? (
                  <>
                    <span className="h-4 w-4 animate-spin rounded-full border-2 border-white/40 border-t-white" />
                    Göndərilir…
                  </>
                ) : (
                  <>
                    <IconCheck className="h-5 w-5" /> Çıxışı təsdiqlə
                  </>
                )}
              </button>
              {!sending && (
                <button onClick={() => { setPhoto(null); setPhase('framing') }} className="w-full py-2 font-semibold text-slate-500">
                  Yenidən çək
                </button>
              )}
            </>
          ) : cameraFailed ? (
            <>
              <button
                onClick={() => fileRef.current?.click()}
                className="flex w-full items-center justify-center gap-2 rounded-2xl bg-violet-600 py-3.5 font-bold text-white transition active:scale-[.99]"
              >
                <IconCamera className="h-5 w-5" /> Şəkil çək (kamera tətbiqi)
              </button>
              <button
                disabled={sending}
                onClick={() => (confirmNoPhoto ? void submit() : requestNoPhoto())}
                className="w-full rounded-2xl border border-slate-200 py-3 font-bold text-slate-600 disabled:opacity-60"
              >
                {sending ? 'Göndərilir…' : confirmNoPhoto ? 'Şəkilsiz çıxışı təsdiqlə' : 'Şəkilsiz çıx'}
              </button>
            </>
          ) : (
            <>
              <button
                disabled={warming}
                onClick={() => void shoot()}
                className="mx-auto flex h-16 w-16 items-center justify-center rounded-full bg-violet-600 text-white shadow-lg transition active:scale-95 disabled:opacity-50"
                aria-label="Şəkil çək"
              >
                <IconCamera className="h-7 w-7" />
              </button>
              <div className="text-center text-sm font-semibold text-slate-600">Şəkil çək</div>
              <button
                disabled={sending}
                onClick={() => (confirmNoPhoto ? void submit() : requestNoPhoto())}
                className="mt-1 w-full py-2 text-sm font-semibold text-slate-500 disabled:opacity-60"
              >
                {sending ? 'Göndərilir…' : confirmNoPhoto ? 'Şəkilsiz çıxışı təsdiqlə' : 'Şəkilsiz çıx'}
              </button>
            </>
          )}
          {!sending && (
            <button onClick={onClose} className="w-full py-1 text-sm text-slate-400">
              Bağla
            </button>
          )}
        </div>

        {/* The native camera app has its own permission grant and often works where getUserMedia does
            not — notably inside in-app browsers. */}
        <input ref={fileRef} type="file" accept="image/*" capture="environment" className="hidden" onChange={(e) => void pickFile(e)} />
      </div>
    </div>
  )
}
