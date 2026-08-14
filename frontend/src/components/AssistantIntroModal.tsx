import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useFeatureEnabled } from '../branding/BrandingContext'

/**
 * The one-time "meet the assistant" dialog on the home screen. A modal rather than a banner on
 * purpose (the user's call): a banner among banners is furniture, a dialog is an event — and this
 * audience taps big obvious buttons, not small novelty pills.
 *
 * Shows ONCE per device, whatever the outcome: "try it" and "later" both stamp the flag, because a
 * novelty announcement that comes back is nagging, and the blue button + menu row remain as the
 * permanent ways in.
 */
const SEEN_KEY = 'qrlog:aiIntroSeen'

export function AssistantIntroModal() {
  const navigate = useNavigate()
  const enabled = useFeatureEnabled('assistant')
  const [open, setOpen] = useState(false)

  useEffect(() => {
    if (!enabled) return
    try {
      if (localStorage.getItem(SEEN_KEY)) return
    } catch {
      return // private mode — skip quietly rather than show it on every open
    }
    // Let the home paint first: a dialog that beats the content it covers feels like an ambush.
    const t = window.setTimeout(() => setOpen(true), 600)
    return () => window.clearTimeout(t)
  }, [enabled])

  function close(thenTry: boolean) {
    try {
      localStorage.setItem(SEEN_KEY, '1')
    } catch {
      /* private mode — it will simply show again; harmless */
    }
    setOpen(false)
    if (thenTry) navigate('/help')
  }

  if (!open) return null

  return (
    <div
      className="fixed inset-0 z-40 flex items-center justify-center bg-slate-900/50 p-6"
      onClick={() => close(false)}
    >
      <div
        role="dialog"
        aria-modal="true"
        aria-label="Yenilik: AI Köməkçi"
        className="w-full max-w-sm rounded-3xl bg-white p-6 shadow-2xl"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex h-14 w-14 items-center justify-center rounded-2xl bg-blue-100 text-3xl">🤖</div>
        <h2 className="mt-4 text-xl font-bold text-slate-900">Yenilik: AI Köməkçi</h2>
        <p className="mt-2 text-sm leading-relaxed text-slate-600">
          Sualınızı yazın və ya 🎤 ilə <b>səsli deyin</b> — azərbaycanca və ya rusca. Köməkçi:
        </p>
        <ul className="mt-3 space-y-2 text-sm text-slate-700">
          <li className="flex gap-2"><span>🔍</span><span>skan alınmayanda <b>səbəbini tapır</b></span></li>
          <li className="flex gap-2"><span>🕐</span><span>bu ay <b>neçə saat işlədiyinizi</b> deyir</span></li>
          <li className="flex gap-2"><span>📱</span><span>telefon dəyişəndə <b>nə edəcəyinizi</b> göstərir</span></li>
          <li className="flex gap-2"><span>⚠️</span><span><b>çıxışı unudulan</b> günlərinizi xatırladır</span></li>
        </ul>
        <button
          onClick={() => close(true)}
          className="mt-5 w-full rounded-2xl bg-blue-600 py-3.5 text-base font-bold text-white transition active:scale-[0.98]"
        >
          İndi sınayın 🎤
        </button>
        <button onClick={() => close(false)} className="mt-2 w-full py-2 text-sm font-semibold text-slate-400">
          Sonra
        </button>
      </div>
    </div>
  )
}
