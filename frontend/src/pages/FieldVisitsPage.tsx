import { useEffect, useRef, useState } from 'react'
import { EmployeeNav } from '../components/EmployeeNav'
import {
  getMyFieldVisits,
  startFieldVisit,
  checkInFieldVisit,
  checkOutFieldVisit,
  type MyFieldVisit,
} from '../api/fieldVisits'
import { fileToWebp } from '../lib/fieldSelfie'
import { getPosition } from '../lib/geo'
import { fmtTime } from '../lib/format'

type Action =
  | { kind: 'checkin'; visitId: string }
  | { kind: 'checkout'; visitId: string }
  | { kind: 'start'; label: string | null }

const GEO_MSG: Record<string, string> = {
  denied: 'GPS icazəsi bağlıdır — brauzer/telefon ayarlarından yeri açın',
  unavailable: 'GPS mövqe tapılmadı — açıq havaya çıxıb yenidən yoxlayın',
  timeout: 'GPS gec cavab verdi — yenidən cəhd edin',
  unsupported: 'Bu cihaz GPS-i dəstəkləmir',
}

const STATUS_TONE: Record<string, string> = {
  Assigned: 'bg-blue-500/20 text-blue-300',
  CheckedIn: 'bg-amber-500/20 text-amber-300',
  Completed: 'bg-green-500/20 text-green-400',
}
const STATUS_LABEL: Record<string, string> = {
  Assigned: 'Tapşırılıb',
  CheckedIn: 'Ərazidə',
  Completed: 'Tamamlandı',
}

