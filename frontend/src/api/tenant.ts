import { apiRequest } from './client'

export interface TenantBranding {
  displayName: string
  color: string | null
}

/** GET /api/tenant/branding — the current tenant's name/colour (resolved from the subdomain). Public,
 * so the login screen can brand itself before anyone signs in. */
export function getTenantBranding() {
  return apiRequest<TenantBranding>('/api/tenant/branding', { auth: false })
}
