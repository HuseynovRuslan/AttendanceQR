import { useEffect, useState } from 'react'
import { SubPageHeader } from '../components/SubPageHeader'
import { SkeletonList } from '../components/employeeBits'
import { IconCheck, IconClock, IconLogout, IconMapPin, IconRefresh } from '../components/icons'
import {
  getMyFieldVisits,
  startFieldVisit,
  getFieldSites,
  type FieldSite,
  checkInFieldVisit,
  setChecklistItem,
  type ChecklistItem,
  type MyFieldVisit,
} from '../api/fieldVisits'
import { FieldCheckoutSheet } from '../components/FieldCheckoutSheet'
import { applyPendingTicks, writePendingTick } from '../lib/pendingTicks'
import { getMyProfile } from '../api/attendance'
import { getPosition } from '../lib/geo'
import { fmtDayMonth, fmtDuration, fmtTime } from '../lib/format'
import { todayStr } from '../lib/att'

const GEO_MSG: Record<string, string> = {
  denied: 'GPS icazəsi bağlıdır — brauzer/telefon ayarlarından yeri açın',
  unavailable: 'GPS mövqe tapılmadı — açıq havaya çıxıb yenidən yoxlayın',
  timeout: 'GPS gec cavab verdi — yenidən cəhd edin',
  unsupported: 'Bu cihaz GPS-i dəstəkləmir',
}

type Msg = { kind: 'err' | 'ok'; text: string }

const fmtMeters = (d: number) => (d < 1000 ? `${Math.round(d)} m` : `${(d / 1000).toFixed(1)} km`)

const titleOf = (v: MyFieldVisit) => v.targetLabel || (v.selfReported ? 'Sərbəst ziyarət' : 'Sahə ziyarəti')


/**
 * The worker's field-visit screen (/field) — light, professional, self-contained (owns its shell, no
 * bottom tab bar). Violet is the field signature, matching the home FieldVisitCards. Photo-less GPS
 * check-in/out that NEVER blocks; a distance-from-target chip is advisory only. Only reachable by an
 * employee an admin granted CanFieldCheckIn (menu row is gated; the backend also 403s /start).
 */
