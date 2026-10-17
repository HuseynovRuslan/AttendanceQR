// @ts-check
import { defineConfig } from 'astro/config'
import sitemap from '@astrojs/sitemap'
import tailwindcss from '@tailwindcss/vite'

// site: the absolute base for canonical URLs, Open Graph and the generated sitemap. It is the real
// production domain — the app itself lives on bax.qrlog.az (see SITE.appUrl in src/data/site.ts).
//
// trailingSlash 'always' matches how Caddy serves the build: /haqqimizda/ resolves to
// /srv/qrlog/haqqimizda/index.html, and Caddy's file_server redirects the slashless form itself.
export default defineConfig({
  site: 'https://qrlog.az',
  trailingSlash: 'always',
  // Yönləndirmə (redirect) — köhnə ünvana gələn adamı yeni ünvana göndərmək. Ayrıca «Qiymət» səhifəsi
  // silindi (qiymətlər ana səhifənin #pricing bölməsindədir), amma /qiymet/ Google-da indekslənib və
  // kiminsə əlfəcinində ola bilər. Statik sayt build olunanda Astro hər ünvan üçün kiçik HTML faylı yaradır
  // (dist/qiymet/index.html): içində `<meta http-equiv="refresh" content="0;url=/#pricing">` var,
  // brauzer onu açan kimi yeni ünvana keçir, `#pricing` hissəsi də itmir.
  redirects: {
    '/qiymet/': '/#pricing',
    '/ru/qiymet/': '/ru/#pricing',
    '/en/qiymet/': '/en/#pricing',
  },
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
  // Tailwind v4. How it sits next to the existing global.css is explained in src/styles/tailwind.css.
  vite: {
    plugins: [tailwindcss()],
  },
})
