import { useEffect, useState } from 'react'
import { canInstall, onInstallAvailability, promptInstall } from '../lib/installPrompt'

/**
 * A permanent home for the one-tap install, in Menyu.
 *
 * The home-screen banner (InstallHint) carries the same button, but it is dismissible — and once
 * someone taps "Anladım, gizlət" it never returns, taking the install offer with it. This card sits
 * where settings live and simply disappears when there is nothing to offer: already installed, or a
 * browser with no install API (iOS).
 */
export function InstallAppCard() {
  const [installable, setInstallable] = useState(canInstall)
  const [busy, setBusy] = useState(false)

  useEffect(() => onInstallAvailability(setInstallable), [])

  if (!installable) return null

  async function install() {
    setBusy(true)
    await promptInstall()
    setBusy(false)
  }

  return (
    <div className="rounded-3xl border border-blue-100 bg-blue-50 p-4 shadow-sm">
      <div className="font-bold text-blue-900">Proqramı telefonun ekranına çıxarın</div>
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
    </div>
  )
}