export function FieldVisitsPage() {
  const [visits, setVisits] = useState<MyFieldVisit[]>([])
  const [loading, setLoading] = useState(true)
  const [busyId, setBusyId] = useState<string | null>(null)
  const [msg, setMsg] = useState<Record<string, Msg | undefined>>({})
  const [topMsg, setTopMsg] = useState<Msg | null>(null)
  // The visit whose check-out sheet is open (tick → photograph the work → leave).
  const [checkoutVisit, setCheckoutVisit] = useState<MyFieldVisit | null>(null)
  const [now, setNow] = useState(() => Date.now())
  const [canSelfReport, setCanSelfReport] = useState(false)
  const [sheet, setSheet] = useState(false)

  async function load() {
    const res = await getMyFieldVisits().catch(() => null)
    // Ticks the server has not acknowledged are layered back on top, so a tick that failed ten
    // minutes ago still shows as ticked instead of silently undoing itself.
    if (res && res.status === 200 && Array.isArray(res.data)) setVisits(res.data.map(applyPendingTicks))
    // A thrown/offline load (res === null) must NOT read as "no field work" — a worker with a real
    // assigned visit would then never check in. Surface it as an error instead.
    else setTopMsg({ kind: 'err', text: 'Yüklənmədi — internet bağlantısını yoxlayın' })
    setLoading(false)
  }
  useEffect(() => {
    void load()
    void getMyProfile().then((r) => {
      if (r.status === 200 && r.data && 'fullName' in r.data) setCanSelfReport(r.data.canFieldCheckIn === true)
    })
  }, [])

  // Live "how long on site" ticker — cheap, and only matters while a visit is open.
  useEffect(() => {
    const t = window.setInterval(() => setNow(Date.now()), 30_000)
    return () => window.clearInterval(t)
  }, [])
  // Don't let the two-tap checkout timer fire after the screen is gone.

  // ARRIVAL only — the departure now goes through FieldCheckoutSheet (tick, photograph the work,
  // leave). One tap → GPS → recorded, photo-less. Wrapped: apiRequest rejects on a network drop or a
  // non-JSON gateway body, so a failure flags in-card and finally always clears busy.
  //
  // Arrival still requires GPS: for a field visit the position IS the proof of presence, and a
  // check-in with no location would be nothing but a claim. The DEPARTURE deliberately does not —
  // there the record is what stops the clock, and losing it costs the worker hours.
  async function act(v: MyFieldVisit) {
    setBusyId(v.id)
    setMsg((m) => ({ ...m, [v.id]: undefined }))
    try {
      const geo = await getPosition()
      if (!geo.ok) {
        setMsg((m) => ({ ...m, [v.id]: { kind: 'err', text: GEO_MSG[geo.kind] ?? 'GPS alınmadı' } }))
        return
      }
      const body = { latitude: geo.coords.latitude, longitude: geo.coords.longitude, photoBase64: null }
      const res = await checkInFieldVisit(v.id, body)
      if (res.status === 200) {
        setMsg((m) => ({ ...m, [v.id]: { kind: 'ok', text: 'Ərazidə qeyd olundunuz ✓' } }))
        await load()
      } else {
        setMsg((m) => ({ ...m, [v.id]: { kind: 'err', text: 'Alınmadı — yenidən cəhd edin' } }))
      }
    } catch {
      setMsg((m) => ({ ...m, [v.id]: { kind: 'err', text: 'Alınmadı — yenidən cəhd edin' } }))
    } finally {
      setBusyId(null)
    }
  }

  // Ticking is never a network operation from the worker's point of view: the row flips instantly,
  // the POST is fire-and-forget, and a failure leaves the tick standing (the check-out reconciles it).
  function tick(v: MyFieldVisit, item: ChecklistItem, isDone: boolean) {
    setVisits((vs) =>
      vs.map((x) =>
        x.id !== v.id
          ? x
          : {
              ...x,
              checklist: x.checklist.map((i) => (i.id === item.id ? { ...i, isDone } : i)),
              checklistDone: x.checklist.filter((i) => (i.id === item.id ? isDone : i.isDone)).length,
            },
      ),
    )
    navigator.vibrate?.(10)
    writePendingTick(v.id, item.id, isDone)
    void setChecklistItem(v.id, item.id, isDone)
      .then((r) => {
        if (r.status === 200) writePendingTick(v.id, item.id, isDone, true)
      })
      .catch(() => { /* stays pending; the departure payload settles it */ })
  }

  const active = visits
    .filter((v) => v.status === 'Assigned' || v.status === 'CheckedIn')
    .sort((a, b) => (a.status === 'CheckedIn' ? -1 : 1) - (b.status === 'CheckedIn' ? -1 : 1))
  const done = visits.filter((v) => v.status === 'Completed')

  return (
    <div className="min-h-screen bg-slate-50 text-slate-900">
      <SubPageHeader title="Səyyar ziyarət" back="/menu" />

      <main className="mx-auto flex w-full max-w-md flex-col gap-4 p-4 pb-24">
        <div className="flex items-center justify-between px-1">
          <div className="text-sm font-semibold text-slate-400">Bu gün · {fmtDayMonth(todayStr())}</div>
          <button
            onClick={() => void load()}
            className="flex items-center gap-1 text-sm font-semibold text-violet-600 transition active:opacity-70"
          >
            <IconRefresh className="h-4 w-4" /> Yenilə
          </button>
        </div>

        {topMsg && (
          <div
            className={`rounded-2xl p-3 text-sm font-semibold ${
              topMsg.kind === 'ok' ? 'bg-emerald-50 text-emerald-700' : 'bg-red-50 text-red-600'
            }`}
          >
            {topMsg.text}
          </div>
        )}

        {loading ? (
          <SkeletonList />
        ) : (
          <>
            {active.length > 0 && (
              <div className="flex flex-col gap-3">
                <div className="px-1 text-sm font-bold text-slate-500">Aktiv ziyarətlər</div>
                {active.map((v) => (
                  <VisitCard
                    key={v.id}
                    v={v}
                    now={now}
                    busy={busyId === v.id}
                    locked={busyId !== null}
                    msg={msg[v.id]}
                    onCheckIn={() => void act(v)}
                    onCheckout={() => setCheckoutVisit(v)}
                    onTick={(item, isDone) => tick(v, item, isDone)}
                  />
                ))}
              </div>
            )}

            {done.length > 0 && (
              <div className="flex flex-col gap-3">
                <div className="px-1 text-sm font-bold text-slate-500">Bu gün tamamlandı</div>
                {done.map((v) => (
                  <VisitCard key={v.id} v={v} now={now} busy={false} locked={false} msg={msg[v.id]} />
                ))}
              </div>
            )}

            {active.length === 0 && done.length === 0 && (
              <div className="rounded-3xl border border-slate-100 bg-white p-8 text-center shadow-sm">
                <div className="mx-auto grid h-14 w-14 place-items-center rounded-full bg-violet-50 text-violet-600">
                  <IconMapPin className="h-7 w-7" />
                </div>
                <div className="mt-3 font-bold">Bu gün sahə ziyarəti yoxdur</div>
                <div className="mt-1 text-sm text-slate-500">
                  {canSelfReport
                    ? 'Ad-hoc bir yerə getmisinizsə, ziyarəti özünüz əlavə edin.'
                    : 'Sizə tapşırılan səyyar iş olduqda burada görünəcək.'}
                </div>
              </div>
            )}

            {canSelfReport && (
              <button
                onClick={() => setSheet(true)}
                className="w-full rounded-2xl border border-violet-200 bg-violet-50 py-3 font-bold text-violet-700 transition active:scale-[0.99]"
              >
                + Yeni sahə ziyarəti
              </button>
            )}

            <p className="px-2 text-center text-xs text-slate-400">
              Əraziyə çatanda «Ərazidəyəm», iş bitəndə işin şəklini çəkib «Çıxış et» edin. Şəkil olmasa da
              çıxışınız qeyd olunur.
            </p>
          </>
        )}
      </main>

      {checkoutVisit && (
        <FieldCheckoutSheet
          visit={checkoutVisit}
          onClose={() => setCheckoutVisit(null)}
          onDone={async ({ photoPending }) => {
            setCheckoutVisit(null)
            setTopMsg({
              kind: 'ok',
              text: photoPending ? 'Çıxış qeyd olundu ✓ · Şəkil göndərilir…' : 'Çıxış qeyd olundu ✓',
            })
            window.setTimeout(() => setTopMsg(null), 4000)
            await load()
          }}
        />
      )}

      {sheet && (
        <SelfReportSheet
          onClose={() => setSheet(false)}
          onDone={async () => {
            setSheet(false)
            setTopMsg({ kind: 'ok', text: 'Ərazidə qeyd olundunuz ✓' })
            window.setTimeout(() => setTopMsg(null), 3000)
            await load()
          }}
        />
      )}
    </div>
  )
}

