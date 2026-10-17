import { readFileSync } from 'node:fs'
import { test as base, expect, type Browser, type BrowserContext, type Page, type TestInfo } from '@playwright/test'
import { Api, Direct } from './api'
import { loadWorld, ROLE_TITLES, type RoleKey, type World } from './world'

export { expect }

export const world: World = loadWorld()

// Two things on screen are credentials: who is signed in (the sidebar shows the login — the admin's
// is a real address) and the box a fresh temporary PIN or invite link is shown in. Both are blurred
// on every page so no screenshot or screencast frame carries them; tests read the DOM, untouched.
const MASK_IDENTITY = `(() => {
  const add = () => {
    const s = document.createElement('style')
    s.textContent = '.sidebar-role .role-name,.link-box{filter:blur(8px)!important}'
    ;(document.head || document.documentElement).appendChild(s)
  }
  if (document.documentElement) add(); else document.addEventListener('DOMContentLoaded', add)
})()`

export function tokenOf(role: RoleKey): string {
  const state = JSON.parse(readFileSync(world.sessions[role], 'utf8'))
  return state.origins[0].localStorage[0].value as string
}

/** Skips the calling test, with the reason, when the suite or this role could not be set up. */
export function needs(...roles: RoleKey[]): void {
  base.skip(!!world.blocked, world.blocked ?? '')
  for (const r of roles) {
    const why = world.roleBlocked?.[r]
    base.skip(!!why, `${ROLE_TITLES[r]}: ${why}`)
  }
  base.skip(world.mode !== 'Enforced', `Şirkət ${world.mode} rejimindədir; bu testlər Enforced üçündür`)
}

/** Fails the test up front when the template role it relies on was edited in the company. */
export function templateIntact(template: string): void {
  const edited = world.rolesEdited?.[template]
  expect(edited, `«${template}» şablon rolu şirkətdə dəyişdirilib: ${edited}`).toBeUndefined()
}

async function signedInContext(browser: Browser, role: RoleKey): Promise<BrowserContext> {
  const ctx = await browser.newContext({ storageState: world.sessions[role] })
  await ctx.addInitScript(MASK_IDENTITY)
  return ctx
}

type Fixtures = {
  /** A page signed in as `role`, in its own context — several roles can be open in one test. */
  pageAs: (role: RoleKey) => Promise<Page>
  /** The API as `role`, traced (the tokens are scrubbed from the trace after the run). */
  apiAs: (role: RoleKey) => Promise<Api>
  /** The company administrator's API, untraced: the one credential no run can rotate. */
  admin: Direct
}

export const test = base.extend<Fixtures>({
  context: async ({ context }, use) => {
    await context.addInitScript(MASK_IDENTITY)
    await use(context)
  },
  pageAs: async ({ browser }, use) => {
    const opened: BrowserContext[] = []
    await use(async (role) => {
      const ctx = await signedInContext(browser, role)
      opened.push(ctx)
      return ctx.newPage()
    })
    for (const c of opened) await c.close()
  },
  apiAs: async ({}, use) => {
    const opened: Api[] = []
    await use(async (role) => {
      const api = await Api.as(tokenOf(role))
      opened.push(api)
      return api
    })
    for (const a of opened) await a.dispose()
  },
  admin: async ({}, use) => {
    await use(new Direct(tokenOf('admin')))
  },
})

/** A full-page screenshot attached to the report under `name`. */
export async function shot(page: Page, testInfo: TestInfo, name: string): Promise<void> {
  await page.waitForLoadState('networkidle').catch(() => {})
  await testInfo.attach(name, { body: await page.screenshot({ fullPage: true }), contentType: 'image/png' })
}

/** The labels of the panel's sidebar links, badges stripped. */
export async function sidebar(page: Page): Promise<string[]> {
  const nav = page.locator('nav.sidebar-nav')
  await expect(nav.locator('a.nav-item').first()).toBeVisible()
  return (await nav.locator('a.nav-item .nav-label').allInnerTexts()).map((s) => s.trim())
}

/** Re-reads /api/me/access the way the panel does when its tab comes back into view. */
export async function refreshAccess(page: Page): Promise<void> {
  const resp = page.waitForResponse((r) => r.url().endsWith('/api/me/access') && r.request().method() === 'GET')
  await page.evaluate(() => document.dispatchEvent(new Event('visibilitychange')))
  await resp
}
