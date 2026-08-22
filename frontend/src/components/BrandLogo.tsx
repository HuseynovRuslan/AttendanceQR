import { useState } from 'react'
import { useBranding } from '../branding/BrandingContext'

/** Sentinel LogoKey that means "QRLog-branded tenant" — render the QRLog wordmark lockup (below)
 * rather than treating it as an image path. Same file is still used for favicon/PWA icon. */
const QRLOG_LOGO = '/brand/qrlog.svg'

/**
 * The tenant's brand mark. In order: QRLog wordmark (the sentinel LogoKey, promoting the product
 * brand) → the tenant's own uploaded image logo → a neutral initial badge in the tenant accent →
 * the QRLog wordmark again when a tenant has nothing at all.
 */
export function BrandLogo({ size = 34 }: { size?: number }) {
  const { logoUrl, displayName, color } = useBranding()
  const [broken, setBroken] = useState(false)

  // QRLog official wordmark, set on a clean white chip. The wordmark is a wide navy/blue lockup on a
  // light ground, so bare it looked like a floating white box on the dark login/sidebar chrome and it
  // overran the square logo slot. The chip gives it a balanced, intentional shape that reads on both the
  // dark chrome and the light employee bar.
  if (logoUrl === QRLOG_LOGO) {
    return <QrlogChip size={size} />
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
          fontFamily: 'Manrope, sans-serif',
          fontWeight: 800,
          fontSize: Math.round(size * 0.46),
          lineHeight: 1,
        }}
      >
        {initial}
      </div>
    )
  }

  // Default: the PRODUCT mark. It used to be a leaf drawn for Bakı Abadlıq — one customer's mark
  // standing in as everybody's default, which is how a company ended up wearing another company's
  // identity (see the Green Garden correction, 2026-08-22). A tenant with no branding of its own now
  // shows QRLog, which is true for every one of them.
  return <QrlogChip size={size} />
}

/** The QRLog wordmark on a white chip. Bare, the wide navy lockup read as a floating white box on the
 *  dark chrome and overran the square logo slot; the chip gives it a shape that works on both. */
function QrlogChip({ size }: { size: number }) {
  return (
    <span
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        background: '#fff',
        borderRadius: Math.round(size * 0.26),
        padding: `${Math.round(size * 0.16)}px ${Math.round(size * 0.28)}px`,
        border: '1px solid rgba(15,27,45,0.08)',
        boxShadow: '0 1px 3px rgba(15,27,45,0.12)',
      }}
    >
      <img
        src="/brand/qrlog-logo.png"
        alt="QRLog"
        style={{ height: Math.round(size * 0.56), width: 'auto', display: 'block' }}
      />
    </span>
  )
}
