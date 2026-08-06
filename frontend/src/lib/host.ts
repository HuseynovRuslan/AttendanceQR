/**
 * The single-URL native app is served from a company-neutral host (app.qrlog.az / the TWA), so it has
 * no subdomain to resolve the tenant from. In that mode the login goes through the cross-tenant
 * app-login endpoint and the branding stays the QRLog product brand. The subdomain web (bax.qrlog.az…)
 * is unaffected.
 */
export function isAppMode(): boolean {
  if (typeof location === 'undefined') return false
  return location.hostname.split('.')[0].toLowerCase() === 'app'
}

/**
 * The platform operator console lives on its own host (admin.qrlog.az), separate from every customer
 * company. It has no tenant subdomain, so — like the app — login goes through a cross-tenant endpoint
 * (operator-login, which additionally requires the super-admin allowlist). On this host the app renders
 * the operator shell, not a company's employee/admin app.
 */
export function isOperatorHost(): boolean {
  if (typeof location === 'undefined') return false
  return location.hostname.split('.')[0].toLowerCase() === 'admin'
}