export function FieldVisitsPage() {
  const [visits, setVisits] = useState<MyFieldVisit[]>([])
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [info, setInfo] = useState<string | null>(null)
  const fileRef = useRef<HTMLInputElement>(null)
  const pending = useRef<Action | null>(null)

  async function load() {
    setLoading(true)
    const res = await getMyFieldVisits()
    setLoading(false)
    if (res.status === 200 && Array.isArray(res.data)) setVisits(res.data)
    else setError('Yüklənmədi')
  }
  useEffect(() => {
    void load()
  }, [])

  // A tap opens the camera; the photo (optional) comes back through the file input's change event.
  function startCapture(action: Action) {
    setError(null)
    setInfo(null)
    pending.current = action
    fileRef.current?.click()
  }

  async function onFile(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0]
    e.target.value = '' // let the same photo be retaken
    const action = pending.current
    pending.current = null
    if (!action) return
    const photo = file ? await fileToWebp(file) : null
    await submit(action, photo)
  }

  // "Şəkilsiz" — a field worker whose camera fails must still be able to prove they arrived (GPS+time).
  async function submitPhotoless(action: Action) {
    setError(null)
    setInfo(null)
    await submit(action, null)
  }

  async function submit(action: Action, photoBase64: string | null) {
    setBusy(true)
    const geo = await getPosition()
    if (!geo.ok) {
      setBusy(false)
      setError(GEO_MSG[geo.kind] ?? 'GPS alınmadı')
      return
    }
    const body = { latitude: geo.coords.latitude, longitude: geo.coords.longitude, photoBase64 }
    const res =
      action.kind === 'checkin'
        ? await checkInFieldVisit(action.visitId, body)
        : action.kind === 'checkout'
          ? await checkOutFieldVisit(action.visitId, body)
          : await startFieldVisit({ ...body, targetLabel: action.label })
    setBusy(false)
    if (res.status === 200) {
      setInfo(
        action.kind === 'checkout' ? 'Çıxış qeyd olundu ✓' : 'Ərazidə qeyd olundunuz ✓',
      )
      await load()
    } else {
      setError('Alınmadı — yenidən cəhd edin')
    }
  }

  function newSelfVisit() {
    const label = window.prompt('Harada olduğunuzu yazın (istəyə bağlı — məs. ünvan/obyekt):', '')
    if (label === null) return // cancelled
    startCapture({ kind: 'start', label: label.trim() || null })
  }

  return (
    <div className="min-h-screen flex flex-col bg-slate-900 text-white">
      <EmployeeNav title="Səyyar" />

      <input ref={fileRef} type="file" accept="image/*" capture="user" hidden onChange={(e) => void onFile(e)} />

      <main className="flex-1 p-4 flex flex-col items-center gap-4">
        <div className="w-full max-w-md flex flex-col gap-3">
          {busy && (
            <div className="bg-slate-800 rounded-xl p-3 text-center text-slate-300">Yer təyin olunur…</div>
          )}
          {error && <div className="bg-red-500/15 text-red-300 rounded-xl p-3 text-center text-sm">{error}</div>}
          {info && <div className="bg-green-500/15 text-green-300 rounded-xl p-3 text-center text-sm">{info}</div>}

          {loading && <p className="text-slate-400 text-center">Yüklənir…</p>}
          {!loading && visits.length === 0 && (
            <p className="text-slate-400 text-center py-6">Bu gün üçün sahə ziyarəti yoxdur.</p>
          )}

          {visits.map((v) => (
            <div key={v.id} className="bg-slate-800 rounded-2xl p-4 flex flex-col gap-2">
              <div className="flex items-start justify-between gap-2">
                <div className="min-w-0">
                  <div className="font-semibold truncate">
                    {v.targetLabel || (v.selfReported ? 'Sərbəst ziyarət' : 'Sahə ziyarəti')}
                  </div>
                  {v.assignedByName && (
                    <div className="text-xs text-slate-400 mt-0.5">Tapşıran: {v.assignedByName}</div>
                  )}
                  {v.note && <div className="text-xs text-slate-400 mt-0.5">{v.note}</div>}
                </div>
                <span className={`text-xs font-bold px-2 py-1 rounded-full whitespace-nowrap ${STATUS_TONE[v.status] ?? 'bg-slate-600 text-slate-200'}`}>
                  {STATUS_LABEL[v.status] ?? v.status}
                </span>
              </div>

              {(v.checkInAtUtc || v.checkOutAtUtc) && (
                <div className="text-sm text-slate-300">
                  {v.checkInAtUtc && <>Giriş: <b>{fmtTime(v.checkInAtUtc)}</b></>}
                  {v.checkOutAtUtc && <> · Çıxış: <b>{fmtTime(v.checkOutAtUtc)}</b></>}
                </div>
              )}

              {v.status === 'Assigned' && (
                <div className="flex flex-col gap-1 mt-1">
                  <button
                    disabled={busy}
                    onClick={() => startCapture({ kind: 'checkin', visitId: v.id })}
                    className="w-full rounded-xl bg-blue-600 py-3 font-bold disabled:opacity-60"
                  >
                    📷 Ərazidəyəm (giriş)
                  </button>
                  <button disabled={busy} onClick={() => void submitPhotoless({ kind: 'checkin', visitId: v.id })} className="text-xs text-slate-400 underline disabled:opacity-60">
                    Şəkilsiz giriş
                  </button>
                </div>
              )}

              {v.status === 'CheckedIn' && (
                <div className="flex flex-col gap-1 mt-1">
                  <button
                    disabled={busy}
                    onClick={() => startCapture({ kind: 'checkout', visitId: v.id })}
                    className="w-full rounded-xl bg-emerald-600 py-3 font-bold disabled:opacity-60"
                  >
                    📷 Çıxış et
                  </button>
                  <button disabled={busy} onClick={() => void submitPhotoless({ kind: 'checkout', visitId: v.id })} className="text-xs text-slate-400 underline disabled:opacity-60">
                    Şəkilsiz çıxış
                  </button>
                </div>
              )}
            </div>
          ))}

          <button
            disabled={busy}
            onClick={newSelfVisit}
            className="w-full rounded-2xl border border-slate-600 py-3 font-semibold text-slate-200 disabled:opacity-60 mt-1"
          >
            + Yeni sahə ziyarəti
          </button>
          <p className="text-xs text-slate-500 text-center px-2">
            Əraziyə çatanda «giriş», ayrılanda «çıxış» edin. Yeriniz və şəkiliniz qeyd olunur — QR-a ehtiyac yoxdur.
          </p>
        </div>
      </main>
    </div>
  )
}
