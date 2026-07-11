// @ts-check
import { defineConfig } from 'astro/config'
import sitemap from '@astrojs/sitemap'
import tailwindcss from '@tailwindcss/vite'

// site: absolute base for canonical URLs, Open Graph and the generated sitemap.xml.
export default defineConfig({
  site: 'https://qrlog.az',
  integrations: [sitemap()],
  vite: {
    plugins: [tailwindcss()],
  },
})
