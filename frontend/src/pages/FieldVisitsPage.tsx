import { useEffect, useRef, useState } from 'react'
import { SubPageHeader } from '../components/SubPageHeader'
import { SkeletonList } from '../components/employeeBits'
import { IconCheck, IconClock, IconLogout, IconMapPin, IconRefresh } from '../components/icons'
import {
  getMyFieldVisits,
  startFieldVisit,
  checkInFieldVisit,
  checkOutFieldVisit,
  type MyFieldVisit,
} from '../api/fieldVisits'
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
  const [confirmId, setConfirmId] = useState<string | null>(null)
  const [now, setNow] = useState(() => Date.now())
  const [canSelfReport, setCanSelfReport] = useState(false)
  const [sheet, setSheet] = useState(false)
  const confirmTimer = useRef<number | undefined>(undefined)

  async function load() {
    const res = await getMyFieldVisits().catch(() => null)
    if (res && res.status === 200 && Array.isArray(res.data)) setVisits(res.data)
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
  useEffect(() => () => window.clearTimeout(confirmTimer.current), [])

  // One tap → GPS → check-in/out, photo-less, never blocks. Wrapped: apiRequest rejects on a network
  // drop / non-JSON body, so a failure flags in-card and finally always clears busy.
  async function act(v: MyFieldVisit) {
    setConfirmId(null)
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

  // Check-out is terminal with no worker undo — a two-tap confirm (button morphs in place, ~4s) guards
  // an accidental tap without a modal. Check-in stays one tap.
  function requestCheckout(v: MyFieldVisit) {
    setConfirmId(v.id)
    window.clearTimeout(confirmTimer.current)
    confirmTimer.current = window.setTimeout(() => setConfirmId(null), 4000)
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
                    confirming={confirmId === v.id}
                    msg={msg[v.id]}
                    onCheckIn={() => void act(v)}
                    onRequestCheckout={() => requestCheckout(v)}
                    onConfirmCheckout={() => void act(v)}
                    onCancelConfirm={() => setConfirmId(null)}
                  />
                ))}
              </div>
            )}

            {done.length > 0 && (
              <div className="flex flex-col gap-3">
                <div className="px-1 text-sm font-bold text-slate-500">Bu gün tamamlandı</div>
                {done.map((v) => (
                  <VisitCard key={v.id} v={v} now={now} busy={false} locked={false} confirming={false} msg={msg[v.id]} />
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
              Əraziyə çatanda «Ərazidəyəm», ayrılanda «Çıxış et» edin. Yeriniz və vaxt qeyd olunur — QR-a ehtiyac yoxdur.
            </p>
          </>
        )}
      </main>

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
  confirming: boolean
  msg?: Msg
  onCheckIn?: () => void
  onRequestCheckout?: () => void
  onConfirmCheckout?: () => void
  onCancelConfirm?: () => void
}

function VisitCard({
  v,
  now,
  busy,
  locked,
  confirming,
  msg,
  onCheckIn,
  onRequestCheckout,
  onConfirmCheckout,
  onCancelConfirm,
}: VisitCardProps) {
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

      {v.status === 'Assigned' && (
        <button
          disabled={locked}
          onClick={onCheckIn}
          className="mt-3 flex w-full items-center justify-center gap-2 rounded-2xl bg-violet-600 py-3 font-bold text-white transition active:scale-[.99] disabled:opacity-60"
        >
          {busy ? <Spinner label="Yer təyin olunur…" /> : <><IconMapPin className="h-5 w-5" /> Ərazidəyəm</>}
        </button>
      )}

      {v.status === 'CheckedIn' &&
        (confirming ? (
          <div className="mt-3 flex gap-2">
            <button
              disabled={locked}
              onClick={onConfirmCheckout}
              className="flex flex-1 items-center justify-center gap-2 rounded-2xl bg-emerald-600 py-3 font-bold text-white transition active:scale-[.99] disabled:opacity-60"
            >
              {busy ? <Spinner label="Yer təyin olunur…" /> : 'Çıxışı təsdiqlə'}
            </button>
            <button onClick={onCancelConfirm} className="rounded-2xl px-4 font-semibold text-slate-500 active:bg-slate-100">
              Ləğv
            </button>
          </div>
        ) : (
          <button
            disabled={locked}
            onClick={onRequestCheckout}
            className="mt-3 flex w-full items-center justify-center gap-2 rounded-2xl bg-emerald-600 py-3 font-bold text-white transition active:scale-[.99] disabled:opacity-60"
          >
            <IconLogout className="h-5 w-5" /> Çıxış et
          </button>
        ))}

      {msg && <div className={`mt-2 text-sm ${msg.kind === 'err' ? 'text-red-600' : 'text-emerald-600'}`}>{msg.text}</div>}
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
function SelfReportSheet({ onClose, onDone }: { onClose: () => void; onDone: () => Promise<void> }) {
  const [label, setLabel] = useState('')
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState<string | null>(null)

  async function submit() {
    setBusy(true)
    setErr(null)
    try {
      const geo = await getPosition()
      if (!geo.ok) {
        setErr(GEO_MSG[geo.kind] ?? 'GPS alınmadı')
        return
      }
      const res = await startFieldVisit({
        latitude: geo.coords.latitude,
        longitude: geo.coords.longitude,
        photoBase64: null,
        targetLabel: label.trim() || null,
      })
      if (res.status === 200) {
        await onDone()
      } else if (res.status === 403) {
        setErr('Sizə səyyar giriş icazəsi verilməyib')
      } else {
        setErr('Alınmadı — yenidən cəhd edin')
      }
    } catch {
      setErr('Alınmadı — yenidən cəhd edin')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="fixed inset-0 z-50">
      <div className="absolute inset-0 bg-slate-900/40 backdrop-blur-sm" onClick={busy ? undefined : onClose} />
      <div className="absolute inset-x-0 bottom-0 rounded-t-3xl bg-white p-5 pb-8 shadow-2xl">
        <div className="mx-auto mb-4 h-1.5 w-10 rounded-full bg-slate-200" />
        <div className="text-lg font-bold">Yeni sahə ziyarəti</div>
        <div className="mt-1 text-sm text-slate-500">
          Olduğunuz yeri qeyd edin — QR lazım deyil. Yeriniz və vaxt yazılacaq.
        </div>
        <label className="mt-4 block text-sm font-semibold text-slate-600">Ünvan / obyekt (istəyə bağlı)</label>
        <input
          autoFocus
          value={label}
          onChange={(e) => setLabel(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter' && !busy) void submit()
          }}
          placeholder="Məs. Nizami küç. 12 / Anbar"
          className="mt-1 w-full rounded-2xl border border-slate-200 px-4 py-3 focus:border-violet-500 focus:outline-none focus:ring-2 focus:ring-violet-100"
        />
        {err && <div className="mt-2 text-sm text-red-600">{err}</div>}
        <button
          disabled={busy}
          onClick={() => void submit()}
          className="mt-4 flex w-full items-center justify-center gap-2 rounded-2xl bg-violet-600 py-3 font-bold text-white transition active:scale-[.99] disabled:opacity-60"
        >
          {busy ? <Spinner label="Yer təyin olunur…" /> : <><IconMapPin className="h-5 w-5" /> Ərazidəyəm</>}
        </button>
        <button onClick={onClose} disabled={busy} className="mt-2 w-full py-2 font-semibold text-slate-500 disabled:opacity-60">
          Ləğv et
        </button>
      </div>
    </div>
  )
}
