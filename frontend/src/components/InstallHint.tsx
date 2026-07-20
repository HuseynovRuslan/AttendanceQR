import { useState } from 'react'
import { isStandalone } from '../lib/device'

const DISMISS_KEY = 'attendanceqr.installHintDismissed'

/**
 * Nudge employees to add the app to their home screen. This is not cosmetic: an installed PWA gets
 * DURABLE storage, so the device fingerprint survives — whereas a Safari/Chrome tab loses it (iOS
 * evicts tab storage after ~7 days), which is what makes an employee read as a "new device" and
 * triggers the "Cihaz uyğun deyil" churn. Shown only when NOT already installed, and dismissible.
 */
export function InstallHint() {
  const [hidden, setHidden] = useState(
    () => isStandalone() || localStorage.getItem(DISMISS_KEY) === '1',
  )
  if (hidden) return null

  const ios = /iPhone|iPad|iPod/.test(navigator.userAgent)

  const dismiss = () => {
    localStorage.setItem(DISMISS_KEY, '1')
    setHidden(true)
  }

  return (
    <div className="rounded-3xl border border-blue-100 bg-blue-50 p-4 text-sm">
      <div className="flex items-start gap-3">
        <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-blue-600 text-white">
          <svg className="h-5 w-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <rect x="5" y="2" width="14" height="20" rx="3" /><path d="M12 18h.01" />
          </svg>
        </div>
        <div className="min-w-0 flex-1">
          <div className="font-bold text-blue-900">Tətbiqi ana ekrana əlavə edin</div>
          <p className="mt-1 leading-relaxed text-blue-800">
            Belə etməsəniz, telefon vaxtaşırı sizi «yeni cihaz» kimi tanıya bilər və yenidən icazə
            lazım olar. Ana ekrandan açanda bu problem olmur.
          </p>
          <p className="mt-2 text-[13px] text-blue-700">
            {ios ? (
              <>
                <b>Safari</b>-də aşağıdakı <b>Paylaş</b> düyməsi → <b>«Ana ekrana əlavə et»</b>.
              </>
            ) : (
              <>
                Brauzer menyusu (⋮) → <b>«Ana ekrana əlavə et»</b> / <b>«Tətbiqi quraşdır»</b>.
              </>
            )}
          </p>
          <button onClick={dismiss} className="mt-3 text-[13px] font-semibold text-blue-600 underline underline-offset-2">
            Anladım, gizlət
          </button>
        </div>
        <button onClick={dismiss} aria-label="Bağla" className="shrink-0 rounded-lg p-1 text-blue-400 hover:bg-blue-100">
          <svg className="h-4 w-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.4" strokeLinecap="round"><path d="M18 6 6 18M6 6l12 12" /></svg>
        </button>
      </div>
    </div>
  )
}
