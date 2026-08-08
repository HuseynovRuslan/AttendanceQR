import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { checkInFieldVisit, checkOutFieldVisit, type MyFieldVisit } from '../api/fieldVisits'
import { getPosition } from '../lib/geo'
import { fmtTime } from '../lib/format'

/** GPS failure copy — mirrors FieldVisitsPage so a check-in never dead-ends silently. */
const GEO_MSG: Record<string, string> = {
  denied: 'GPS icazəsi bağlıdır — brauzer/telefon ayarlarından yeri açın',
  unavailable: 'GPS mövqe tapılmadı — açıq havaya çıxıb yenidən yoxlayın',
  timeout: 'GPS gec cavab verdi — yenidən cəhd edin',
  unsupported: 'Bu cihaz GPS-i dəstəkləmir',
}

/** Most time-sensitive first: an open checkout on top, done work at the bottom. */
const ORDER: Record<string, number> = { CheckedIn: 0, Assigned: 1, Completed: 2 }

type Msg = { kind: 'err' | 'ok'; text: string }

/**
 * Field-visit check-in, brought onto the employee home — but only for the worker who actually has field
 * work today. Replaces the link-only FieldVisitBanner: instead of bouncing to /field, the worker taps
 * «Ərazidəyəm» / «Çıxış et» right here (GPS + time, photo-less, never blocks). For everyone else this
 * renders nothing at all. HomePage owns the visits (one shared /mine call) and gates its auto-scan on
 * them; here we own only the in-flight UI state and call onChanged() to reconcile after a check-in/out.
 */
