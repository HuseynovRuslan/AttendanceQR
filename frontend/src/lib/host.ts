import { Capacitor } from '@capacitor/core'

/* Marketing site */
export const LANDING_URL = 'https://qrlog.az/'
export const LANDING_PRIVACY_URL = 'https://qrlog.az/mexfilik/'

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
 * Səhifə Android tətbiqinin (Capacitor) içində açılıbmı?
 *
 * isAppMode() bunu ayıra bilmir: Android tətbiqi də elə https://app.qrlog.az saytını açır
 * (capacitor.config.ts → server.url), ona görə adi brauzerdə app.qrlog.az açanda da isAppMode() true verir.
 * Capacitor isə tətbiqin içində öz körpüsünü (bridge) səhifəyə qoşur və isNativePlatform() yalnız orada
 * true qaytarır. lib/push.ts də tətbiqi eyni yolla tanıyır.
 *
 * Diqqət: köhnə TWA tətbiqi (az.qrlog.app.twa) Chrome-un içində işləyir, Capacitor körpüsü yoxdur —
 * orada bu funksiya false qaytarır, yəni TWA adi brauzer kimi görünür.
 */
export function isNativeApp(): boolean {
  return Capacitor.isNativePlatform()
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
