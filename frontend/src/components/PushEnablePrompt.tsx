import { useEffect, useState } from 'react'
import { enablePush, isSubscribed, pushPermission, pushSupported } from '../lib/push'

/**
 * One-tap "turn the checkout reminder on", placed where the employee actually is — on the check-in
 * result and on the home screen — because a toggle buried in the menu is a toggle nobody finds.
 *
 * Self-hiding: renders nothing when push is unsupported, already on, or permission was refused, so it
 * can be dropped in unconditionally and simply disappears once the job is done.
 */
export function PushEnablePrompt({ dark = false }: { dark?: boolean }) {
  const [show, setShow] = useState(false)
  const [busy, setBusy] = useState(false)
  const [done, setDone] = useState(false)
  const [failed, setFailed] = useState<string | null>(null)

  useEffect(() => {
    if (!pushSupported() || pushPermission() === 'denied') return
    void isSubscribed().then((sub) => setShow(!sub))
  }, [])

  async function turnOn() {
    setBusy(true)
    setFailed(null)
    const r = await enablePush()
    setBusy(false)
    if (r === 'ok') setDone(true)
    else if (r === 'denied') setFailed('İcazə verilmədi')
    else if (r === 'disabled') setShow(false)
    else setFailed('Alınmadı, yenidən yoxlayın')
  }

  if (!show) return null

  if (done) {
    return (
      <div className={`mt-4 rounded-xl px-4 py-3 text-sm font-bold ${dark ? 'bg-black/25' : 'bg-green-50 text-green-800'}`}>
        ✓ Bildiriş açıldı — çıxışı unutmayacaqsınız
      </div>
    )
  }

  const wrap = dark
    ? 'mt-4 rounded-xl bg-black/25 px-4 py-3 text-left'
    : 'rounded-3xl border-2 border-blue-200 bg-blue-50 p-4'
  const btn = dark
    ? 'mt-3 w-full rounded-lg bg-white py-2.5 text-sm font-bold text-slate-900'
    : 'mt-3 w-full rounded-xl bg-blue-600 py-2.5 text-sm font-bold text-white'

  return (
    <div className={wrap}>
      <div className={`text-sm font-bold ${dark ? '' : 'text-blue-900'}`}>🔔 Çıxış xatırlatmasını aç</div>
      <div className={`mt-0.5 text-xs ${dark ? 'opacity-85' : 'text-blue-800'}`}>
        İş vaxtın bitməyinə 10 dəqiqə qalanda telefonuna bildiriş gələcək — çıxışı unutmayasan.
      </div>
      <button onClick={() => void turnOn()} disabled={busy} className={`${btn} disabled:opacity-60`}>
        {busy ? 'Açılır…' : 'Bildirişi aç'}
      </button>
      {failed && <div className={`mt-2 text-xs ${dark ? 'opacity-85' : 'text-blue-800'}`}>{failed}</div>}
    </div>
  )
}
