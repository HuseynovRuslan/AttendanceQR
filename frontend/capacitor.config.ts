import type { CapacitorConfig } from '@capacitor/cli'

/**
 * Native Android shell (Capacitor) for the QRLog attendance app. Unlike the TWA, this runs the site in
 * a native WebView, so camera / location / notification permissions are the APP's own native Android
 * permissions (Settings → Apps → QRLog → Permissions) instead of Chrome site settings.
 *
 * server.url points at the live company-neutral host: the app is a thin native wrapper around the same
 * PWA, so a web deploy updates the app instantly (no re-publish), the service worker still gives
 * offline scanning, and login goes through the cross-tenant app-login endpoint.
 */
const config: CapacitorConfig = {
  appId: 'az.qrlog.app',
  appName: 'QRLog',
  webDir: 'dist',
  server: {
    url: 'https://app.qrlog.az',
    cleartext: false,
  },
}

export default config
