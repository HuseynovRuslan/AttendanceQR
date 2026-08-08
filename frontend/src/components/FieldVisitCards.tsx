import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { checkInFieldVisit, checkOutFieldVisit, type MyFieldVisit } from '../api/fieldVisits'
import { getPosition } from '../lib/geo'
import { fmtTime } from '../lib/format'
import { IconCheck, IconLogout, IconMapPin } from './icons'

/** GPS failure copy — mirrors FieldVisitPage so a check-in never dead-ends silently. */
const GEO_MSG: Record<string, string> = {
  denied: 'GPS icazəsi bağlıdır — brauzer/telefon ayarlarından yeri açın',
  unavailable: 'GPS mövqe tapılmadı — açıq havaya çıxıb yenidən yoxlayın',
  timeout: 'GPS gec cavab verdi — yenidən cəhd edin',
  unsupported: 'Bu cihaz GPS-i dəstəkləmir',
}

/** Most time-sensitive first: an open checkout on top, done work at the bottom. */
const ORDER: Record<string, number> = { CheckedIn: 0, Assigned: 1, Completed: 2 }

type Msg = { kind: 'err' | 'ok'; text: string }

const titleOf = (v: MyFieldVisit) => v.targetLabel || (v.selfReported ? 'Sərbəst ziyarət' : 'Sahə ziyarəti')

function sublineOf(v: MyFieldVisit): string {
  if (v.status === 'Assigned') return v.assignedByName ? `Tapşıran: ${v.assignedByName}` : (v.note ?? 'Yeni sahə tapşırığı')
  if (v.status === 'CheckedIn') return `Giriş ${fmtTime(v.checkInAtUtc)}`
  return `Giriş ${fmtTime(v.checkInAtUtc)} · Çıxış ${fmtTime(v.checkOutAtUtc)}`
}

/**
 * Field-visit check-in, brought onto the employee home — but only for the worker who actually has field
 * work today. Violet is the field signature (distinct from the green/blue/red QR ScanHero on the same
 * page). Renders ONE gradient hero for the most-urgent visit + compact rows for any extras; nothing at
 * all when the worker has no field visit. HomePage owns the visits (one shared /mine call) and passes an
 * onChanged reload; we own only the in-flight UI state.
 */
export function FieldVisitCards({
  visits,
  onChanged,
  canFieldCheckIn,
}: {
  visits: MyFieldVisit[]
  onChanged: () => Promise<void>
  canFieldCheckIn?: boolean
}) {
  const navigate = useNavigate()
  const [busyId, setBusyId] = useState<string | null>(null)
  const [msg, setMsg] = useState<Record<string, Msg | undefined>>({})

  const shown = visits
    .filter((v) => v.status === 'Assigned' || v.status === 'CheckedIn' || v.status === 'Completed')
    .sort((a, b) => (ORDER[a.status] ?? 9) - (ORDER[b.status] ?? 9))
  if (shown.length === 0) return null

  // One tap → GPS → check-in/out. Sibling buttons lock while one is in flight. apiRequest REJECTS on a
  // network drop or non-JSON gateway body, so the whole thing is wrapped: a failure flags in-card and
  // lets them retry, and finally always clears busy — never leave a field worker stuck (project rule).
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
        await onChanged()
      } else {
        setMsg((m) => ({ ...m, [v.id]: { kind: 'err', text: 'Alınmadı — yenidən cəhd edin' } }))
      }
    } catch {
      setMsg((m) => ({ ...m, [v.id]: { kind: 'err', text: 'Alınmadı — yenidən cəhd edin' } }))
    } finally {
      setBusyId(null)
    }
  }

  const [featured, ...rest] = shown
  const locked = busyId !== null

  return (
    <div className="flex flex-col gap-3">
      {shown.length >= 2 && <div className="px-1 text-sm font-bold text-slate-500">Sahə tapşırıqları</div>}

      <FeaturedCard v={featured} busy={busyId === featured.id} locked={locked} msg={msg[featured.id]} onAct={() => void act(featured)} />

      {rest.map((v) => (
        <CompactRow key={v.id} v={v} busy={busyId === v.id} locked={locked} msg={msg[v.id]} onAct={() => void act(v)} />
      ))}

      {/* Self-report keeps the label step on /field — shown only to permitted field workers. */}
      {canFieldCheckIn && (
        <button
          disabled={locked}
          onClick={() => navigate('/field')}
          className="w-full rounded-2xl border border-violet-200 bg-violet-50 py-2.5 text-sm font-bold text-violet-700 disabled:opacity-60 active:scale-[0.99]"
        >
          + Yeni sahə ziyarəti
        </button>
      )}
    </div>
  )
}

type CardProps = { v: MyFieldVisit; busy: boolean; locked: boolean; msg?: Msg; onAct: () => void }

