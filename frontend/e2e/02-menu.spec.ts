import { test, expect, world, needs, shot, sidebar, templateIntact } from './support/fixtures'
import {
  ADMIN_MUST_HAVE,
  ADMIN_ONLY,
  EXPECTED_MENU,
  FORBIDDEN_PAGE,
  ROLE_TEMPLATE,
  TEMPLATE_PERMISSIONS,
} from './support/expectations'
import { ROLE_KEYS, ROLE_TITLES, type RoleKey } from './support/world'

// Each role's panel: exactly the sidebar it should have, a page outside it refused even by URL,
// and the permission set the server reports for the session.

const ROLE_LINE: Record<RoleKey, RegExp> = {
  admin: /Admin|Şirkət administratoru/,
  operator: /Struktur operatoru/,
  tabelci: /Tabelçi/,
  musahideci: /Müşahidəçi/,
  menecer: /Filial meneceri/,
}

const sorted = (a: string[]) => [...a].sort((x, y) => x.localeCompare(y, 'az'))

for (const role of ROLE_KEYS) {
  test.describe(ROLE_TITLES[role], () => {
    test.use({ storageState: world.sessions?.[role] })

    test('menyu rola uyğundur', async ({ page }, testInfo) => {
      needs(role)
      const tpl = ROLE_TEMPLATE[role]
      if (tpl) templateIntact(tpl)

      await page.goto('/admin')
      const labels = await sidebar(page)
      await expect(page.locator('.sidebar-role .role-area')).toHaveText(ROLE_LINE[role])

      if (role === 'admin') {
        expect(labels).toEqual(expect.arrayContaining(ADMIN_MUST_HAVE))
      } else {
        expect(sorted(labels), `${ROLE_TITLES[role]} menyusu`).toEqual(sorted(EXPECTED_MENU[role]))
        for (const label of ADMIN_ONLY) expect(labels).not.toContain(label)
      }
      await shot(page, testInfo, `${role}-menyu`)
    })

    if (role !== 'admin') {
      test('menyuda olmayan səhifə URL ilə də açılmır', async ({ page }, testInfo) => {
        needs(role)
        const { path, label } = FORBIDDEN_PAGE[role]
        await page.goto('/admin')
        expect(await sidebar(page)).not.toContain(label)
        await page.goto(path)
        await expect(page.getByText('Bu səhifəyə icazəniz yoxdur')).toBeVisible()
        await shot(page, testInfo, `${role}-qadagan-sehife`)
      })
    }

    test('server sessiyaya rolun hüquqlarını verir', async ({ apiAs }) => {
      needs(role)
      const api = await apiAs(role)
      const me = await api.json<{
        mode: string
        panel: boolean
        companyAdmin: boolean
        permissions: { key: string; company: boolean }[]
      }>('/api/me/access')
      expect(me.mode).toBe('Enforced')
      expect(me.panel).toBe(true)
      expect(me.companyAdmin).toBe(role === 'admin')
      const keys = me.permissions.map((p) => p.key)
      const tpl = ROLE_TEMPLATE[role]
      if (tpl) {
        expect(sorted(keys)).toEqual(sorted(TEMPLATE_PERMISSIONS[tpl]))
        // A structure or branch role grants nothing company-wide.
        expect(me.permissions.filter((p) => p.company).map((p) => p.key)).toEqual([])
      }
      if (role === 'menecer') {
        // The old manager keeps exactly today's rights: the branch ones on their branches, and the
        // company-wide PIN desk for plain staff — nothing an admin alone holds.
        expect(me.permissions.filter((p) => p.company).map((p) => p.key).sort()).toEqual([
          'employees.change_phone',
          'employees.reset_pin',
        ])
        for (const k of ['roles.manage', 'assignments.manage', 'employees.delete', 'locations.create', 'structures.manage']) {
          expect(keys).not.toContain(k)
        }
      }
    })
  })
}
