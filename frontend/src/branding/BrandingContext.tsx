import { createContext, useContext, useEffect, useLayoutEffect, useState, type ReactNode } from 'react'
import { getTenantBranding, type TenantBranding } from '../api/tenant'
import { applyAccent } from './accent'

const BrandingContext = createContext<TenantBranding>({ displayName: '', color: null, logoUrl: null })

// Hosts that aren't a tenant subdomain (kept in sync with the backend's TenantSlug rules).
const NON_TENANT = new Set(['bax', 'api', 'www', 'localhost', 'qrlog', '127'])
const QRLOG_LOGO = '/brand/qrlog.svg'

/**
 * First-paint guess from the subdomain, so a QRLog tenant never flashes the default Bakı Abadlıq mark
 * + green for the moment before the branding API responds. Policy: bax (and non-tenant hosts) keep the
 * built-in default; every other subdomain is QRLog-branded. The API call below then fills in the exact
 * display name (and would correct the rare tenant that isn't QRLog).
 */
function guessBranding(): TenantBranding {
  const label = (typeof location !== 'undefined' ? location.hostname.split('.')[0] : '').toLowerCase()
  if (!label || label === 'bax' || NON_TENANT.has(label)) return { displayName: '', color: null, logoUrl: null }
  return { displayName: '', color: '#1E70C8', logoUrl: QRLOG_LOGO }
}

/** Push branding into the design system + browser tab (accent recolour, favicon, title). */
function applyBranding(b: TenantBranding) {
  if (b.color) applyAccent(b.color)
  if (b.logoUrl) {
    let link = document.querySelector<HTMLLinkElement>("link[rel~='icon']")
    if (!link) {
      link = document.createElement('link')
      link.rel = 'icon'
      document.head.appendChild(link)
    }
    link.href = b.logoUrl
  }
  if (b.displayName) document.title = `${b.displayName} — Davamiyyət`
}

/**
 * Provides the current tenant's branding (by subdomain). Seeds a subdomain-based guess so the first
 * paint is already correct, then fetches the authoritative branding to fill in the display name.
 */
export function BrandingProvider({ children }: { children: ReactNode }) {
  const [branding, setBranding] = useState<TenantBranding>(guessBranding)

  // Apply the guessed accent/favicon before the browser paints — kills the default-branding flash.
  useLayoutEffect(() => {
    applyBranding(guessBranding())
  }, [])

  useEffect(() => {
    void getTenantBranding().then((r) => {
      if (r.status === 200 && r.data && 'displayName' in r.data) {
        setBranding(r.data)
        applyBranding(r.data)
      }
    })
  }, [])

  return <BrandingContext.Provider value={branding}>{children}</BrandingContext.Provider>
}

// eslint-disable-next-line react-refresh/only-export-components
export function useBranding(): TenantBranding {
  return useContext(BrandingContext)
}
