// @ts-check
import { defineConfig } from 'astro/config'
import sitemap from '@astrojs/sitemap'

// site: the absolute base for canonical URLs, Open Graph and the generated sitemap. It is the real
// production domain — the app itself lives on bax.qrlog.az (see SITE.appUrl in src/data/site.ts).
//
// trailingSlash 'always' matches how Caddy serves the build: /haqqimizda/ resolves to
// /srv/qrlog/haqqimizda/index.html, and Caddy's file_server redirects the slashless form itself.
export default defineConfig({
  site: 'https://qrlog.az',
  trailingSlash: 'always',
  i18n: {
    defaultLocale: 'az',
    locales: ['az', 'ru', 'en'],
    routing: {
      // AZ stays at "/", the others at "/ru/" and "/en/" — the Azerbaijani URLs that are already
      // indexed must not move.
      prefixDefaultLocale: false,
    },
  },
  integrations: [
    sitemap({
      i18n: {
        defaultLocale: 'az',
        locales: { az: 'az-AZ', ru: 'ru-RU', en: 'en-US' },
      },
    }),
  ],
})
