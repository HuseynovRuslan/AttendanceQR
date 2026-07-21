import { useEffect, useState } from 'react'
import { disablePush, enablePush, isSubscribed, pushPermission, pushSupported, sendTestPush } from '../lib/push'
import { isStandalone } from '../lib/device'

/**
 * Turns the checkout reminder on for this device. Honest about the platform: on an iPhone the APIs
 * only exist inside the installed PWA, so a Safari tab is told to add the app to the home screen
 * rather than being shown a button that cannot work.
 */
export function PushToggle() {
  const [on, setOn] = useState(false)
  const [busy, setBusy] = useState(false)
  const [msg, setMsg] = useState<string | null>(null)
  const supported = pushSupported()
  const denied = pushPermission() === 'denied'

  useEffect(() => {
    void isSubscribed().then(setOn)
  }, [])

  async function turnOn() {
    setBusy(true)
    setMsg(null)
    const r = await enablePush()
    setBusy(false)
    if (r === 'ok') {
      setOn(true)
      setMsg('Bildirişlər açıldı ✓')
    } else if (r === 'denied') {
      setMsg('Bildirişə icazə verilmədi. Brauzer parametrlərindən icazə verin.')
    } else if (r === 'disabled') {
      setMsg('Bildiriş serverdə hələ aktiv deyil.')
    } else if (r === 'unsupported') {
      setMsg('Bu cihaz bildirişi dəstəkləmir.')
    } else {
      setMsg('Alınmadı, bir azdan yenidən yoxlayın.')
    }
  }

  async function runTest() {
    setBusy(true)
    setMsg(null)
    const reached = await sendTestPush()
    setBusy(false)
    if (reached === null) setMsg('Test göndərilmədi')
    else if (reached === 0) setMsg('Abunə tapılmadı — söndürüb yenidən açın')
    else setMsg('Test göndərildi — bildiriş bir neçə saniyəyə gəlməlidir')
  }

  async function turnOff() {
    setBusy(true)
    await disablePush()
    setBusy(false)
    setOn(false)
    setMsg('Bildirişlər söndürüldü')
  }

  // iPhone in a Safari tab: no PushManager at all until the app is installed.
  if (!supported) {
    return (
      <div className="rounded-3xl border border-slate-100 bg-white p-4 shadow-sm">
        <div className="font-bold">Çıxış xatırlatması</div>
        <div className="mt-1 text-sm text-slate-500">
          {isStandalone()
            ? 'Bu cihaz bildirişi dəstəkləmir.'
            : 'Bildiriş almaq üçün proqramı ana ekrana əlavə edin, sonra buradan aktiv edin.'}
        </div>
      </div>
    )
  }

  return (
    <div className="rounded-3xl border border-slate-100 bg-white p-4 shadow-sm">
      <div className="flex items-center gap-3">
        <div className="min-w-0 flex-1">
          <div className="font-bold">Çıxış xatırlatması</div>
          <div className="mt-0.5 text-sm text-slate-500">
            İş vaxtınız bitəndə çıxışı unutmusunuzsa telefonunuza bildiriş gəlsin.
          </div>
        </div>
        {on ? (
          <button
            onClick={() => void turnOff()}
            disabled={busy}
            className="shrink-0 rounded-xl border border-slate-200 px-4 py-2 text-sm font-semibold text-slate-600 disabled:opacity-50"
          >
            Söndür
          </button>
        ) : (
          <button
            onClick={() => void turnOn()}
            disabled={busy || denied}
            className="shrink-0 rounded-xl bg-blue-600 px-4 py-2 text-sm font-bold text-white disabled:opacity-50"
          >
            {busy ? '…' : 'Aç'}
          </button>
        )}
      </div>
      {on && (
        <div className="mt-2 flex items-center gap-3">
          <span className="text-sm font-semibold text-green-700">Bildirişlər açıqdır ✓</span>
          <button
            onClick={() => void runTest()}
            disabled={busy}
            className="rounded-lg border border-slate-200 px-3 py-1.5 text-xs font-semibold text-slate-600 disabled:opacity-50"
          >
            Test göndər
          </button>
        </div>
      )}
      {denied && !on && (
        <div className="mt-2 text-sm text-amber-700">
          Bildirişə icazə bloklanıb — brauzer parametrlərindən icazə verməlisiniz.
        </div>
      )}
      {msg && <div className="mt-2 text-sm text-slate-500">{msg}</div>}
    </div>
  )
}
