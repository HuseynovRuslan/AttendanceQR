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
