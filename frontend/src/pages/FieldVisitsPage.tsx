import { useEffect, useState } from 'react'
import { EmployeeNav } from '../components/EmployeeNav'
import {
  getMyFieldVisits,
  startFieldVisit,
  checkInFieldVisit,
  checkOutFieldVisit,
  type MyFieldVisit,
} from '../api/fieldVisits'
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

  // Photo-less by design: a single tap records GPS + time. No selfie, no camera step.
  async function submit(action: Action) {
    setError(null)
    setInfo(null)
    setBusy(true)
    const geo = await getPosition()
    if (!geo.ok) {
      setBusy(false)
      setError(GEO_MSG[geo.kind] ?? 'GPS alınmadı')
      return
    }
    const body = { latitude: geo.coords.latitude, longitude: geo.coords.longitude, photoBase64: null }
    const res =
      action.kind === 'checkin'
        ? await checkInFieldVisit(action.visitId, body)
        : action.kind === 'checkout'
          ? await checkOutFieldVisit(action.visitId, body)
          : await startFieldVisit({ ...body, targetLabel: action.label })
    setBusy(false)
    if (res.status === 200) {
      setInfo(action.kind === 'checkout' ? 'Çıxış qeyd olundu ✓' : 'Ərazidə qeyd olundunuz ✓')
      await load()
    } else {
      setError('Alınmadı — yenidən cəhd edin')
    }
  }

  function newSelfVisit() {
    const label = window.prompt('Harada olduğunuzu yazın (istəyə bağlı — məs. ünvan/obyekt):', '')
    if (label === null) return // cancelled
    void submit({ kind: 'start', label: label.trim() || null })
  }

  return (
    <div className="min-h-screen flex flex-col bg-slate-900 text-white">
      <EmployeeNav title="Səyyar" />

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
                <button
                  disabled={busy}
                  onClick={() => void submit({ kind: 'checkin', visitId: v.id })}
                  className="w-full rounded-xl bg-blue-600 py-3 font-bold disabled:opacity-60 mt-1"
                >
                  Ərazidəyəm
                </button>
              )}

              {v.status === 'CheckedIn' && (
                <button
                  disabled={busy}
                  onClick={() => void submit({ kind: 'checkout', visitId: v.id })}
                  className="w-full rounded-xl bg-emerald-600 py-3 font-bold disabled:opacity-60 mt-1"
                >
                  Çıxış et
                </button>
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
            Əraziyə çatanda «Ərazidəyəm», ayrılanda «Çıxış et» edin. Yeriniz və vaxt qeyd olunur — QR-a ehtiyac yoxdur.
          </p>
        </div>
      </main>
    </div>
  )
}
