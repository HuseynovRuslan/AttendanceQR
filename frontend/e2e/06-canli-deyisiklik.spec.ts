import type { Page } from '@playwright/test'
import { test, expect, world, shot, sidebar, refreshAccess, displayOf } from './support/fixtures'
import { ensureGrant, updateBody } from './support/provision'
import { NAMES, PEOPLE } from './support/world'

// Role and branch changes reach a person who is already signed in — no logout, no new token. The
// panel re-reads /api/me/access when its tab comes back into view; the server reads the role graph
// on every request. Each test puts the person back on their baseline grant in `finally`, and
// teardown does it once more.

const SEARCH = 'Ad, telefon nömrəsi və ya işçi ID-si üzrə axtarın'

async function sameSession(page: Page, token: string) {
  await expect(page).not.toHaveURL(/\/login/)
  // The page still holds the session it started with: nothing logged it out or swapped it.
  expect(await page.evaluate(() => localStorage.getItem('attendanceqr.jwt')), 'eyni sessiya').toBe(token)
}

async function openFromSidebar(page: Page, label: RegExp) {
  await page.locator('nav.sidebar-nav').getByRole('link', { name: label }).click()
}

test('Tabelçiyə filial verilir və geri alınır — yenidən girişsiz', async ({ pageAs, apiAs, admin }, testInfo) => {
  const page = await pageAs('tabelci')
  const api = await apiAs('tabelci')
  const token = displayOf('tabelci')
  const a2Worker = world.people.workerA2.id

  await page.goto('/admin/my-area')
  await expect(page.getByText('Filiallarım (1)')).toBeVisible()
  expect((await api.get(`/api/manager/employees/${a2Worker}`)).status()).toBe(404)

  let grantId: string | undefined
  try {
    await test.step('admin Tabelçiyə «E2E Filial A2» verir', async () => {
      const r = await admin.post('/api/admin/access/assignments', {
        employeeId: world.people.tabelci.id,
        roleId: world.roles.timekeeper,
        scope: 'Branch',
        locationIds: [world.branches.A2],
      })
      expect(r.status).toBe(200)
      grantId = r.body.created[0].id
    })

    await test.step('açıq paneldə yeni filial görünür', async () => {
      await refreshAccess(page)
      await expect(page.getByText('Filiallarım (2)')).toBeVisible()
      await expect(page.getByText(NAMES.branchA2, { exact: true }).first()).toBeVisible()
      await shot(page, testInfo, 'tabelci-filial-elave-olundu')
      await openFromSidebar(page, /^İşçilər/)
      await page.getByPlaceholder(SEARCH).fill(PEOPLE.workerA2.name)
      await expect(page.getByRole('link', { name: PEOPLE.workerA2.name, exact: true })).toBeVisible()
      expect((await api.get(`/api/manager/employees/${a2Worker}`)).status()).toBe(200)
      await sameSession(page, token)
    })

    await test.step('admin filialı geri alır', async () => {
      const r = await admin.post(`/api/admin/access/assignments/${grantId}/revoke`)
      expect(r.status).toBe(200)
      grantId = undefined
    })

    await test.step('filial açıq paneldən də çıxır', async () => {
      await refreshAccess(page)
      await openFromSidebar(page, /^Mənim sahəm/)
      await expect(page.getByText('Filiallarım (1)')).toBeVisible()
      expect((await api.get(`/api/manager/employees/${a2Worker}`)).status()).toBe(404)
      await sameSession(page, token)
      await shot(page, testInfo, 'tabelci-filial-alindi')
    })
  } finally {
    if (grantId) await admin.post(`/api/admin/access/assignments/${grantId}/revoke`)
  }
})

test('Müşahidəçiyə Tabelçi rolu verilir və geri alınır — yenidən girişsiz', async ({ pageAs, apiAs, admin }, testInfo) => {
  const page = await pageAs('musahideci')
  const api = await apiAs('musahideci')
  const token = displayOf('musahideci')
  const addButton = page.getByRole('button', { name: 'İşçi əlavə et' })

  await page.goto('/admin/employees')
  await expect(page.getByRole('heading', { name: 'İşçilər', exact: true })).toBeVisible()
  await expect(addButton).toHaveCount(0)
  expect(await sidebar(page)).not.toContain('PIN sıfırlama')

  let grantId: string | undefined
  try {
    await test.step('admin Müşahidəçiyə A1-də Tabelçi rolu verir', async () => {
      const r = await admin.post('/api/admin/access/assignments', {
        employeeId: world.people.musahideci.id,
        roleId: world.roles.timekeeper,
        scope: 'Branch',
        locationIds: [world.branches.A1],
      })
      expect(r.status).toBe(200)
      grantId = r.body.created[0].id
    })

    await test.step('yazı imkanları açıq paneldə görünür', async () => {
      await refreshAccess(page)
      await expect(addButton).toBeVisible()
      expect(await sidebar(page)).toContain('PIN sıfırlama')
      const me = await api.json<{ permissions: { key: string }[] }>('/api/me/access')
      expect(me.permissions.map((p) => p.key)).toContain('employees.create')
      await sameSession(page, token)
      await shot(page, testInfo, 'musahideci-tabelci-rolu-verildi')
    })

    await test.step('admin rolu geri alır', async () => {
      const r = await admin.post(`/api/admin/access/assignments/${grantId}/revoke`)
      expect(r.status).toBe(200)
      grantId = undefined
    })

    await test.step('yazı imkanları yenidən yoxa çıxır', async () => {
      await refreshAccess(page)
      await expect(addButton).toHaveCount(0)
      expect(await sidebar(page)).not.toContain('PIN sıfırlama')
      const me = await api.json<{ permissions: { key: string }[] }>('/api/me/access')
      expect(me.permissions.map((p) => p.key)).not.toContain('employees.create')
      await sameSession(page, token)
      await shot(page, testInfo, 'musahideci-tabelci-rolu-alindi')
    })
  } finally {
    if (grantId) await admin.post(`/api/admin/access/assignments/${grantId}/revoke`)
  }
})

