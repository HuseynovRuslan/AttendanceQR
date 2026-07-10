/// <reference types="vite/client" />

/** Build id baked in by vite.config.ts; compared against /version.json to detect a stale bundle. */
declare const __BUILD_ID__: string

interface ImportMetaEnv {
  readonly VITE_API_URL?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