type VisitCardProps = {
  v: MyFieldVisit
  now: number
  busy: boolean
  locked: boolean
  msg?: Msg
  onCheckIn?: () => void
  onCheckout?: () => void
  onTick?: (item: ChecklistItem, isDone: boolean) => void
}

function VisitCard({ v, now, busy, locked, msg, onCheckIn, onCheckout, onTick }: VisitCardProps) {
  const emblem =
    v.status === 'Assigned'
      ? 'bg-violet-100 text-violet-700'
      : v.status === 'CheckedIn'
        ? 'bg-amber-100 text-amber-700'
        : 'bg-emerald-50 text-emerald-600'
  const pill =
    v.status === 'Assigned'
      ? { c: 'bg-violet-50 text-violet-700', t: 'Tapşırılıb' }
      : v.status === 'CheckedIn'
        ? { c: 'bg-amber-50 text-amber-700', t: 'Ərazidə' }
        : { c: 'bg-emerald-50 text-emerald-700', t: 'Tamamlandı' }

  return (
    <div
      className={`rounded-3xl border bg-white p-4 shadow-sm ${
        v.status === 'CheckedIn' ? 'border-amber-200' : 'border-slate-100'
      } ${v.status === 'Completed' ? 'opacity-90' : ''}`}
    >
      <div className="flex items-start gap-3">
        <div className={`relative grid h-11 w-11 shrink-0 place-items-center rounded-2xl ${emblem}`}>
          {v.status === 'Completed' ? <IconCheck className="h-5 w-5" /> : <IconMapPin className="h-5 w-5" />}
          {v.status === 'CheckedIn' && (
            <span className="absolute -right-0.5 -top-0.5 h-2.5 w-2.5 animate-pulse rounded-full bg-emerald-500 ring-2 ring-white" />
          )}
        </div>
        <div className="min-w-0 flex-1">
          <div className="truncate font-bold leading-tight">{titleOf(v)}</div>
          <VisitMeta v={v} now={now} />
        </div>
        <span className={`shrink-0 rounded-full px-2 py-1 text-xs font-bold ${pill.c}`}>{pill.t}</span>
      </div>

      <DistanceChip v={v} />

      <VisitChecklist v={v} onTick={onTick} />

      {v.status === 'Assigned' && (
        <button
          disabled={locked}
          onClick={onCheckIn}
          className="mt-3 flex w-full items-center justify-center gap-2 rounded-2xl bg-violet-600 py-3 font-bold text-white transition active:scale-[.99] disabled:opacity-60"
        >
          {busy ? <Spinner label="Yer təyin olunur…" /> : <><IconMapPin className="h-5 w-5" /> Ərazidəyəm</>}
        </button>
      )}

      {/* The old two-tap morph-confirm is gone from here: the check-out sheet IS the deliberate
          second step, and two confirms in a row reads as nagging. The guard survives inside the
          sheet, on the one tap that still ends a visit without a multi-step gesture in front of it
          («Şəkilsiz çıx»). */}
      {v.status === 'CheckedIn' && (
          <button
            disabled={locked}
            onClick={onCheckout}
            className="mt-3 flex w-full items-center justify-center gap-2 rounded-2xl bg-emerald-600 py-3 font-bold text-white transition active:scale-[.99] disabled:opacity-60"
          >
            <IconLogout className="h-5 w-5" /> Çıxış et
          </button>
      )}

      {msg && <div className={`mt-2 text-sm ${msg.kind === 'err' ? 'text-red-600' : 'text-emerald-600'}`}>{msg.text}</div>}
    </div>
  )
}

