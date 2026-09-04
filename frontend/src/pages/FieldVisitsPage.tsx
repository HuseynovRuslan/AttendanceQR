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
import { getPosition, isJunkTargetLabel, reverseGeocode } from '../lib/geo'
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

/** Close enough that the branch IS the answer: 500 m covers a park and its car park, and is far
 *  short of the next branch in central Baku. */
const AT_BRANCH_METRES = 500
/** Beyond this, they are somewhere the branch list does not contain, so the street address is
 *  fetched without waiting for them to ask for it. */
const FAR_FROM_BRANCH_METRES = 1500

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
 * Filing your own visit — one tap, when the answer is obvious.
 *
 * It began as a single autofocused text box, which is where «Obyektdeyem» came from, in four
 * spellings. Then it became a list of branches. This is the last step: when the phone says the
 * worker is thirty metres from Green Garden, the app should not make a cleaner in the rain read a
 * list and choose — it should say so and let her confirm.
 *
 * So the nearest branch is PRE-SELECTED inside 500 m, the button carries its name rather than the
 * word «Ərazidəyəm», and everything else — the other branches, the places she goes often, the free
 * text — is folded away behind one link. Nothing is removed; it is ordered by how often it is right.
 */
function SelfReportSheet({ onClose, onDone }: { onClose: () => void; onDone: () => Promise<void> }) {
  const [sites, setSites] = useState<FieldSite[] | null>(null)
  const [recent, setRecent] = useState<string[]>([])
  const [picked, setPicked] = useState<string | null>(null)
  const [showOther, setShowOther] = useState(false)
  const [showAll, setShowAll] = useState(false)
  const [label, setLabel] = useState('')
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState<string | null>(null)
  const [pos, setPos] = useState<{ lat: number; lng: number } | null>(null)
  const [addrBusy, setAddrBusy] = useState(false)

  useEffect(() => {
    let alive = true
    void (async () => {
      const geo = await getPosition()
      const coords = geo.ok ? { lat: geo.coords.latitude, lng: geo.coords.longitude } : null
      if (!alive) return
      setPos(coords)
      const { status, data } = await getFieldSites(coords?.lat, coords?.lng)
      if (!alive) return
      if (!(status === 200 && data && 'sites' in data)) { setSites([]); return }

      setSites(data.sites)
      // The server already drops one-off labels and the «Obyektdeyem» family; this is the same rule
      // applied on the way in, so an older build of the API cannot put one back on screen.
      setRecent((data.recent ?? []).filter((r) => r.trim().length > 0 && !isJunkTargetLabel(r)))

      const nearest = data.sites[0]
      if (nearest?.distanceMeters != null && nearest.distanceMeters <= AT_BRANCH_METRES) {
        // Thirty metres from her own park IS the answer. Choosing it for her is the difference
        // between one tap and reading a list in the rain.
        setPicked(nearest.id)
      } else if (coords && (!nearest || (nearest.distanceMeters ?? Infinity) > FAR_FROM_BRANCH_METRES)) {
        // Far from everything: she is at an ad-hoc site, so fetch the street address now rather than
        // making her discover the «başqa ünvan» link first. See reverseGeocode — this is the ONE
        // place the app sends a position off our servers.
        setAddrBusy(true)
        void reverseGeocode(coords.lat, coords.lng)
          .then((a) => { if (alive && a) setLabel(a) })
          .finally(() => { if (alive) setAddrBusy(false) })
      }
    })()
    return () => { alive = false }
  }, [])

  async function submit() {
    setBusy(true)
    setErr(null)
    try {
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

  const nearest = sites?.[0] ?? null
  const atNearest = nearest?.distanceMeters != null && nearest.distanceMeters <= AT_BRANCH_METRES
  const chosenSite = picked ? sites?.find((x) => x.id === picked) ?? null : null
  const ready = picked !== null || label.trim().length > 0

  // The button says WHERE, not just "here". «Ərazidəyəm» left the worker confirming something they
  // could not see. The name is appended without a case ending on purpose: «Green Garden-dəyəm» and
  // «Qala Anbar-dayam» need different vowels, and getting that wrong on somebody's own workplace
  // reads worse than not trying.
  const confirmText = chosenSite ? `${chosenSite.name} · Təsdiq et`
    : label.trim() ? `${label.trim()} · Təsdiq et`
      : 'Ərazidəyəm'

  return (
    <div className="fixed inset-0 z-50">
      <div className="absolute inset-0 bg-slate-900/40 backdrop-blur-sm" onClick={busy ? undefined : onClose} />
      <div className="absolute inset-x-0 bottom-0 max-h-[88vh] overflow-y-auto rounded-t-3xl bg-white p-5 pb-8 shadow-2xl">
        <div className="mx-auto mb-4 h-1.5 w-10 rounded-full bg-slate-200" />
        <div className="text-xl font-extrabold text-slate-900">Haradasınız?</div>
        <div className="mt-1 text-sm text-slate-500">
          {atNearest ? 'Yeriniz təyin olundu — təsdiq edin.' : 'Yerinizi seçin və ya yazın.'}
        </div>

        {sites === null && (
          <div className="mt-5 flex items-center justify-center gap-3 rounded-2xl border border-slate-100 bg-slate-50 p-6 text-slate-500">
            <span className="h-5 w-5 animate-spin rounded-full border-2 border-violet-600 border-t-transparent" />
            <span className="text-sm font-semibold">Ən yaxın filial axtarılır…</span>
          </div>
        )}

        {sites !== null && !showOther && nearest && (
          <div className="mt-4 flex flex-col gap-3">
            <button
              type="button"
              onClick={() => { setPicked(nearest.id); setLabel('') }}
              className={`flex flex-col rounded-2xl border-2 p-4 text-left transition ${
                picked === nearest.id ? 'border-emerald-500 bg-emerald-50/70' : 'border-slate-200 bg-white'
              }`}
            >
              <div className="flex items-center justify-between gap-3">
                <span className={`text-[11px] font-bold uppercase tracking-wider ${
                  picked === nearest.id ? 'text-emerald-700' : 'text-slate-500'
                }`}>
                  {atNearest ? 'Hazırda buradasınız' : 'Ən yaxın filial'}
                </span>
                {nearest.distanceMeters != null && (
                  <span className={`shrink-0 rounded-full px-2.5 py-0.5 text-xs font-extrabold ${
                    picked === nearest.id ? 'bg-emerald-100 text-emerald-800' : 'bg-slate-100 text-slate-500'
                  }`}>
                    {nearest.distanceMeters >= 1000
                      ? `${(nearest.distanceMeters / 1000).toFixed(1)} km`
                      : `${nearest.distanceMeters} m`}
                  </span>
                )}
              </div>
              <div className="mt-1.5 text-lg font-extrabold text-slate-900">{nearest.name}</div>
              {picked === nearest.id && (
                <div className="mt-1 text-xs font-semibold text-emerald-700">✓ seçildi</div>
              )}
            </button>

            {sites.length > 1 && (
              showAll ? (
                <div className="flex flex-col gap-2">
                  {sites.slice(1, 6).map((sIt) => (
                    <button
                      key={sIt.id}
                      type="button"
                      onClick={() => { setPicked(sIt.id); setLabel('') }}
                      className={`flex items-center justify-between gap-3 rounded-xl border px-3.5 py-2.5 text-left text-sm transition ${
                        picked === sIt.id
                          ? 'border-violet-500 bg-violet-50 font-bold text-violet-900'
                          : 'border-slate-200 bg-white font-medium text-slate-700'
                      }`}
                    >
                      <span className="min-w-0 truncate">{sIt.name}</span>
                      {sIt.distanceMeters != null && (
                        <span className="shrink-0 text-xs text-slate-400">
                          {sIt.distanceMeters >= 1000
                            ? `${(sIt.distanceMeters / 1000).toFixed(1)} km`
                            : `${sIt.distanceMeters} m`}
                        </span>
                      )}
                    </button>
                  ))}
                </div>
              ) : (
                // Folded away, because the nearest branch is the answer nearly every time and a list
                // of six is six chances to tap the wrong one.
                <button
                  type="button"
                  onClick={() => setShowAll(true)}
                  className="py-2 text-center text-xs font-bold text-slate-500"
                >
                  Digər filiallar ({sites.length - 1}) ↓
                </button>
              )
            )}
          </div>
        )}

        {sites !== null && showOther && (
          <div className="mt-4">
            <div className="flex items-center justify-between">
              <label className="text-xs font-bold uppercase tracking-wider text-slate-500">
                Kənar ünvan və ya obyekt
              </label>
              {addrBusy && <span className="text-xs font-semibold text-violet-600">ünvan tapılır…</span>}
            </div>
            <input
              autoFocus
              value={label}
              onChange={(e) => setLabel(e.target.value)}
              onKeyDown={(e) => { if (e.key === 'Enter' && !busy && ready) void submit() }}
              placeholder={addrBusy ? 'GPS ünvanı tapılır…' : 'Məs. Nizami küç. 12 / Anbar'}
              className="mt-1.5 w-full rounded-2xl border border-slate-200 px-4 py-3.5 text-sm font-semibold focus:border-violet-500 focus:outline-none focus:ring-2 focus:ring-violet-100"
            />
            {/* Only here, where they are already answering "somewhere else": on the branch screen
                these were a second list competing with the one that is usually right. */}
            {recent.length > 0 && (
              <div className="mt-3">
                <div className="text-[11px] font-bold uppercase tracking-wider text-slate-400">
                  Tez-tez getdiyiniz yerlər
                </div>
                <div className="mt-2 flex flex-wrap gap-1.5">
                  {recent.map((r) => (
                    <button
                      key={r}
                      type="button"
                      onClick={() => { setPicked(null); setLabel(label === r ? '' : r) }}
                      className={`rounded-full border px-3 py-1.5 text-xs font-bold transition ${
                        label === r
                          ? 'border-violet-500 bg-violet-50 text-violet-900'
                          : 'border-slate-200 bg-white text-slate-700'
                      }`}
                    >
                      {r}
                    </button>
                  ))}
                </div>
              </div>
            )}
          </div>
        )}

        {sites !== null && (
          <button
            type="button"
            onClick={() => {
              const next = !showOther
              setShowOther(next)
              if (next) {
                setPicked(null)
                if (pos && !label) {
                  setAddrBusy(true)
                  void reverseGeocode(pos.lat, pos.lng)
                    .then((a) => { if (a) setLabel(a) })
                    .finally(() => setAddrBusy(false))
                }
              } else {
                setLabel('')
                if (nearest && atNearest) setPicked(nearest.id)
              }
            }}
            className="mt-3 w-full py-2 text-center text-xs font-bold text-violet-600"
          >
            {showOther ? '← Filiallara qayıt' : 'Filialda deyiləm — başqa ünvan →'}
          </button>
        )}

        {err && <div className="mt-3 text-sm font-semibold text-red-600">{err}</div>}

        <button
          disabled={busy || !ready}
          onClick={() => void submit()}
          className="mt-4 flex w-full items-center justify-center gap-2 rounded-2xl bg-violet-600 py-4 font-bold text-white transition active:scale-[.99] disabled:opacity-50"
        >
          {busy ? <Spinner label="Qeyd olunur…" /> : <><IconMapPin className="h-5 w-5" /><span className="truncate">{confirmText}</span></>}
        </button>
        <button onClick={onClose} disabled={busy} className="mt-2 w-full py-2.5 text-sm font-semibold text-slate-500 disabled:opacity-60">
          Ləğv et
        </button>
      </div>
    </div>
  )
}
