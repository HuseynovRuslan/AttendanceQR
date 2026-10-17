import { test, expect, world, needs, shot } from './support/fixtures'
import { NAMES, PEOPLE } from './support/world'

// The company administrator: the whole company in one list, and the access screen that shows who
// holds what — the other side of every scope test before this one.

const SEARCH = 'Ad, telefon nömrəsi və ya işçi ID-si üzrə axtarın'

test.describe('Admin (Şirkət administratoru)', () => {
  test.use({ storageState: world.sessions?.admin })

  test('işçi siyahısında bütün filialların işçiləri var', async ({ page }, testInfo) => {
    needs('admin')
    await page.goto('/admin/employees')
    await page.getByPlaceholder(SEARCH).fill('E2E')
    for (const key of ['workerA1', 'workerA2', 'workerB'] as const) {
      await expect(page.getByRole('link', { name: PEOPLE[key].name, exact: true })).toBeVisible()
    }
    await shot(page, testInfo, 'admin-isciler')
  })

  test('«Səlahiyyətlər»: E2E rolları və struktur görünür', async ({ page }, testInfo) => {
    needs('admin')
    await page.goto('/admin/access')
    const grants: [string, string][] = [
      [PEOPLE.operator.name, `Struktur: ${NAMES.structure}`],
      [PEOPLE.tabelci.name, `Filial: ${NAMES.branchA1}`],
      [PEOPLE.musahideci.name, `Struktur: ${NAMES.structure}`],
    ]
    for (const [person, area] of grants) {
      // One card per grant: the person's link, the role and area, and «Geri al». Ancestors come
      // first in document order, so the last match is the card itself.
      const card = page
        .locator('div')
        .filter({ has: page.getByRole('link', { name: person, exact: true }) })
        .filter({ has: page.getByRole('button', { name: 'Geri al' }) })
        .last()
      await expect(card).toContainText(area)
    }
    await shot(page, testInfo, 'admin-teyinatlar')
    await page.getByRole('tab', { name: 'Strukturlar' }).click()
    await expect(page.getByText(NAMES.structure, { exact: true }).first()).toBeVisible()
    await shot(page, testInfo, 'admin-strukturlar')
  })

  test('axtarış böyük «İ» ilə başlayan adı tapır', async ({ page }, testInfo) => {
    needs('admin')
    test.fail(
      true,
      'MƏLUM QÜSUR: admin işçi axtarışı (GET /api/admin/employees?search=) böyük «İ» olan adı tapmır — ' +
        '«E2E İşçi A1», «İsmayılov» 0 nəticə verir. Axtarış sətri C#-da, ad isə PostgreSQL-də lower() olunur.',
    )
    await page.goto('/admin/employees')
    await page.getByPlaceholder(SEARCH).fill(PEOPLE.workerA1.name)
    await expect(page.getByRole('link', { name: PEOPLE.workerA1.name, exact: true })).toBeVisible()
    await shot(page, testInfo, 'admin-axtaris-I')
  })
})
