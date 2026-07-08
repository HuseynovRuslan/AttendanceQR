import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

// https://vite.dev/config/
export default defineConfig({
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
