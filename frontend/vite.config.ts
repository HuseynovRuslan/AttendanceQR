import { existsSync, readFileSync } from 'node:fs'
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

// Bake the id of THIS build into the bundle. The app compares it against /version.json to know it is
// stale. Reading the id from the server on first load instead would be circular: a stale bundle would
// adopt the new id as its own and never notice it needs replacing. `scripts/version.mjs` writes the
// file just before this config is read (see the `build` script).
const buildId: string = existsSync('public/version.json')
  ? (JSON.parse(readFileSync('public/version.json', 'utf8')).buildId as string)
  : 'dev'

// https://vite.dev/config/
export default defineConfig({
  define: { __BUILD_ID__: JSON.stringify(buildId) },
  plugins: [react(), tailwindcss()],
  server: {
    port: 5173,
    host: true,
  },
  build: {
    // Minification is back on (was disabled when the deploy box was a 4 GB Hetzner with no swap and
    // the esbuild pass thrashed at "rendering chunks"; the current server has 12 GB, so the reason
    // it was turned off is gone).
    minify: 'esbuild',
  },
  test: {
    environment: 'node',
    // Pin the clock's zone. The formatters turn UTC instants into local wall-clock time, so their
    // tests assert Baku times — which would fail on any machine that is not UTC+4 (a CI box in UTC,
    // a laptop abroad). Everyone who uses this app is in Baku; the tests should say so out loud
    // rather than depend on where they happen to run.
    env: { TZ: 'Asia/Baku' },
  },
})
