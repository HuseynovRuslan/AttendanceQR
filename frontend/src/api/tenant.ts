import { apiRequest } from './client'

export interface TenantBranding {
  displayName: string
  color: string | null
  /** Public path/URL to the tenant's own logo (null → fall back to the default mark). */
  logoUrl: string | null
  /** Feature keys the tenant's plan has switched OFF (see backend TenantFeatures). Empty = all on. */
  disabledFeatures: string[]
}

/** GET /api/tenant/branding — the current tenant's name/colour (resolved from the subdomain). Public,
 * so the login screen can brand itself before anyone signs in. */
export function getTenantBranding() {
  return apiRequest<TenantBranding>('/api/tenant/branding', { auth: false })
}

/** GET /api/tenant/group-companies — the other companies in this customer's group, the options for
 *  «Sənəd üzrə şirkət» on the employee forms. Authorised staff only: which companies share an owner
 *  is not something the anonymous branding call may hand a stranger. Empty when none are configured,
 *  and the field falls back to a plain text box. */
export function getGroupCompanies() {
  return apiRequest<string[]>('/api/tenant/group-companies')
}
