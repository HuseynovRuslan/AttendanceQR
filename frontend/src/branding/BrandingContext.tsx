import { createContext, useContext, useEffect, useState, type ReactNode } from 'react'
import { getTenantBranding, type TenantBranding } from '../api/tenant'
import { applyAccent } from './accent'

const BrandingContext = createContext<TenantBranding>({ displayName: '', color: null, logoUrl: null })

/**
 * Fetches the current tenant's branding once (by subdomain) and provides it to the app, so the company
 * name isn't hard-coded. Empty until loaded — consumers fall back to a neutral label.
 */
export function BrandingProvider({ children }: { children: ReactNode }) {
  const [branding, setBranding] = useState<TenantBranding>({ displayName: '', color: null, logoUrl: null })

  useEffect(() => {
    void getTenantBranding().then((r) => {
      if (r.status === 200 && r.data && 'displayName' in r.data) {
        const { displayName, color, logoUrl } = r.data
        setBranding(r.data)
        // Recolour the whole design system to the tenant's accent (no-op for bax, which has none).
        if (color) applyAccent(color)
        // Per-tenant browser tab: title text + favicon (index.html ships a neutral default).
        document.title = displayName ? `${displayName} — Davamiyyət` : 'Davamiyyət'
        if (logoUrl) {
          let link = document.querySelector<HTMLLinkElement>("link[rel~='icon']")
          if (!link) {
            link = document.createElement('link')
            link.rel = 'icon'
            document.head.appendChild(link)
          }
          link.href = logoUrl
        }
      }
    })
  }, [])

  return <BrandingContext.Provider value={branding}>{children}</BrandingContext.Provider>
}

// eslint-disable-next-line react-refresh/only-export-components
export function useBranding(): TenantBranding {
  return useContext(BrandingContext)
}