/**
 * The work the manager asked for. Renders nothing when there is no list (self-reports, plain visits).
 * The whole ROW is the tap target — a 20px checkbox is unusable with gloves on, in sun glare, one
 * thumb. Ticked labels go grey but are never struck through: thin struck text is unreadable outdoors.
 */
function VisitChecklist({ v, onTick }: { v: MyFieldVisit; onTick?: (i: ChecklistItem, d: boolean) => void }) {
  if (v.checklistTotal === 0) return null
  const doneCount = v.checklist.filter((i) => i.isDone).length
  const frozen = v.status === 'Completed' || !onTick
  const title = v.status === 'Assigned' ? 'Görüləcək işlər' : 'Yoxlama siyahısı'

  return (
    <div className="mt-3 rounded-2xl border border-slate-100 bg-slate-50/60 p-1">
      <div className="flex items-center justify-between px-2 py-1.5">
        <span className="text-xs font-bold uppercase tracking-wide text-slate-500">{title}</span>
        <span className="text-xs font-bold text-slate-500 tabular-nums">
          {doneCount}/{v.checklistTotal}
        </span>
      </div>
      {v.status === 'CheckedIn' && (
        <div className="mx-2 mb-1 h-[3px] overflow-hidden rounded-full bg-slate-200">
          <div
            className="h-full rounded-full bg-violet-600 transition-[width]"
            style={{ width: `${Math.round((doneCount / v.checklistTotal) * 100)}%` }}
          />
        </div>
      )}
      <div className="divide-y divide-slate-100">
        {v.checklist.map((i) => (
          <button
            key={i.id}
            disabled={frozen}
            onClick={() => onTick?.(i, !i.isDone)}
            className="flex min-h-[52px] w-full items-center gap-3 px-2 text-left disabled:cursor-default"
          >
            <span
              className={`grid h-7 w-7 shrink-0 place-items-center rounded-lg border-2 transition ${
                i.isDone ? 'border-violet-600 bg-violet-600 text-white' : 'border-slate-300'
              }`}
            >
              {i.isDone && <IconCheck className="h-4 w-4" />}
            </span>
            <span className={`text-[15px] font-semibold ${i.isDone ? 'text-slate-400' : 'text-slate-700'}`}>
              {i.label}
            </span>
            {frozen && !i.isDone && <span className="ml-auto shrink-0 text-xs text-slate-400">işarələnmədi</span>}
          </button>
        ))}
      </div>
    </div>
  )
}

function VisitMeta({ v, now }: { v: MyFieldVisit; now: number }) {
  if (v.status === 'Assigned') {
    return (
      <div className="truncate text-sm text-slate-500">
        {v.assignedByName ? `Tapşıran: ${v.assignedByName}` : (v.note ?? 'Yeni sahə tapşırığı')}
      </div>
    )
  }
  if (v.status === 'CheckedIn') {
    const elapsed = v.checkInAtUtc ? fmtDuration(v.checkInAtUtc, new Date(now).toISOString()) : ''
    return (
      <>
        <div className="text-sm font-semibold text-amber-700">Ərazidəsən — çıxışı unutma</div>
        <div className="mt-0.5 flex items-center gap-1 text-xs text-slate-500">
          <IconClock className="h-3.5 w-3.5" /> {elapsed} ərazidəsən · Giriş {fmtTime(v.checkInAtUtc)}
        </div>
      </>
    )
  }
  const worked = v.checkInAtUtc && v.checkOutAtUtc ? ` · ${fmtDuration(v.checkInAtUtc, v.checkOutAtUtc)}` : ''
  return (
    <div className="text-sm text-slate-500">
      Giriş {fmtTime(v.checkInAtUtc)} · Çıxış {fmtTime(v.checkOutAtUtc)}
      {worked}
    </div>
  )
}