/** The single most-urgent visit, shown large: a violet GPS hero when actionable, a quiet card when done. */
function FeaturedCard({ v, busy, locked, msg, onAct }: CardProps) {
  if (v.status === 'Completed') {
    return (
      <div className="rounded-3xl border border-violet-100 bg-violet-50/60 p-5">
        <div className="flex items-start gap-3">
          <div className="grid h-10 w-10 shrink-0 place-items-center rounded-2xl bg-violet-100 text-violet-600">
            <IconCheck className="h-5 w-5" />
          </div>
          <div className="min-w-0 flex-1">
            <div className="truncate font-bold text-slate-700">{titleOf(v)}</div>
            <div className="text-sm text-slate-500">{sublineOf(v)}</div>
          </div>
          <span className="shrink-0 rounded-full bg-violet-100 px-2 py-1 text-xs font-bold text-violet-700">Tamamlandı</span>
        </div>
      </div>
    )
  }

  const checkedIn = v.status === 'CheckedIn'
  return (
    <div className="relative w-full overflow-hidden rounded-3xl bg-gradient-to-br from-violet-600 to-purple-600 p-6 text-white shadow-lg shadow-violet-600/25">
      {/* Code-drawn field illustration: corner pin watermark + GPS radar rings (live ping when on-site). */}
      <IconMapPin className="pointer-events-none absolute -right-4 -top-5 h-28 w-28 rotate-[-8deg] opacity-[0.16]" />
      <div className="pointer-events-none absolute -bottom-8 -right-2 h-24 w-24 rounded-full border border-white/10" />
      <div className="pointer-events-none absolute -bottom-2 right-5 h-12 w-12 rounded-full border border-white/10" />
      {checkedIn && <div className="pointer-events-none absolute -bottom-8 -right-2 h-24 w-24 animate-ping rounded-full border border-white/20" />}

      <div className="relative flex items-start gap-3">
        <div className="relative grid h-12 w-12 shrink-0 place-items-center rounded-2xl bg-white/15 backdrop-blur-sm">
          <IconMapPin className="h-6 w-6" />
          {checkedIn && <span className="absolute -right-0.5 -top-0.5 h-2.5 w-2.5 rounded-full bg-emerald-300 ring-2 ring-violet-600" />}
        </div>
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-1.5 text-xs font-bold uppercase tracking-wider opacity-85">
            {checkedIn && <span className="h-2 w-2 animate-pulse rounded-full bg-emerald-300" />}
            {checkedIn ? 'Ərazidəsən' : 'Sahə tapşırığı'}
          </div>
          <div className="mt-1 truncate text-2xl font-extrabold leading-tight">{titleOf(v)}</div>
          <div className="mt-1 text-sm opacity-90">
            {checkedIn ? (
              <>
                Giriş {fmtTime(v.checkInAtUtc)} ·{' '}
                <span className="rounded-full bg-amber-400/20 px-2 py-0.5 text-amber-100">çıxışı unutma</span>
              </>
            ) : (
              sublineOf(v)
            )}
          </div>
        </div>
      </div>

      <button
        disabled={locked}
        onClick={onAct}
        className="mt-5 flex w-full items-center justify-center gap-2 rounded-2xl bg-white py-3.5 text-base font-extrabold text-violet-700 transition active:scale-[.99] disabled:opacity-70"
      >
        {busy ? (
          <>
            <span className="h-4 w-4 animate-spin rounded-full border-2 border-violet-300 border-t-violet-700" />
            Yer təyin olunur…
          </>
        ) : checkedIn ? (
          <>
            <IconLogout className="h-5 w-5" /> Çıxış et
          </>
        ) : (
          <>
            <IconMapPin className="h-5 w-5" /> Ərazidəyəm
          </>
        )}
      </button>
      {!busy && !msg && <div className="mt-1.5 text-center text-xs opacity-70">GPS + vaxt ilə qeyd olunur</div>}
      {msg && <div className="mt-3 rounded-xl bg-white/15 px-3 py-2 text-sm">{msg.text}</div>}
    </div>
  )
}

/** A collapsed extra visit — compact white row with its own action. */
function CompactRow({ v, busy, locked, msg, onAct }: CardProps) {
  const emblem =
    v.status === 'Assigned'
      ? 'bg-violet-50 text-violet-600'
      : v.status === 'CheckedIn'
        ? 'bg-amber-50 text-amber-600'
        : 'bg-emerald-50 text-emerald-600'
  const badge =
    v.status === 'Assigned'
      ? { c: 'bg-violet-50 text-violet-700', t: 'Tapşırılıb' }
      : v.status === 'CheckedIn'
        ? { c: 'bg-amber-50 text-amber-700', t: 'Ərazidə' }
        : { c: 'bg-emerald-50 text-emerald-700', t: 'Tamamlandı' }
  const actionable = v.status === 'Assigned' || v.status === 'CheckedIn'

  return (
    <div className="rounded-2xl border border-slate-100 bg-white p-4 shadow-sm">
      <div className="flex items-center gap-3">
        <div className={`grid h-10 w-10 shrink-0 place-items-center rounded-2xl ${emblem}`}>
          {v.status === 'Completed' ? <IconCheck className="h-5 w-5" /> : <IconMapPin className="h-5 w-5" />}
        </div>
        <div className="min-w-0 flex-1">
          <div className="truncate font-bold leading-tight">{titleOf(v)}</div>
          <div className="truncate text-sm text-slate-500">{sublineOf(v)}</div>
        </div>
        <span className={`shrink-0 rounded-full px-2 py-1 text-xs font-bold ${badge.c}`}>{badge.t}</span>
      </div>
      {actionable && (
        <button
          disabled={locked}
          onClick={onAct}
          className={`mt-3 flex w-full items-center justify-center gap-2 rounded-xl py-2.5 font-bold text-white transition active:scale-[.99] disabled:opacity-60 ${
            v.status === 'CheckedIn' ? 'bg-emerald-600' : 'bg-violet-600'
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
      {msg && <div className={`mt-2 text-sm ${msg.kind === 'err' ? 'text-red-600' : 'text-emerald-600'}`}>{msg.text}</div>}
    </div>
  )
}