export function FieldVisitCards({ visits, onChanged }: { visits: MyFieldVisit[]; onChanged: () => Promise<void> }) {
  const navigate = useNavigate()
  const [busyId, setBusyId] = useState<string | null>(null)
  const [msg, setMsg] = useState<Record<string, Msg | undefined>>({})

  // Nothing to show → nothing on screen (no DOM, no layout gap) for the office-worker majority.
  const shown = visits
    .filter((v) => v.status === 'Assigned' || v.status === 'CheckedIn' || v.status === 'Completed')
    .sort((a, b) => (ORDER[a.status] ?? 9) - (ORDER[b.status] ?? 9))
  if (shown.length === 0) return null

  // One tap → GPS → check-in/out. Sibling buttons lock while one is in flight (no double geolocation
  // prompt, no double submit — field POSTs have no idempotency key). apiRequest REJECTS on a network
  // drop or a non-JSON gateway body, so the whole thing is wrapped: a failure flags in-card and lets
  // them retry, and finally always clears busy — it must never leave a field worker stuck (project rule).
  async function act(v: MyFieldVisit) {
    setBusyId(v.id)
    setMsg((m) => ({ ...m, [v.id]: undefined }))
    const checkingOut = v.status === 'CheckedIn'
    try {
      const geo = await getPosition()
      if (!geo.ok) {
        setMsg((m) => ({ ...m, [v.id]: { kind: 'err', text: GEO_MSG[geo.kind] ?? 'GPS alınmadı' } }))
        return
      }
      const body = { latitude: geo.coords.latitude, longitude: geo.coords.longitude, photoBase64: null }
      const res = checkingOut ? await checkOutFieldVisit(v.id, body) : await checkInFieldVisit(v.id, body)
      if (res.status === 200) {
        setMsg((m) => ({
          ...m,
          [v.id]: { kind: 'ok', text: checkingOut ? 'Çıxış qeyd olundu ✓' : 'Ərazidə qeyd olundunuz ✓' },
        }))
        await onChanged() // reconcile from the server; keep the spinner until the card's new state is in
      } else {
        setMsg((m) => ({ ...m, [v.id]: { kind: 'err', text: 'Alınmadı — yenidən cəhd edin' } }))
      }
    } catch {
      setMsg((m) => ({ ...m, [v.id]: { kind: 'err', text: 'Alınmadı — yenidən cəhd edin' } }))
    } finally {
      setBusyId(null)
    }
  }

  return (
    <div className="flex flex-col gap-3">
      {shown.length >= 2 && <div className="px-1 text-sm font-bold text-slate-500">Sahə tapşırıqları</div>}

      {shown.map((v) => {
        const busy = busyId === v.id
        const locked = busyId !== null
        const m = msg[v.id]
        const title = v.targetLabel || (v.selfReported ? 'Sərbəst ziyarət' : 'Sahə ziyarəti')

        return (
          <div key={v.id} className="rounded-3xl border border-slate-100 bg-white p-4 shadow-sm">
            <div className="flex items-start gap-3">
              <div
                className={`flex h-11 w-11 shrink-0 items-center justify-center rounded-2xl text-xl ${
                  v.status === 'CheckedIn'
                    ? 'bg-amber-100 text-amber-700'
                    : v.status === 'Completed'
                      ? 'bg-emerald-50 text-emerald-600'
                      : 'bg-blue-100 text-blue-700'
                }`}
              >
                {v.status === 'Completed' ? '✓' : '📍'}
              </div>

              <div className="min-w-0 flex-1">
                <div className={`truncate font-bold leading-tight ${v.status === 'Completed' ? 'text-slate-500' : ''}`}>
                  {title}
                </div>
                {v.status === 'Assigned' && (
                  <div className="truncate text-sm text-slate-500">
                    {v.assignedByName ? `Tapşıran: ${v.assignedByName}` : v.note ?? ''}
                  </div>
                )}
                {v.status === 'CheckedIn' && (
                  <>
                    <div className="text-sm font-semibold text-amber-700">Ərazidəsən — çıxışı unutma</div>
                    {v.checkInAtUtc && <div className="text-xs text-slate-500">Giriş {fmtTime(v.checkInAtUtc)}</div>}
                  </>
                )}
                {v.status === 'Completed' && (
                  <div className="text-sm text-slate-500">
                    Giriş {fmtTime(v.checkInAtUtc)} · Çıxış {fmtTime(v.checkOutAtUtc)}
                  </div>
                )}
              </div>

              <span
                className={`whitespace-nowrap rounded-full px-2 py-1 text-xs font-bold ${
                  v.status === 'CheckedIn'
                    ? 'bg-amber-50 text-amber-700'
                    : v.status === 'Completed'
                      ? 'bg-emerald-50 text-emerald-700'
                      : 'bg-blue-50 text-blue-700'
                }`}
              >
                {v.status === 'CheckedIn' ? 'Ərazidə' : v.status === 'Completed' ? 'Tamamlandı' : 'Tapşırılıb'}
              </span>
            </div>

            {(v.status === 'Assigned' || v.status === 'CheckedIn') && (
              <button
                disabled={locked}
                onClick={() => void act(v)}
                className={`mt-3 flex w-full items-center justify-center gap-2 rounded-2xl py-3 font-bold text-white disabled:opacity-60 ${
                  v.status === 'CheckedIn' ? 'bg-emerald-600' : 'bg-blue-600'
                }`}
              >
                {busy ? (
                  <>
                    <span className="h-4 w-4 animate-spin rounded-full border-2 border-white/40 border-t-white" />
                    Yer təyin olunur…
                  </>
                ) : v.status === 'CheckedIn' ? (
                  'Çıxış et'
                ) : (
                  'Ərazidəyəm'
                )}
              </button>
            )}

            {m && (
              <div className={`mt-2 text-sm ${m.kind === 'err' ? 'text-red-600' : 'text-emerald-600'}`}>{m.text}</div>
            )}
          </div>
        )
      })}

      {/* Self-report keeps the window.prompt label step that lives on /field. */}
      <button
        disabled={busyId !== null}
        onClick={() => navigate('/field')}
        className="w-full rounded-2xl border border-slate-200 py-2.5 text-sm font-semibold text-slate-500 disabled:opacity-60"
      >
        + Yeni sahə ziyarəti
      </button>
    </div>
  )
}
