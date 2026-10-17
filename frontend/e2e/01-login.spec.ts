import { test, expect, world, needs, shot } from './support/fixtures'
import { credentials, type Credentials } from './support/env'
import { strongPin } from './support/provision'
import { enterSecret, pasteSecret } from './support/secrets'
import { ROLE_KEYS, ROLE_TITLES, pinVar, type RoleKey } from './support/world'

// Real logins through the login screen, one per role. This is the one spec that types PINs, so it
// runs untraced and without automatic screenshots (a trace keeps every argument and input value;
// a failure screenshot would show the number in the phone field). The screenshots it does take are
// of the screen AFTER login.
test.use({ trace: 'off', screenshot: 'off' })

function loginOf(role: RoleKey): Credentials | undefined {
  if (role === 'admin') return credentials('E2E_ADMIN')
  const pin = process.env[pinVar(role)]
  const login = world.logins?.[role]
  return pin && login ? { phone: login, pin } : undefined
}

async function signIn(page: import('@playwright/test').Page, creds: Credentials) {
  await page.goto('/login')
  await expect(page.getByText('Sistemə daxil olun')).toBeVisible()
  await enterSecret(page.getByPlaceholder('0XX XXX XX XX'), creds.phone)
  await pasteSecret(page.getByLabel('PIN 1', { exact: true }), creds.pin)
  await page.getByRole('button', { name: 'Daxil ol', exact: true }).click()
}

// Where each account lands. The token decides the first stop (Admin/Manager → panel, everyone else
// → the employee app); the new roles reach the panel from the employee menu.
const PANEL_DIRECT: Record<RoleKey, boolean> = {
  admin: true,
  menecer: true,
  operator: false,
  tabelci: false,
  musahideci: false,
}

for (const role of ROLE_KEYS) {
  test(`${ROLE_TITLES[role]}: login ekranından real giriş`, async ({ page }, testInfo) => {
    needs(role)
    const creds = loginOf(role)
    test.skip(!creds, `${ROLE_TITLES[role]}: giriş məlumatı yoxdur`)

    await signIn(page, creds!)

    if (PANEL_DIRECT[role]) {
      await expect(page).toHaveURL(/\/admin\/[a-z-]+$/)
      await expect(page.locator('nav.sidebar-nav')).toBeVisible()
    } else {
      // A person whose account is a plain employee lands in the employee app — and, holding a
      // panel role, finds «İdarəetmə paneli» in its menu.
      await expect(page).toHaveURL(/\/(home|scan)$/)
      await page.goto('/menu')
      const door = page.getByText('İdarəetmə paneli', { exact: true })
      await expect(door).toBeVisible()
      await shot(page, testInfo, `${role}-isci-menyusu`)
      await door.click()
      await expect(page).toHaveURL(/\/admin\/[a-z-]+$/)
      await expect(page.locator('nav.sidebar-nav')).toBeVisible()
    }
    await shot(page, testInfo, `${role}-panel`)
  })
}

test('Yanlış PIN rədd olunur, düzgün PIN ilə sonra giriş alınır', async ({ page }, testInfo) => {
  // On an E2E account, never on the admin's: one failure is recorded against the login, and the
  // successful login right after clears it.
  needs('tabelci')
  const creds = loginOf('tabelci')
  test.skip(!creds, 'Tabelçi: giriş məlumatı yoxdur')

  let wrong = strongPin()
  while (wrong === creds!.pin) wrong = strongPin()
  await signIn(page, { phone: creds!.phone, pin: wrong })
  await expect(page.getByText('Nömrə və ya PIN yanlışdır')).toBeVisible()
  await expect(page).toHaveURL(/\/login$/)
  await testInfo.attach('yanlis-pin', {
    body: await page.screenshot({ mask: [page.locator('input')] }),
    contentType: 'image/png',
  })

  await signIn(page, creds!)
  await expect(page).not.toHaveURL(/\/login$/)
})
