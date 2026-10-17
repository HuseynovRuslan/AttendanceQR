import { test, expect, world, shot } from './support/fixtures'
import { NAMES, PEOPLE } from './support/world'

// The company administrator: the whole company in one list, and the access screen that shows who
// holds what — the other side of every scope test before this one.

const SEARCH = 'Ad, telefon nömrəsi və ya işçi ID-si üzrə axtarın'

test.describe('Admin (Şirkət administratoru)', () => {
  test.use({ storageState: world.sessions?.admin })

  test('işçi siyahısında bütün filialların işçiləri var', async ({ page }, testInfo) => {
    await page.goto('/admin/employees')
    await page.getByPlaceholder(SEARCH).fill('E2E')
    for (const key of ['workerA1', 'workerA2', 'workerB'] as const) {
      await expect(page.getByRole('link', { name: PEOPLE[key].name, exact: true })).toBeVisible()
    }
    await shot(page, testInfo, 'admin-isciler')
  })

  test('«Səlahiyyətlər»: E2E rolları və struktur görünür', async ({ page }, testInfo) => {
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

  // Regression (staging E2E, 2026-10-17, fixed in fix/e2e-findings): the search lowered the text in .NET
  // and the name in Postgres, which disagree about «İ» — «İsmayılov» found nobody. Every way of typing the
  // dotted and dotless i must find the person; ş/ç stay themselves.
  for (const typed of ['E2E İşçi A1', 'e2e işçi a1', 'E2E İŞÇİ A1', 'E2E IŞÇI A1', 'e2e ışçı a1']) {
    test(`axtarış «${typed}» yazılanda «E2E İşçi A1»-i tapır`, async ({ page }, testInfo) => {
      await page.goto('/admin/employees')
      await page.getByPlaceholder(SEARCH).fill(typed)
      await expect(page.getByRole('link', { name: PEOPLE.workerA1.name, exact: true })).toBeVisible()
      await expect(page.getByRole('link', { name: PEOPLE.workerA2.name, exact: true })).toHaveCount(0)
      if (typed === 'E2E IŞÇI A1') await shot(page, testInfo, 'admin-axtaris-I')
    })
  }

  test('axtarış ş/ç hərflərini başqa hərflə qarışdırmır', async ({ apiAs }) => {
    const api = await apiAs('admin')
    const r = await api.json<{ items: { fullName: string }[] }>(`/api/admin/employees?search=${encodeURIComponent('E2E Isci A1')}`)
    expect(r.items.map((e) => e.fullName)).not.toContain(PEOPLE.workerA1.name)
  })
})
