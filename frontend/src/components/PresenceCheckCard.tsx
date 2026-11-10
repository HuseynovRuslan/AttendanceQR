import { useCallback, useEffect, useRef, useState } from 'react'
import { answerPresenceCheck, getPendingPresenceCheck, type PendingPresenceCheck } from '../api/presenceChecks'
import { getPosition, type GeoFailKind } from '../lib/geo'
import { minutesLeft } from '../lib/presenceCheck'
import { GpsHelp } from './GpsHelp'

type Phase =
  | { kind: 'ask' }
  | { kind: 'locating'; seconds: number | null }
  | { kind: 'sending' }
  | { kind: 'gps'; fail: GeoFailKind }
  | { kind: 'network' }
  | { kind: 'done' }

const POLL_MS = 60_000

/**
 * «Yerinizi təsdiqləyin» — a presence check waiting for this person (see PresenceCheck on the server).
 *
 * Here, on the home page, and not only in the push: an iPhone gets the push only when the app is installed to the
 * home screen, and most are not. So the card asks on every open and every minute while the app is in front of
 * them, and keeps asking after the fifteen minutes are up — a late answer is still taken.
 *
 * Renders nothing when there is no question, and nothing when the server cannot be reached for one: a card that
 * says «xəta» on every home page during an outage would be noise about something that is not the person's to fix.
 */
export function PresenceCheckCard() {
  const [check, setCheck] = useState<PendingPresenceCheck | null>(null)
  const [phase, setPhase] = useState<Phase>({ kind: 'ask' })
  const [, setTick] = useState(0)
  const busy = useRef(false)
  const shownId = useRef<string | null>(null)

  const refresh = useCallback(async () => {
    if (busy.current) return
    try {
      const r = await getPendingPresenceCheck()
      if (r.status !== 200 || !r.data) return
      const next = r.data.check
      // A new question starts over; the same one keeps whatever the person is in the middle of.
      if ((next?.id ?? null) !== shownId.current) {
        shownId.current = next?.id ?? null
        setPhase({ kind: 'ask' })
      }
      setCheck(next)
    } catch {
      // Offline or the server is away: keep what is shown, ask again on the next tick.
    }
  }, [])

  useEffect(() => {
    void refresh()
    const onVisible = () => {
      if (document.visibilityState === 'visible') void refresh()
    }
    document.addEventListener('visibilitychange', onVisible)
    const timer = window.setInterval(() => {
      if (document.visibilityState === 'visible') {
        void refresh()
        setTick((t) => t + 1)   // the minutes left
      }
    }, POLL_MS)
    return () => {
      document.removeEventListener('visibilitychange', onVisible)
      window.clearInterval(timer)
    }
  }, [refresh])

  async function confirm() {
    if (!check || busy.current) return
    busy.current = true
    try {
      setPhase({ kind: 'locating', seconds: null })
      const geo = await getPosition((s) => setPhase({ kind: 'locating', seconds: s }))
      if (!geo.ok) {
        // Told to the server as «GPS alınmadı», never as «outside»; they can try again from here.
        try {
          await answerPresenceCheck(check.id, { failure: geo.kind })
        } catch {
          // The failure is shown either way; the record of it is best-effort.
        }
        setPhase({ kind: 'gps', fail: geo.kind })
        return
      }

      setPhase({ kind: 'sending' })
      const r = await answerPresenceCheck(check.id, {
        latitude: geo.coords.latitude,
        longitude: geo.coords.longitude,
        accuracyMeters: Number.isFinite(geo.coords.accuracy) ? geo.coords.accuracy : null,
      })
      // Answered — or already answered, or the day closed meanwhile: there is nothing left to ask either way.
      if (r.status === 200 || r.status === 409 || r.status === 404) {
        setPhase({ kind: 'done' })
        window.setTimeout(() => {
          shownId.current = null
          setCheck(null)
        }, 4000)
        return
      }
      setPhase({ kind: 'network' })
    } catch {
      setPhase({ kind: 'network' })
    } finally {
      busy.current = false
    }
  }

  if (!check) return null

  if (phase.kind === 'done') {
    return (
      <div className="rounded-3xl border border-emerald-200 bg-emerald-50 p-4">
        <div className="flex items-center gap-3">
          <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-emerald-100 text-xl">✅</span>
          <div className="font-bold text-emerald-800">Təşəkkürlər, yeriniz qeyd olundu.</div>
        </div>
      </div>
    )
  }

  if (phase.kind === 'gps') {
    return (
      <div className="flex flex-col items-center gap-3 rounded-3xl bg-slate-900 p-4">
        <GpsHelp kind={phase.fail} onRetry={() => void confirm()} />
      </div>
    )
  }

  const left = minutesLeft(check.answerByUtc)
  const working = phase.kind === 'locating' || phase.kind === 'sending'

  return (
    <div className="rounded-3xl border-2 border-blue-300 bg-blue-50 p-4 shadow-sm">
      <div className="flex items-start gap-3">
        <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-blue-100 text-xl">📍</span>
        <div className="min-w-0 flex-1">
          <div className="text-lg font-bold text-blue-900">Yerinizi təsdiqləyin</div>
          <p className="mt-0.5 text-sm text-blue-800">
            {left > 0
              ? `Düyməni sıxın. ${left} dəqiqə vaxtınız var.`
              : 'Düyməni sıxın — yeriniz qeyd olunsun.'}
          </p>
          {phase.kind === 'network' && (
            <p className="mt-1 text-sm font-semibold text-red-700">
              Göndərilmədi — internet bağlantısını yoxlayıb yenidən sıxın.
            </p>
          )}
        </div>
      </div>
      <button
        onClick={() => void confirm()}
        disabled={working}
        className="mt-4 w-full rounded-2xl bg-blue-600 px-4 py-4 text-lg font-bold text-white shadow transition active:scale-[0.99] disabled:opacity-70"
      >
        {phase.kind === 'locating'
          ? `Məkan axtarılır…${phase.seconds != null ? ` ${phase.seconds} san` : ''}`
          : phase.kind === 'sending'
            ? 'Göndərilir…'
            : 'Yerimi təsdiqlə'}
      </button>
    </div>
  )
}
