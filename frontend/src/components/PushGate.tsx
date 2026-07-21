import { useEffect, useState } from 'react'
import { enablePush, isSubscribed, markPushGateShown, pushPermission, pushSupported } from '../lib/push'
import { isStandalone } from '../lib/device'

/**
 * The "turn notifications on" step shown before a check-in. Scanning is the only time employees open
 * the app at all, so it is the only place this can realistically be asked — but it is rationed, or it
 * becomes a wall: at most once a day, only before a check-IN (never on the way out), and only for the
 * first few days (see shouldShowPushGate). After that the soft in-card prompt carries on alone.
 *
 * It offers no "later" on the ask itself — but it is not a hard block, and cannot be: on iPhone in a
 * Safari tab the push APIs do not exist, and a browser that was once refused will never prompt again.
 * Gating the scan on something those people physically cannot do would stop them recording attendance,
 * which matters far more than a notification.
 */
export function PushGate({ onDone }: { onDone: () => void }) {
  const [state, setState] = useState<'checking' | 'ask' | 'install' | 'blocked'>('checking')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    void (async () => {
      // Already on — nothing to ask, and it must not cost them a tap.
      if (pushSupported() && pushPermission() !== 'denied' && (await isSubscribed())) {
        if (!cancelled) onDone()
        return
      }
      if (cancelled) return
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
      cancelled = true
    }
  }, [onDone])

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
          <button onClick={onDone} className="mt-5 w-full rounded-xl bg-slate-700 py-3 font-semibold">
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
          <button onClick={onDone} className="mt-5 w-full rounded-xl bg-slate-700 py-3 font-semibold">
            Davam et
          </button>
        </>
      )}
    </div>
  )
}
