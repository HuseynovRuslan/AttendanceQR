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
    // Skip JS minification: on the low-RAM deploy server the esbuild minify pass thrashed/hung at
    // "rendering chunks". nginx gzips responses, so the transfer-size cost is small. Re-enable the
    // default ('esbuild') once the build runs on a host with more memory.
    minify: false,
  },
})
