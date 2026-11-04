import type { ReactNode } from 'react'
import { useBranding } from '../branding/BrandingContext'
import { BrandLogo } from './BrandLogo'
import './AuthShell.css'

/**
 * Qeydiyyat səhifəsinin çərçivəsi: nöqtəli tünd fon, ortada loqo, şirkətin adı və
 * "Davamiyyət sistemi" yazısı, altında isə səhifənin öz kartları (children).
 * Harada: RegisterPage.
 * Props: children — çərçivənin içinə qoyulan hər şey (React bunu avtomatik ötürür).
 * Görünüş AuthShell.css-dədir; kartlar üçün "auth-card" klassını işlədin.
 */
export function AuthShell({ children }: { children: ReactNode }) {
  const branding = useBranding()

  return (
    <div className="auth-page">
      {/* Bəzək qatları — məna daşımır, ona görə ekran oxuyucudan gizlədilir. */}
      <div className="auth-dots" aria-hidden="true" />
      <div className="auth-vignette" aria-hidden="true" />

      <div className="auth-inner">
        <div className="auth-brand">
          <div className="auth-brand-logo">
            <BrandLogo size={52} />
          </div>
          {/* The company's name, when we know it. On app.qrlog.az we do not — the app shell serves every
              company and only learns which one from the phone number typed below — and the fallback used
              to print "Davamiyyət" directly above the "Davamiyyət sistemi" line, saying the same word
              twice under a logo that already says QRLog. With no company to name, the logo and the
              subtitle are the whole heading. */}
          {branding.displayName && <div className="auth-brand-name">{branding.displayName}</div>}
          <div className="auth-brand-sub">Davamiyyət sistemi</div>
        </div>

        {children}
      </div>
    </div>
  )
}
