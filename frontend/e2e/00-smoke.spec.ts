import { expect, test } from '@playwright/test'

// Smoke test: the site is up and a signed-out visitor lands on the login screen. Read-only — it
// never submits the form.
test('açılış login səhifəsinə aparır', async ({ page }, testInfo) => {
  await page.goto('/')

  await expect(page).toHaveURL(/\/login$/)
  await expect(page.getByText('Sistemə daxil olun')).toBeVisible()
  await expect(page.getByPlaceholder('0XX XXX XX XX')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Daxil ol', exact: true })).toBeVisible()

  // The company name and logo arrive from the branding API after first paint; let them land so the
  // screenshot shows the page people actually see, not a blank logo chip.
  await page.waitForLoadState('networkidle')
  const path = testInfo.outputPath('login.png')
  await page.screenshot({ path, fullPage: true })
  await testInfo.attach('login', { path, contentType: 'image/png' })
})
