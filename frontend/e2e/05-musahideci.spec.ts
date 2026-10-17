import { test, expect, world, shot } from './support/fixtures'
import type { Answer } from './support/api'
import { updateBody } from './support/provision'
import { NAMES, PEOPLE } from './support/world'

// The observer reads and changes nothing — checked where a person would try (the screens) and
// where a script would (the API, which must refuse even what the screen never offers). Every write
// is aimed at the observer's OWN area, so a refusal can only come from the missing right, not from
// the scope rules the previous spec covers.

test.describe('Müşahidəçi — UI-da dəyişiklik imkanı yoxdur', () => {
  test.use({ storageState: world.sessions?.musahideci })

  test('işçilər: əlavə et və redaktə düymələri yoxdur', async ({ page }, testInfo) => {
    await page.goto('/admin/employees')
    await page.getByPlaceholder('Ad, telefon nömrəsi və ya işçi ID-si üzrə axtarın').fill(PEOPLE.workerA1.name)
    const link = page.getByRole('link', { name: PEOPLE.workerA1.name, exact: true })
    await expect(link).toBeVisible()
    const row = page.locator('tr', { has: link })
    await expect(row.getByText('yalnız baxış')).toBeVisible()
    await expect(row.getByRole('button', { name: 'Redaktə', exact: true })).toHaveCount(0)
    await expect(page.getByRole('button', { name: 'İşçi əlavə et' })).toHaveCount(0)
    await shot(page, testInfo, 'musahideci-isciler')
  })

  test('işçi kartı: redaktə, deaktiv, PIN düymələri yoxdur', async ({ page }, testInfo) => {
    await page.goto(`/admin/employees/${world.people.workerA1.id}`)
    await expect(page.getByText(PEOPLE.workerA1.name).first()).toBeVisible()
    await expect(page.getByText(/Bu hesabın məlumatlarını dəyişə bilmirsiniz/)).toBeVisible()
    for (const name of ['Redaktə et', 'Deaktiv et', 'PIN sıfırla', 'Dəvət linki']) {
      await expect(page.getByRole('button', { name, exact: true }), name).toHaveCount(0)
    }
    await shot(page, testInfo, 'musahideci-isci-karti')
  })

  test('növbələr və filiallar: yaratma və redaktə yoxdur', async ({ page }, testInfo) => {
    await page.goto('/admin/schedules')
    await expect(page.getByText(NAMES.shiftMorning).first()).toBeVisible()
    await expect(page.getByRole('button', { name: /Yeni növbə/ })).toHaveCount(0)
    await expect(page.getByRole('button', { name: 'Redaktə', exact: true })).toHaveCount(0)
    await shot(page, testInfo, 'musahideci-novbeler')

    await page.goto('/admin/locations')
    await expect(page.getByText(NAMES.branchA1).first()).toBeVisible()
    await expect(page.getByRole('button', { name: /Filial əlavə et/ })).toHaveCount(0)
    await expect(page.getByRole('button', { name: 'Redaktə', exact: true })).toHaveCount(0)
    await shot(page, testInfo, 'musahideci-filiallar')
  })
})

test.describe('Müşahidəçi — API yazıları rədd edir', () => {
  test('hər yazı 403 alır və heç nə dəyişmir', async ({ apiAs, admin }) => {
    const api = await apiAs('musahideci')
    const target = world.people.workerA1.id
    const before = await admin.ok('GET', `/api/admin/employees/${target}`)
    const observerRole = world.roles.observer
    const tryName = `${NAMES.created} müşahidəçi ${world.runId}`

    const asSeen = await api.json(`/api/manager/employees/${target}`)
    const loc = (await admin.ok<any[]>('GET', '/api/admin/locations')).find((l) => l.id === world.branches.A1)

    const attempts: [string, () => Promise<Answer>][] = [
      ['işçi əlavə etmək', () =>
        api.post('/api/manager/employees', {
          fullName: tryName, email: `musahideci-${world.runId}@e2e.qrlog.invalid`,
          phoneNumber: null, fatherName: null, position: null, locationId: world.branches.A1,
        })],
      ['işçini redaktə etmək', () =>
        api.put(`/api/manager/employees/${target}`, { ...asSeen, fatherName: 'dəyişməməlidir' })],
      ['işçini deaktiv etmək', () => api.put(`/api/manager/employees/${target}`, { ...asSeen, isActive: false })],
      ['PIN sıfırlamaq', () => api.post(`/api/manager/employees/${target}/reset-pin`)],
      ['növbə yaratmaq', () =>
        api.post('/api/manager/schedules', { name: tryName, shiftStart: '09:00', shiftEnd: '18:00' })],
      ['filialı dəyişmək', () => api.put(`/api/admin/locations/${world.branches.A1}`, { ...loc, name: tryName })],
      ['rol vermək', () =>
        api.post('/api/admin/access/assignments', {
          employeeId: target, roleId: observerRole, scope: 'Branch', locationIds: [world.branches.A1],
        })],
      ['işçini silmək', () => api.delete(`/api/admin/employees/${target}`)],
    ]

    const statuses: Record<string, number> = {}
    for (const [label, call] of attempts) {
      statuses[label] = await test.step(label, async () => (await call()).status())
    }

    // Undo anything that should not have happened, before judging — staging stays clean either way.
    const after = await admin.ok('GET', `/api/admin/employees/${target}`)
    if (after.fatherName !== before.fatherName || after.isActive !== before.isActive) {
      await admin.ok('PUT', `/api/admin/employees/${target}`, updateBody(before, {}))
    }
    const strayShift = (await admin.ok<any[]>('GET', '/api/admin/schedules')).find((s) => s.name === tryName)
    if (strayShift) await admin.delete(`/api/admin/schedules/${strayShift.id}`)
    const strayGrants = await admin.ok<any[]>('GET', `/api/admin/access/assignments?employeeId=${target}`)
    for (const g of strayGrants) await admin.post(`/api/admin/access/assignments/${g.id}/revoke`)
    const locAfter = (await admin.ok<any[]>('GET', '/api/admin/locations')).find((l) => l.id === world.branches.A1)
    if (locAfter.name !== loc.name) await admin.ok('PUT', `/api/admin/locations/${loc.id}`, loc)

    for (const [label] of attempts) expect.soft(statuses[label], label).toBe(403)
    expect(after.fatherName, 'işçinin məlumatı dəyişməyib').toBe(before.fatherName)
    expect(after.isActive, 'işçi aktiv qalıb').toBe(before.isActive)
    expect(after.activated, 'aktivləşmə vəziyyəti dəyişməyib').toBe(before.activated)
    expect(strayShift, 'növbə yaranmayıb').toBeUndefined()
    expect(strayGrants, 'rol verilməyib').toEqual([])
    expect(locAfter.name, 'filial adı dəyişməyib').toBe(loc.name)
  })
})
