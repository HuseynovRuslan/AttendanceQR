import { defineConfig, devices } from '@playwright/test'
import { WEB_URL } from './e2e/support/env'

// End-to-end tests run against a DEPLOYED site, not a local dev server: staging. The role suite writes
// (E2E branches, people and role grants in the staging `test` company), so e2e/support/env.ts accepts only
// staging's hosts and a local stack — production is refused there, in every API helper and in every browser
// context, with no override.
//
// @playwright/test is pinned exactly: each release expects one browser build, and the cloud
// sessions ship that build pre-installed (/opt/pw-browsers). On a laptop, run
// `npx playwright install chromium` once.
//
// Credentials: E2E_ADMIN_PHONE / E2E_ADMIN_PIN, from the environment only (a secret). See e2e/README.md.
export default defineConfig({
  testDir: './e2e',
  globalSetup: './e2e/global-setup.ts',
  globalTeardown: './e2e/global-teardown.ts',
  // One worker, in file order: the live-change spec moves role grants that the others read.
  fullyParallel: false,
  workers: 1,
  timeout: 60_000,
  expect: { timeout: 10_000 },
  retries: 0,
  reporter: [
    ['list'],
    ['html', { open: 'never' }],
    // Must stay last: it scrubs tokens out of the traces the HTML reporter has just copied.
    ['./e2e/support/scrub-reporter.ts'],
  ],
  use: {
    baseURL: WEB_URL,
    locale: 'az-AZ',
    // Same reason as the vitest config: everyone who uses this app is in Baku.
    timezoneId: 'Asia/Baku',
    trace: 'on',
    screenshot: 'on',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
})