test('Müşahidəçinin yeganə rolu alınanda panel bağlanır, qaytarılanda açılır', async ({ pageAs, apiAs, admin }, testInfo) => {
  const page = await pageAs('musahideci')
  const api = await apiAs('musahideci')
  const token = displayOf('musahideci')
  const person = world.people.musahideci.id

  await page.goto('/admin/dashboard')
  await expect(page.locator('nav.sidebar-nav')).toBeVisible()
  try {
    await test.step('admin Müşahidəçi rolunu geri alır', async () => {
      const rows = await admin.ok<{ id: string; revokedAtUtc: string | null; system: boolean }[]>(
        'GET',
        `/api/admin/access/assignments?employeeId=${person}`,
      )
      for (const r of rows.filter((x) => !x.revokedAtUtc && !x.system)) {
        expect((await admin.post(`/api/admin/access/assignments/${r.id}/revoke`)).status).toBe(200)
      }
    })

    await test.step('panel bağlanır, API də bağlanır', async () => {
      await refreshAccess(page)
      await expect(page).toHaveURL(/\/scan$/)
      expect((await api.get('/api/manager/employees')).status()).toBe(403)
      const me = await api.json<{ panel: boolean }>('/api/me/access')
      expect(me.panel).toBe(false)
      await sameSession(page, token)
      await shot(page, testInfo, 'musahideci-rol-alindi')
    })
  } finally {
    await ensureGrant(admin, 'musahideci', person, world.roles, world.structureId, world.branches)
  }

  await test.step('rol qaytarılır — eyni sessiyada panel yenidən açılır', async () => {
    await page.goto('/admin')
    await expect(page.locator('nav.sidebar-nav')).toBeVisible()
    expect((await api.get('/api/manager/employees')).status()).toBe(200)
    await sameSession(page, token)
  })
})

test('Köhnə Manager-ə filial əlavə olunur və alınır — yenidən girişsiz', async ({ pageAs, apiAs, admin }, testInfo) => {
  // The old model's grant is the employee card's «Rəhbərlik etdiyi filiallar»; the access model
  // mirrors it as a system assignment on the next write.
  const page = await pageAs('menecer')
  const api = await apiAs('menecer')
  const token = displayOf('menecer')
  const person = world.people.menecer.id
  const a2Worker = world.people.workerA2.id

  await page.goto('/admin/employees')
  await expect(page.getByRole('heading', { name: 'İşçilər', exact: true })).toBeVisible()
  expect((await api.get(`/api/manager/employees/${a2Worker}`)).status()).toBe(404)

  const before = await admin.ok('GET', `/api/admin/employees/${person}`)
  try {
    await test.step('admin menecerə A2 filialını da verir', async () => {
      await admin.ok(
        'PUT',
        `/api/admin/employees/${person}`,
        updateBody(before, { managedLocationIds: [world.branches.B, world.branches.A2] }),
      )
    })

    await test.step('açıq paneldə A2 işçisi görünür', async () => {
      await refreshAccess(page)
      await page.reload()
      await page.getByPlaceholder(SEARCH).fill(PEOPLE.workerA2.name)
      await expect(page.getByRole('link', { name: PEOPLE.workerA2.name, exact: true })).toBeVisible()
      expect((await api.get(`/api/manager/employees/${a2Worker}`)).status()).toBe(200)
      await sameSession(page, token)
      await shot(page, testInfo, 'menecer-filial-elave-olundu')
    })
  } finally {
    await admin.ok('PUT', `/api/admin/employees/${person}`, updateBody(before, {}))
  }

  await test.step('geri alınan filial yox olur', async () => {
    expect((await api.get(`/api/manager/employees/${a2Worker}`)).status()).toBe(404)
    await page.reload()
    await page.getByPlaceholder(SEARCH).fill(PEOPLE.workerA2.name)
    await expect(page.getByRole('link', { name: PEOPLE.workerA2.name, exact: true })).toHaveCount(0)
    await sameSession(page, token)
  })
})
