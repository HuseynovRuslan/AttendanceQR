// Writes a fresh build id before every `vite build`. The app polls /version.json and reloads itself
// when the id changes — without this an installed iOS PWA never picks up a deploy, because it is
// suspended rather than closed and so never re-fetches index.html. There is no service worker (a
// stale cached scan page would be far worse), so this file is the only update signal we have.
import { mkdirSync, writeFileSync } from 'node:fs'

mkdirSync('public', { recursive: true })
writeFileSync('public/version.json', `${JSON.stringify({ buildId: String(Date.now()) })}\n`)
