import { readFileSync } from 'node:fs'
import { test as base, expect, type Browser, type BrowserContext, type Page, type TestInfo } from '@playwright/test'
import { Direct, RoleApi } from './api'
import { API_URL, isProductionHost } from './env'
import { displayToken } from './provision'
import { loadWorld, ROLE_KEYS, type RoleKey, type World } from './world'

export { expect }

export const world: World = loadWorld()

/** A role's real token — for the transport layer and the API helpers, never for a page. */
export function tokenOf(role: RoleKey): string {
  return readFileSync(world.tokens[role], 'utf8')
}

/** What the role's page holds in its storage instead (provision.displayToken). */
export function displayOf(role: RoleKey): string {
  return displayToken(tokenOf(role))
}

// Display token → real token, for every role of this run.
const REAL = new Map(ROLE_KEYS.map((r) => [`Bearer ${displayOf(r)}`, `Bearer ${tokenOf(r)}`]))

// Blurred on every page so no screenshot or screencast frame carries them: a fresh temporary PIN or invite
// link (`.link-box`), and who is signed in (the sidebar prints the login — with a display token there is no
// e-mail to print, but the UI-login spec's session is a real one).
const MASK = `(() => {
  const add = () => {
    const s = document.createElement('style')
    s.textContent = '.link-box,.sidebar-role .role-name{filter:blur(8px)!important}'
    ;(document.head || document.documentElement).appendChild(s)
  }
  if (document.documentElement) add(); else document.addEventListener('DOMContentLoaded', add)
})()`

/**
 * What every browser context of the suite gets before its first page:
 *  - signed-in API requests made on the real token without the browser ever holding it. The page holds a
 *    display token (provision.displayToken); a request carrying one is sent on to the real API by this
 *    process, with the real token, and the server's answer is handed back to the page as it came — status,
 *    headers and body. A trace records a request as the BROWSER made it, so only the display token is in it.
 *    (Swapping the header on the browser's own request does not do this: the trace records the swapped
 *    header — checked, 2026-10-17.) Nothing is answered by the suite itself: every answer is the server's;
 *  - a wall around production: any request to a QRLog host that is not staging is aborted and recorded, and
 *    the test that made it fails (see the fixtures below);
 *  - the mask above.
 */
async function harden(ctx: BrowserContext, blocked: string[]): Promise<void> {
  await ctx.route(
    (url) => isProductionHost(url.hostname),
    async (route) => {
      blocked.push(new URL(route.request().url()).hostname)
      await route.abort('blockedbyclient')
    },
  )
  await ctx.route(`${API_URL}/**`, async (route) => {
    const request = route.request()
    const real = REAL.get(request.headers()['authorization'] ?? '')
    if (!real) return route.fallback()
    const headers: Record<string, string> = { ...request.headers(), authorization: real }
    delete headers.host
    const res = await fetch(request.url(), {
      method: request.method(),
      headers,
      body: request.postDataBuffer() ? new Uint8Array(request.postDataBuffer()!) : undefined,
      redirect: 'manual',
    })
    // The body arrives decoded, so the encoding headers that described it on the wire no longer do.
    const answer: Record<string, string> = {}
    res.headers.forEach((v, k) => {
      if (!['content-encoding', 'content-length', 'transfer-encoding', 'connection'].includes(k)) answer[k] = v
    })
    await route.fulfill({ status: res.status, headers: answer, body: Buffer.from(await res.arrayBuffer()) })
  })
  await ctx.addInitScript(MASK)
}

function noProductionTraffic(blocked: string[]) {
  expect(blocked, 'production QRLog host-una sorğu göndərildi və bloklandı').toEqual([])
}

/** Fails the test up front when the template role it relies on was edited in the company. */
export function templateIntact(template: string): void {
  const edited = world.rolesEdited?.[template]
  expect(edited, `«${template}» şablon rolu şirkətdə dəyişdirilib: ${edited}`).toBeUndefined()
}

async function signedInContext(browser: Browser, role: RoleKey, blocked: string[]): Promise<BrowserContext> {
  const ctx = await browser.newContext({ storageState: world.sessions[role] })
  await harden(ctx, blocked)
  return ctx
}

type Fixtures = {
  /** A page signed in as `role`, in its own context — several roles can be open in one test. */
  pageAs: (role: RoleKey) => Promise<Page>
  /** The API as `role`: each call a report step, nothing traced. */
  apiAs: (role: RoleKey) => Promise<RoleApi>
  /** The company administrator's API, outside the test's steps — setup-like work inside a test. */
  admin: Direct
  /** Hosts a page of this test tried to reach and the production wall stopped. */
  blockedHosts: string[]
}

export const test = base.extend<Fixtures>({
  blockedHosts: async ({}, use) => {
    const blocked: string[] = []
    await use(blocked)
    noProductionTraffic(blocked)
  },
  context: async ({ context, blockedHosts }, use) => {
    await harden(context, blockedHosts)
    await use(context)
  },
  pageAs: async ({ browser, blockedHosts }, use) => {
    const opened: BrowserContext[] = []
    await use(async (role) => {
      const ctx = await signedInContext(browser, role, blockedHosts)
      opened.push(ctx)
      return ctx.newPage()
    })
    for (const c of opened) await c.close()
  },
  apiAs: async ({}, use) => {
    await use(async (role) => new RoleApi(role, tokenOf(role)))
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
