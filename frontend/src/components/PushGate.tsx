import { useEffect, useState } from 'react'
import { enablePush, isSubscribed, pushPermission, pushSupported } from '../lib/push'
import { isStandalone } from '../lib/device'

/**
 * The mandatory "turn notifications on" step, shown when the employee opens the scanner. Scanning is
 * the only time they open the app at all, so it is the only place this can realistically be asked.
 *
 * It has no "later" button — but it is NOT a hard wall, and cannot be: on iPhone in a Safari tab the
 * push APIs do not exist, and a browser that was once refused will never prompt again. Gating the
 * scan on something those people physically cannot do would stop them recording attendance at all,
 * which is far worse than a missed notification. So the gate stands firm where it can work, and steps
 * aside (with an explanation) where it cannot.
 */
// Employees who CAN enable see this every scan until they do. Those who cannot — iOS Safari tab, or a
// browser that already refused — have nothing to act on right now, so showing them a wall on every
// single check-in is pure friction: they see it once a day instead.
const SEEN_KEY = 'attendanceqr.pushGateSeen'
const SEEN_FOR_MS = 24 * 60 * 60 * 1000

function seenRecently(): boolean {
  const at = Number(localStorage.getItem(SEEN_KEY) ?? 0)
  return at > 0 && Date.now() - at < SEEN_FOR_MS
}

export function PushGate({ onDone }: { onDone: () => void }) {
  const [state, setState] = useState<'checking' | 'ask' | 'install' | 'blocked'>('checking')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    void (async () => {
      // Nothing the employee can do about it right now — show at most once a day.
      const cannotAct = !pushSupported() || pushPermission() === 'denied'
      if (cannotAct && seenRecently()) {
        onDone()
        return
      }
      if (!pushSupported()) {
        // No push in this context at all. On iOS that means "not installed to the home screen yet".
        if (!cancelled) setState(isStandalone() ? 'blocked' : 'install')
        return
      }
      if (pushPermission() === 'denied') {
        if (!cancelled) setState('blocked')
        return
      }
      const already = await isSubscribed()
      if (cancelled) return
      if (already) onDone()
      else setState('ask')
    })()
    return () => {
      cancelled = true
    }
  }, [onDone])

  function continueAnyway() {
    localStorage.setItem(SEEN_KEY, String(Date.now()))
    onDone()
  }

  async function turnOn() {
    setBusy(true)
    setError(null)
    const r = await enablePush()
    setBusy(false)
    if (r === 'ok') onDone()
    else if (r === 'denied') setState('blocked')
    else if (r === 'disabled' || r === 'unsupported') onDone()
    else setError('Alınmadı. Yenidən cəhd edin.')
  }

  if (state === 'checking') return <div className="min-h-[40vh]" aria-busy="true" />

  return (
    <div className="w-full max-w-sm rounded-2xl bg-slate-800 p-6 text-center text-white shadow-lg">
      <div className="text-5xl">🔔</div>

      {state === 'ask' && (
        <>
          <h2 className="mt-3 text-xl font-bold">Bildirişi aç</h2>
          <p className="mt-2 text-sm text-slate-300">
            Davam etmək üçün bildirişə icazə verin. Elanlar, giriş və çıxış xatırlatmaları bu bildirişlə
            gəlir — çıxışı unutsanız o gün <b className="text-white">0 saat</b> sayılır.
          </p>
          <button
            onClick={() => void turnOn()}
            disabled={busy}
            className="mt-5 w-full rounded-xl bg-white py-3.5 text-base font-extrabold text-slate-900 disabled:opacity-60"
          >
            {busy ? 'Açılır…' : 'Bildirişi aç və davam et'}
          </button>
          {error && <div className="mt-3 text-sm text-red-300">{error}</div>}
        </>
      )}

      {state === 'install' && (
        <>
          <h2 className="mt-3 text-xl font-bold">Proqramı ana ekrana əlavə edin</h2>
          <p className="mt-2 text-sm text-slate-300">
            Bu səhifədə bildiriş işləmir. Aşağıdakı <b>Paylaş</b> düyməsi → <b>«Ana ekrana əlavə et»</b>,
            sonra proqramı oradan açın — bildirişlər işləyəcək.
          </p>
          <button onClick={continueAnyway} className="mt-5 w-full rounded-xl bg-slate-700 py-3 font-semibold">
            Davam et
          </button>
        </>
      )}

      {state === 'blocked' && (
        <>
          <h2 className="mt-3 text-xl font-bold">Bildiriş bağlıdır</h2>
          <p className="mt-2 text-sm text-slate-300">
            Bildirişə icazə bloklanıb. Brauzer parametrlərindən bu sayta bildiriş icazəsi verin —
            elanları və xatırlatmaları ala biləsiniz.
          </p>
          <button onClick={continueAnyway} className="mt-5 w-full rounded-xl bg-slate-700 py-3 font-semibold">
            Davam et
          </button>
        </>
      )}
    </div>
  )
}