/** Advisory distance-from-target chip. Never a block — a far GPS only flags. */
function DistanceChip({ v }: { v: MyFieldVisit }) {
  if (v.status === 'Assigned') {
    if (v.targetLatitude == null) return null
    return (
      <div className="mt-2 inline-flex items-center gap-1 rounded-full bg-violet-50 px-2 py-0.5 text-xs font-semibold text-violet-700">
        🎯 Hədəf təyin olunub · {v.targetRadiusMeters ?? 200} m
      </div>
    )
  }
  if (v.checkInDistanceMeters == null) {
    if (!v.checkInAtUtc) return null
    return (
      <div className="mt-2 inline-flex items-center gap-1 rounded-full bg-slate-100 px-2 py-0.5 text-xs font-semibold text-slate-500">
        📍 Yer qeyd olundu
      </div>
    )
  }
  const within = v.targetRadiusMeters != null && v.checkInDistanceMeters <= v.targetRadiusMeters
  return (
    <div
      className={`mt-2 inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-semibold ${
        within ? 'bg-emerald-50 text-emerald-700' : 'bg-amber-50 text-amber-700'
      }`}
    >
      {within ? `✅ Hədəfdə · ${fmtMeters(v.checkInDistanceMeters)}` : `⚠️ Hədəfdən ${fmtMeters(v.checkInDistanceMeters)} uzaq`}
    </div>
  )
}

function Spinner({ label }: { label: string }) {
  return (
    <>
      <span className="h-4 w-4 animate-spin rounded-full border-2 border-white/40 border-t-white" />
      {label}
    </>
  )
}

/** Self-report a visit — one optional label, then GPS check-in. Replaces the old window.prompt. */
/**
 * Filing your own visit.
 *
 * It used to be one autofocused text box labelled «Ünvan / obyekt». The keyboard opened and people
 * typed a sentence: production holds «Obyektdeyem», «Obyektdəyəm», «Obyekt deyem» and «Obyektdeyem»
 * — four spellings of «I am at the site», which is not a place — and two spellings each of one
 * office and one café. The admin board's only «where» column was built on that, so a manager running
 * two parks opened the day and every row read the same.
 *
 * Now the branches come first, nearest at the top, because the worker is STANDING at one of them and
 * the phone already knows which. Typing is still possible — «Başqa ünvan» — for the genuinely ad-hoc
 * site the list cannot contain, which is what this feature was built for in the first place. It is
 * simply no longer the path of least resistance.
 */
