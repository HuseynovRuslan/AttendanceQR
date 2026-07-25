import { useEffect, useState } from 'react'
import { enablePush, isSubscribed, pushPermission, pushSupported } from '../lib/push'
import { isStandalone } from '../lib/device'

/**
 * One-tap "turn the checkout reminder on", placed where the employee actually is — on the check-in
 * result and on the home screen — because a toggle buried in the menu is a toggle nobody finds.
 *
 * Self-hiding: renders nothing when push is unsupported, already on, or permission was refused, so it
 * can be dropped in unconditionally and simply disappears once the job is done.
 */
export function PushEnablePrompt({
  dark = false,
  onShown,
}: {
  dark?: boolean
  /** Tells the parent whether the ask is on screen, so it can demote its own buttons around it. */
  onShown?: (shown: boolean) => void
}) {
  const [show, setShow] = useState(false)
  const [blocked, setBlocked] = useState(false)
  const [busy, setBusy] = useState(false)
  const [done, setDone] = useState(false)
  const [failed, setFailed] = useState<string | null>(null)

  useEffect(() => {
    if (!pushSupported()) return
    // Permission was refused in the browser: the API can no longer re-ask, so the button is useless.
    // Instead of vanishing (a dead end — the employee just stops getting reminders forever), fall
    // through to a recovery card that points at the one place that can still fix it: device settings.
    if (pushPermission() === 'denied') {
      setBlocked(true)
      return
    }
    void isSubscribed().then((sub) => setShow(!sub))
  }, [])

  useEffect(() => {
    onShown?.(show && !done)
  }, [show, done, onShown])

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

  // iPhone in a Safari tab has no push at all until the app is installed. Say so — silently rendering
  // nothing just looks like the feature is missing, and the employee never learns the one step that
  // would fix it.
  if (!pushSupported() && !isStandalone()) {
    return (
      <div className={dark ? 'mt-5 rounded-2xl bg-black/30 p-4 text-left ring-1 ring-white/25' : 'rounded-3xl border-2 border-blue-200 bg-blue-50 p-4'}>
        <div className={`${dark ? 'text-base' : 'text-sm'} font-bold ${dark ? '' : 'text-blue-900'}`}>
          Bildirişlər
        </div>
        <div className={`mt-1 ${dark ? 'text-sm opacity-90' : 'text-xs text-blue-800'}`}>
          Bildirişləri almaq üçün proqramı <b>ana ekrana əlavə edin</b> (Paylaş → «Ana ekrana əlavə et»)
          və oradan açın. Brauzer səhifəsində bildiriş dəstəklənmir.
        </div>
      </div>
    )
  }

  // Blocked in the browser. The scan card stays silent — the morning queue shouldn't carry the same
  // error every single day — but the home screen shows a quiet, actionable way back, per platform,
  // so someone who once tapped "block" still has a route to fix it.
  if (blocked) {
    if (dark) return null
    return (
      <div className="rounded-3xl border border-amber-200 bg-amber-50 p-4">
        <div className="text-sm font-bold text-amber-900">Bildirişlər bağlıdır</div>
        <div className="mt-1 text-xs text-amber-800">
          Növbə xatırlatmaları və elanlar telefonunuza gəlmir. Açmaq üçün cihaz parametrlərindən
          icazə verin:
        </div>
        <ul className="mt-2 space-y-1 text-xs text-amber-800">
          <li>
            <b>iPhone:</b> Ayarlar → Bildirişlər → <b>QRLog</b> → İcazə verin
          </li>
          <li>
            <b>Android:</b> proqram ikonasını basıb saxlayın → <b>(i)</b> → Bildirişlər → Açın
          </li>
        </ul>
      </div>
    )
  }

  if (!show) return null

  if (done) {
    return (
      <div className={`mt-4 rounded-xl px-4 py-3 text-sm font-bold ${dark ? 'bg-black/25' : 'bg-green-50 text-green-800'}`}>
        Bildirişlər aktivdir ✓
      </div>
    )
  }

  // On the scan card this is deliberately the loudest thing on screen: employees only ever open the
  // app to scan, so this is the single moment the reminder can realistically be switched on.
  const wrap = dark
    ? 'mt-5 rounded-2xl bg-black/30 p-4 text-left ring-1 ring-white/25'
    : 'rounded-3xl border-2 border-blue-200 bg-blue-50 p-4'
  const btn = dark
    ? 'mt-3 w-full rounded-xl bg-white py-3.5 text-base font-extrabold text-slate-900 shadow-lg'
    : 'mt-3 w-full rounded-xl bg-blue-600 py-2.5 text-sm font-bold text-white'

  return (
    <div className={wrap}>
      <div className={`${dark ? 'text-base' : 'text-sm'} font-bold ${dark ? '' : 'text-blue-900'}`}>
        Bildirişləri aktiv edin
      </div>
      <div className={`mt-1 ${dark ? 'text-sm opacity-90' : 'text-xs text-blue-800'}`}>
        Şirkət elanları, növbənin başlaması və bitməsi barədə xatırlatmalar telefonunuza göndəriləcək.
        Çıxış qeyd edilmədikdə həmin gün <b>0 saat</b> hesablanır.
      </div>
      <button onClick={() => void turnOn()} disabled={busy} className={`${btn} disabled:opacity-60`}>
        {busy ? 'Aktivləşdirilir…' : 'Bildirişləri aktiv et'}
      </button>
      {failed && <div className={`mt-2 text-xs ${dark ? 'opacity-85' : 'text-blue-800'}`}>{failed}</div>}
    </div>
  )
}
