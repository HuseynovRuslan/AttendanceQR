import { createContext, useContext, useEffect, useState, type ReactNode } from 'react'
import { getTenantBranding, type TenantBranding } from '../api/tenant'

const BrandingContext = createContext<TenantBranding>({ displayName: '', color: null })

/**
 * Fetches the current tenant's branding once (by subdomain) and provides it to the app, so the company
 * name isn't hard-coded. Empty until loaded — consumers fall back to a neutral label.
 */
export function BrandingProvider({ children }: { children: ReactNode }) {
  const [branding, setBranding] = useState<TenantBranding>({ displayName: '', color: null })

  useEffect(() => {
    void getTenantBranding().then((r) => {
      if (r.status === 200 && r.data && 'displayName' in r.data) setBranding(r.data)
    })
  }, [])

  return <BrandingContext.Provider value={branding}>{children}</BrandingContext.Provider>
}

// eslint-disable-next-line react-refresh/only-export-components
export function useBranding(): TenantBranding {
  return useContext(BrandingContext)
}
