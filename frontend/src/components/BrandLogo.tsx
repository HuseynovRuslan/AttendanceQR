import { useState } from 'react'
import { useBranding } from '../branding/BrandingContext'

/** Sentinel LogoKey that means "QRLog-branded tenant" — render the QRLog wordmark lockup (below)
 * rather than treating it as an image path. Same file is still used for favicon/PWA icon. */
const QRLOG_LOGO = '/brand/qrlog.svg'

/**
 * The tenant's brand mark. In order: QRLog wordmark (every non-bax tenant, promoting the product
 * brand) → the tenant's own uploaded image logo → a neutral initial badge in the tenant accent →
 * the default Bakı Abadlıq leaf (bax, which has no branding).
 */
export function BrandLogo({ size = 34 }: { size?: number }) {
  const { logoUrl, displayName, color } = useBranding()
  const [broken, setBroken] = useState(false)

  // QRLog wordmark: rendered as text in the app font (crisp at any size, consistent across devices)
  // on a light chip so the navy "QR" stays legible on both the dark chrome and the light employee bar.
  if (logoUrl === QRLOG_LOGO) {
    return (
      <span
        aria-label="QRLog"
        style={{
          display: 'inline-flex',
          alignItems: 'center',
          background: '#fff',
          border: '1px solid rgba(15,27,45,0.08)',
          borderRadius: Math.round(size * 0.3),
          padding: `${Math.round(size * 0.16)}px ${Math.round(size * 0.34)}px`,
          fontFamily: 'Sora, sans-serif',
          fontWeight: 800,
          fontSize: Math.round(size * 0.62),
          lineHeight: 1,
          letterSpacing: '-0.02em',
          whiteSpace: 'nowrap',
        }}
      >
        <span style={{ color: '#0F1B2D' }}>QR</span>
        <span style={{ color: '#1E70C8' }}>Log</span>
      </span>
    )
  }

  if (logoUrl && !broken) {
    return (
      <img
        src={logoUrl}
        alt={displayName || 'Logo'}
        width={size}
        height={size}
        onError={() => setBroken(true)}
        style={{ width: size, height: size, borderRadius: '50%', objectFit: 'cover', display: 'block' }}
      />
    )
  }

  // A branded tenant with no logo yet: show its initial in the accent colour instead of another
  // company's leaf.
  if (color) {
    const initial = (displayName || '?').trim().charAt(0).toUpperCase() || '?'
    return (
      <div
        style={{
          width: size,
          height: size,
          borderRadius: '50%',
          background: 'var(--leaf)',
          color: 'var(--on-leaf, #fff)',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          fontFamily: 'Sora, sans-serif',
          fontWeight: 800,
          fontSize: Math.round(size * 0.46),
          lineHeight: 1,
        }}
      >
        {initial}
      </div>
    )
  }

  // Default: Bakı Abadlıq leaf mark.
  return (
    <svg viewBox="0 0 100 120" fill="none" style={{ width: size, height: size }}>
      <path d="M50 4C74 30 92 55 92 76C92 100 73 116 50 116C27 116 8 100 8 76C8 55 26 30 50 4Z" fill="#3D3E3E" />
      <path d="M50 20C68 40 82 58 82 74C82 92 68 104 50 104C32 104 18 92 18 74C18 58 32 40 50 20Z" fill="#F7F6F2" />
      <path d="M50 32C63 47 73 60 73 72C73 85 62 94 50 94C38 94 27 85 27 72C27 60 37 47 50 32Z" fill="#7CB342" />
      <path d="M50 40V88M50 66L38 56M50 70L64 58" stroke="#F7F6F2" strokeWidth="3" strokeLinecap="round" />
    </svg>
  )
}
