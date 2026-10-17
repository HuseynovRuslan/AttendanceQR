import { defineConfig, devices } from '@playwright/test'

// End-to-end tests run against a DEPLOYED site, not a local dev server: staging by default. Point
// E2E_BASE_URL elsewhere (e.g. http://localhost:8081 under `docker compose up`) to test that instead.
// Keep it off production: a test that signs in or scans would write into real attendance records.
//
// @playwright/test is pinned exactly: each release expects one browser build, and the cloud
// sessions ship that build pre-installed (/opt/pw-browsers). On a laptop, run
// `npx playwright install chromium` once.
export default defineConfig({
  testDir: './e2e',
  timeout: 30_000,
  expect: { timeout: 10_000 },
  retries: 0,
  reporter: [['list'], ['html', { open: 'never' }]],
  use: {
    baseURL: process.env.E2E_BASE_URL ?? 'https://test.qrlog.az',
    locale: 'az-AZ',
    // Same reason as the vitest config: everyone who uses this app is in Baku.
    timezoneId: 'Asia/Baku',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
})
