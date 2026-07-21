import { useEffect, useState } from 'react'
import { isStandalone } from '../lib/device'
import { canInstall, onInstallAvailability, promptInstall } from '../lib/installPrompt'

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
  // Chromium can install in one tap; availability arrives asynchronously, so track it.
  const [installable, setInstallable] = useState(canInstall)
  const [busy, setBusy] = useState(false)

  useEffect(() => onInstallAvailability(setInstallable), [])

  if (hidden) return null

  const ios = /iPhone|iPad|iPod/.test(navigator.userAgent)

  const dismiss = () => {
    localStorage.setItem(DISMISS_KEY, '1')
    setHidden(true)
  }

  async function install() {
    setBusy(true)
    const accepted = await promptInstall()
    setBusy(false)
    // Accepted → the app is installed and this banner has nothing left to say.
    if (accepted) setHidden(true)
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
          <div className="font-bold text-blue-900">Proqramı telefonun ekranına çıxarın</div>
          {/* One tap where the browser allows it (Chromium); iOS exposes no such API, so there the
              only honest option is showing exactly which two taps to make. */}
          {installable ? (
            <>
              <button
                onClick={() => void install()}
                disabled={busy}
                className="mt-3 w-full rounded-xl bg-blue-600 py-3 text-base font-bold text-white disabled:opacity-60"
              >
                {busy ? 'Əlavə olunur…' : 'Ekrana çıxart'}
              </button>
              <p className="mt-2 text-[13px] text-blue-700">
                Telefon soruşanda <b>«Quraşdır»</b> (və ya <b>«Əlavə et»</b>) düyməsinə basın.
              </p>
            </>
          ) : (
            // Where the button physically IS, described so someone who has never heard the word
            // "menu" can still find it. Numbered because they will follow it with the phone in hand.
            <div className="mt-2 space-y-1 text-[13px] leading-relaxed text-blue-700">
              {ios ? (
                <>
                  <p><b>1.</b> Ekranın <b>aşağısında, ortada</b> içindən yuxarı ox çıxan kvadrat şəkil var — ona toxunun.</p>
                  <p><b>2.</b> Açılan siyahını <b>yuxarı sürüşdürün</b>.</p>
                  <p><b>3.</b> <b>«Ana ekrana əlavə et»</b> yazısına toxunun, sonra <b>«Əlavə et»</b>.</p>
                </>
              ) : (
                <>
                  <p><b>1.</b> Ekranın <b>yuxarı sağ küncündə</b> alt-alta üç xırda nöqtə var (⋮) — ona toxunun.</p>
                  <p><b>2.</b> Açılan siyahıdan <b>«Ana ekrana əlavə et»</b> seçin.</p>
                  <p><b>3.</b> Sonra <b>«Əlavə et»</b> düyməsinə basın.</p>
                </>
              )}
            </div>
          )}
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
