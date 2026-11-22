import { useCallback, useEffect, useRef, useState } from 'react'
import { enablePush, isSubscribed, markPushGateShown, pushPermission, pushServerEnabled, pushSupported } from '../lib/push'
import { isStandalone } from '../lib/device'

/**
 * How long the gate may spend finding out whether it has anything to ask. The answer needs the service
 * worker and a request to the server; on a weak signal at the poster either can take as long as it likes,
 * and while it does the scanner sits behind a blank overlay. Past this the gate steps aside unasked.
 */
export const PUSH_GATE_CHECK_MS = 5000

/**
 * The "turn notifications on" step shown before a check-in. Scanning is the only time employees open
 * the app at all, so it is the only place this can realistically be asked — but it is rationed, or it
 * becomes a wall: at most once a day, only before a check-IN (never on the way out), and only for the
 * first few days (see shouldShowPushGate). After that the soft in-card prompt carries on alone.
 *
 * It is never a block, and cannot be (CLAUDE.md: a check-in is never blocked by anything optional, not by
 * notifications). On iPhone in a Safari tab the push APIs do not exist, a browser once refused never
 * prompts again, and a subscription can simply fail on a bad connection — so the ask leads with turning
 * them on, but «Sonra» is always beside it, and stays pressable while an attempt is still running. It
 * used to offer only «Aktiv et və davam et»: when that failed it said «Yenidən cəhd edin» and nothing
 * else, and the one way to record the day was the button that had just failed.
 */
export function PushGate({ onDone }: { onDone: () => void }) {
  const [state, setState] = useState<'checking' | 'ask' | 'install' | 'blocked'>('checking')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  // Held in a ref so the check below runs once per gate: ScanPage hands a fresh arrow on every render,
  // and a check keyed on it started over — time-box and all — each time the page re-rendered.
  const onDoneRef = useRef(onDone)
  onDoneRef.current = onDone
  // Once the employee is on their way to the scanner, nothing that finishes late may call them back.
  const finished = useRef(false)
  const finish = useCallback(() => {
    if (finished.current) return
    finished.current = true
    onDoneRef.current()
  }, [])

  useEffect(() => {
    let settled = false
    const settle = () => {
      if (settled) return false
      settled = true
      clearTimeout(timer)
      return true
    }
    // Not knowing in time is an answer too: the scan, not a blank screen.
    const timer = setTimeout(() => {
      if (settle()) finish()
    }, PUSH_GATE_CHECK_MS)
    void (async () => {
      try {
        // Already on — nothing to ask, and it must not cost them a tap.
        if (pushSupported() && pushPermission() !== 'denied' && (await isSubscribed())) {
          if (settle()) finish()
          return
        }
        // A server that sends no push has nothing to ask for — not even once a day.
        if (pushSupported() && !(await pushServerEnabled())) {
          if (settle()) finish()
          return
        }
      } catch {
        // A check that throws has said nothing — go to the scan.
        if (settle()) finish()
        return
      }
      if (!settle()) return
      // From here the gate is genuinely shown, so it counts against today's one allowance.
      markPushGateShown()
      if (!pushSupported()) {
        // No push in this context at all. On iOS that means "not installed to the home screen yet".
        setState(isStandalone() ? 'blocked' : 'install')
      } else if (pushPermission() === 'denied') {
        setState('blocked')
      } else {
        setState('ask')
      }
    })()
    return () => {
      settle()
    }
  }, [finish])

  async function turnOn() {
    setBusy(true)
    setError(null)
    // enablePush asks the server for its key before anything else, outside its own try — a dropped
    // connection rejects instead of answering 'failed', and the button would have stayed «Aktivləşdirilir…».
    const r = await enablePush().catch(() => 'failed' as const)
    // They pressed «Sonra» while this was running and are at the scanner already.
    if (finished.current) return
    setBusy(false)
    if (r === 'ok') finish()
    else if (r === 'denied') setState('blocked')
    else if (r === 'disabled' || r === 'unsupported') finish()
    else setError('Alınmadı. Yenidən cəhd edin.')
  }

  if (state === 'checking') return <div className="min-h-[40vh]" aria-busy="true" />

  return (
    <div className="w-full max-w-sm rounded-2xl bg-slate-800 p-6 text-center text-white shadow-lg">
      <div className="text-5xl">🔔</div>

      {state === 'ask' && (
        <>
          <h2 className="mt-3 text-xl font-bold text-white">Bildirişləri aktiv edin</h2>
          <p className="mt-2 text-sm text-slate-300">
            Şirkət elanları, növbənin başlaması və bitməsi barədə xatırlatmalar telefonunuza bu yolla
            çatdırılır. Çıxış qeyd edilmədikdə həmin gün <b className="text-white">0 saat</b> hesablanır.
          </p>
          <button
            onClick={() => void turnOn()}
            disabled={busy}
            className="mt-5 w-full rounded-xl bg-white py-3.5 text-base font-extrabold text-slate-900 disabled:opacity-60"
          >
            {busy ? 'Aktivləşdirilir…' : 'Aktiv et və davam et'}
          </button>
          {error && <div className="mt-3 text-sm text-red-300">{error}</div>}
          {/* Quieter than the ask, never disabled: the way to the scanner must not depend on the very
              thing that may be failing. */}
          <button
            onClick={finish}
            className="mt-2 w-full rounded-xl py-3 text-sm font-semibold text-slate-300 hover:text-white"
          >
            Sonra
          </button>
        </>
      )}

      {state === 'install' && (
        <>
          <h2 className="mt-3 text-xl font-bold text-white">Proqramı ana ekrana əlavə edin</h2>
          <p className="mt-2 text-sm text-slate-300">
            Brauzer səhifəsində bildiriş dəstəklənmir. Aşağıdakı <b>Paylaş</b> düyməsi →
            <b> «Ana ekrana əlavə et»</b> seçin və proqramı oradan açın — bildirişlər işləyəcək.
          </p>
          <button onClick={finish} className="mt-5 w-full rounded-xl bg-slate-700 py-3 font-semibold">
            Davam et
          </button>
        </>
      )}

      {state === 'blocked' && (
        <>
          <h2 className="mt-3 text-xl font-bold text-white">Bildirişlər bağlıdır</h2>
          <p className="mt-2 text-sm text-slate-300">
            Bildirişlərə icazə bloklanıb. Elanları və xatırlatmaları almaq üçün brauzer
            parametrlərindən bu sayta bildiriş icazəsi verin.
          </p>
          <button onClick={finish} className="mt-5 w-full rounded-xl bg-slate-700 py-3 font-semibold">
            Davam et
          </button>
        </>
      )}
    </div>
  )
}