function SelfReportSheet({ onClose, onDone }: { onClose: () => void; onDone: () => Promise<void> }) {
  const [sites, setSites] = useState<FieldSite[] | null>(null)
  const [picked, setPicked] = useState<string | null>(null)
  const [other, setOther] = useState(false)
  const [label, setLabel] = useState('')
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState<string | null>(null)
  // Taken ONCE, when the sheet opens, and reused for the check-in. Asking twice would mean two
  // permission moments and two chances for the answer to differ from the list they just chose from.
  const [pos, setPos] = useState<{ lat: number; lng: number } | null>(null)

  useEffect(() => {
    let alive = true
    void (async () => {
      const geo = await getPosition()
      const coords = geo.ok ? { lat: geo.coords.latitude, lng: geo.coords.longitude } : null
      if (!alive) return
      setPos(coords)
      const { status, data } = await getFieldSites(coords?.lat, coords?.lng)
      if (!alive) return
      setSites(status === 200 && Array.isArray(data) ? data : [])
    })()
    return () => { alive = false }
  }, [])

  async function submit() {
    setBusy(true)
    setErr(null)
    try {
      // The position from the list step when we have it; otherwise ask now — a failure here is the
      // one thing that must stop the visit, because a field check-in with no coordinates is a claim
      // with nothing behind it.
      let coords = pos
      if (!coords) {
        const geo = await getPosition()
        if (!geo.ok) { setErr(GEO_MSG[geo.kind] ?? 'GPS alınmadı'); return }
        coords = { lat: geo.coords.latitude, lng: geo.coords.longitude }
      }

      const chosen = picked ? sites?.find((x) => x.id === picked)?.name ?? null : null
      const res = await startFieldVisit({
        latitude: coords.lat,
        longitude: coords.lng,
        photoBase64: null,
        targetLabel: chosen ?? (label.trim() || null),
      })
      if (res.status === 200) await onDone()
      else if (res.status === 403) setErr('Sizə səyyar giriş icazəsi verilməyib')
      else setErr('Alınmadı — yenidən cəhd edin')
    } catch {
      setErr('Alınmadı — yenidən cəhd edin')
    } finally {
      setBusy(false)
    }
  }

  const ready = picked !== null || (other && label.trim().length > 0)

  return (
    <div className="fixed inset-0 z-50">
      <div className="absolute inset-0 bg-slate-900/40 backdrop-blur-sm" onClick={busy ? undefined : onClose} />
      <div className="absolute inset-x-0 bottom-0 max-h-[85vh] overflow-y-auto rounded-t-3xl bg-white p-5 pb-8 shadow-2xl">
        <div className="mx-auto mb-4 h-1.5 w-10 rounded-full bg-slate-200" />
        <div className="text-lg font-bold">Haradasınız?</div>
        <div className="mt-1 text-sm text-slate-500">
          QR lazım deyil — yeriniz və vaxt yazılacaq.
        </div>

        {sites === null && <div className="mt-4"><Spinner label="Yaxınlıqdakılar axtarılır…" /></div>}

        {sites !== null && sites.length > 0 && !other && (
          <div className="mt-4 flex flex-col gap-2">
            {sites.slice(0, 6).map((sIt) => (
              <button
                key={sIt.id}
                type="button"
                onClick={() => setPicked(sIt.id)}
                className={`flex items-center justify-between rounded-2xl border px-4 py-3 text-left transition ${
                  picked === sIt.id
                    ? 'border-violet-500 bg-violet-50 font-bold text-violet-900'
                    : 'border-slate-200 bg-white font-semibold text-slate-800'
                }`}
              >
                <span className="min-w-0 truncate">{sIt.name}</span>
                {/* The distance is the reason the top one is usually right, so it is shown rather
                    than merely used for sorting. */}
                {sIt.distanceMeters != null && (
                  <span className="ml-3 shrink-0 text-xs font-semibold text-slate-400">
                    {sIt.distanceMeters >= 1000
                      ? `${(sIt.distanceMeters / 1000).toFixed(1)} km`
                      : `${sIt.distanceMeters} m`}
                  </span>
                )}
              </button>
            ))}
          </div>
        )}

        {sites !== null && (other || sites.length === 0) && (
          <>
            <label className="mt-4 block text-sm font-semibold text-slate-600">Ünvan / obyekt</label>
            <input
              autoFocus
              value={label}
              onChange={(e) => setLabel(e.target.value)}
              onKeyDown={(e) => { if (e.key === 'Enter' && !busy && ready) void submit() }}
              placeholder="Məs. Nizami küç. 12"
              className="mt-1 w-full rounded-2xl border border-slate-200 px-4 py-3 focus:border-violet-500 focus:outline-none focus:ring-2 focus:ring-violet-100"
            />
          </>
        )}

        {sites !== null && sites.length > 0 && (
          <button
            type="button"
            onClick={() => { setOther((v) => !v); setPicked(null); setLabel('') }}
            className="mt-3 w-full py-2 text-sm font-semibold text-violet-600"
          >
            {other ? '← Filiallardan seç' : 'Siyahıda yoxdur — başqa ünvan yazım'}
          </button>
        )}

        {err && <div className="mt-2 text-sm text-red-600">{err}</div>}

        <button
          disabled={busy || !ready}
          onClick={() => void submit()}
          className="mt-4 flex w-full items-center justify-center gap-2 rounded-2xl bg-violet-600 py-3 font-bold text-white transition active:scale-[.99] disabled:opacity-50"
        >
          {busy ? <Spinner label="Qeyd olunur…" /> : <><IconMapPin className="h-5 w-5" /> Ərazidəyəm</>}
        </button>
        <button onClick={onClose} disabled={busy} className="mt-2 w-full py-2 font-semibold text-slate-500 disabled:opacity-60">
          Ləğv et
        </button>
      </div>
    </div>
  )
}
