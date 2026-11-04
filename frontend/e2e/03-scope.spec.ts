import { test, expect, world, shot } from './support/fixtures'
import { NAMES, PEOPLE, ROLE_TITLES, type Branch, type PersonKey, type RoleKey } from './support/world'

// Structure and branch limits. The E2E world:
//   E2E Struktur = { A1, A2 }, B outside any structure.
//   Operator — Struktur operatoru on E2E Struktur    → A1, A2
//   Tabelçi  — Tabelçi on branch A1                  → A1
//   Müşahidəçi — Müşahidəçi on E2E Struktur          → A1, A2
//   Menecer  — old-rule manager of B                 → B
// and one plain worker per branch to be seen, or not.

const REACH: Record<Exclude<RoleKey, 'admin'>, Branch[]> = {
  operator: ['A1', 'A2'],
  tabelci: ['A1'],
  musahideci: ['A1', 'A2'],
  menecer: ['B'],
}
const WORKER: Record<Branch, PersonKey> = { A1: 'workerA1', A2: 'workerA2', B: 'workerB' }
const ALL: Branch[] = ['A1', 'A2', 'B']

for (const role of Object.keys(REACH) as (keyof typeof REACH)[]) {
  const inside = REACH[role]
  const outside = ALL.filter((b) => !inside.includes(b))

  test.describe(`${ROLE_TITLES[role]} — sahə: ${inside.join(', ')}`, () => {
    test.use({ storageState: world.sessions?.[role] })

    test('server yalnız öz filiallarını verir (API)', async ({ apiAs }) => {
      const api = await apiAs(role)

      // Reach is read off employees.view. (`branches` is every branch reached by ANY right — for the
      // old-rule manager that is the whole company, through the company-wide PIN desk.)
      const me = await api.json<{ permissions: { key: string; company: boolean; locationIds: string[] }[] }>(
        '/api/me/access',
      )
      const view = me.permissions.find((p) => p.key === 'employees.view')
      expect(view?.company, 'employees.view şirkət boyu olmamalıdır').toBe(false)
      const mine = view?.locationIds ?? []
      for (const b of inside) expect(mine, `${b} əlçatan olmalıdır`).toContain(world.branches[b])
      for (const b of outside) expect(mine, `${b} əlçatan olmamalıdır`).not.toContain(world.branches[b])

      const list = await api.json<{ id: string }[]>('/api/manager/employees')
      const ids = list.map((e) => e.id)
      for (const b of inside) expect(ids).toContain(world.people[WORKER[b]].id)
      for (const b of outside) expect(ids).not.toContain(world.people[WORKER[b]].id)

      for (const b of inside) {
        expect((await api.get(`/api/manager/employees/${world.people[WORKER[b]].id}`)).status()).toBe(200)
      }
      for (const b of outside) {
        const r = await api.get(`/api/manager/employees/${world.people[WORKER[b]].id}`)
        // Outside the area a person does not exist, rather than exist-but-forbidden.
        expect(r.status(), `${b} işçisinin kartı`).toBe(404)
      }
    })

    test('işçi siyahısı yalnız öz filiallarını göstərir (UI)', async ({ page }, testInfo) => {
      await page.goto('/admin/employees')
      await page.getByPlaceholder('Ad, telefon nömrəsi və ya işçi ID-si üzrə axtarın').fill('E2E İşçi')
      for (const b of inside) {
        await expect(page.getByRole('link', { name: PEOPLE[WORKER[b]].name, exact: true })).toBeVisible()
      }
      for (const b of outside) {
        await expect(page.getByRole('link', { name: PEOPLE[WORKER[b]].name, exact: true })).toHaveCount(0)
      }
      await shot(page, testInfo, `${role}-isciler`)
    })

    test('başqa filialın işçisinin kartı URL ilə açılmır (UI)', async ({ page }, testInfo) => {
      const b = outside[0]
      await page.goto(`/admin/employees/${world.people[WORKER[b]].id}`)
      await expect(page.getByText('İşçi tapılmadı.')).toBeVisible()
      await shot(page, testInfo, `${role}-kenar-kart`)
    })

    test('«Mənim sahəm» filialları düz sayır (UI)', async ({ page }, testInfo) => {
      await page.goto('/admin/my-area')
      if (role === 'menecer') {
        // The old-rule manager also holds the company-wide PIN desk, so every branch is listed.
        await expect(page.getByText(/^Filiallarım \(\d+\)$/)).toBeVisible()
      } else {
        await expect(page.getByText(`Filiallarım (${inside.length})`)).toBeVisible()
      }
      for (const b of inside) await expect(page.getByText(NAMES[`branch${b}`], { exact: true }).first()).toBeVisible()
      await shot(page, testInfo, `${role}-menim-saham`)
    })
  })
}

test.describe('Sahədən kənara yazı', () => {
  test('Tabelçi başqa filiala işçi əlavə edə bilmir', async ({ apiAs, admin }) => {
    const api = await apiAs('tabelci')
    const r = await api.post('/api/manager/employees', {
      fullName: `${NAMES.created} kənar ${world.runId}`,
      email: `kenar-${world.runId}@e2e.qrlog.invalid`,
      phoneNumber: null,
      fatherName: null,
      position: null,
      locationId: world.branches.B,
    })
    if (r.ok()) await admin.delete(`/api/admin/employees/${(await r.json()).id}?force=true`)
    expect(r.status()).toBe(403)
    expect((await r.json()).error).toBe('LocationNotManaged')
  })

  test('Struktur operatoru strukturdan kənar işçini dəyişə bilmir', async ({ apiAs }) => {
    const api = await apiAs('operator')
    const r = await api.put(`/api/manager/employees/${world.people.workerB.id}`, {
      fullName: PEOPLE.workerB.name,
      email: null,
      phoneNumber: null,
      fatherName: 'dəyişməməlidir',
      position: null,
      locationId: world.branches.B,
    })
    expect(r.status()).toBe(404)
  })

  test('Köhnə Manager öz filialından kənar işçini görmür və dəyişmir', async ({ apiAs }) => {
    const api = await apiAs('menecer')
    expect((await api.get(`/api/manager/employees/${world.people.workerA1.id}`)).status()).toBe(404)
    const r = await api.put(`/api/manager/employees/${world.people.workerA1.id}`, {
      fullName: PEOPLE.workerA1.name,
      email: null,
      phoneNumber: null,
      fatherName: 'dəyişməməlidir',
      position: null,
      locationId: world.branches.A1,
    })
    expect(r.status()).toBe(404)
  })

  test('Sahəli rollar admin endpoint-lərinə buraxılmır', async ({ apiAs }) => {
    for (const role of ['operator', 'tabelci', 'musahideci', 'menecer'] as const) {
      const api = await apiAs(role)
      expect((await api.get('/api/admin/employees')).status(), `${role}: admin işçi siyahısı`).toBe(403)
      expect((await api.get('/api/admin/structures')).status(), `${role}: strukturlar`).toBe(403)
    }
  })
})
