import type { Page } from '@playwright/test'
import { test, expect, world, needs, shot, tokenOf } from './support/fixtures'
import { Direct } from './support/api'
import { NAMES } from './support/world'

// The Tabelçi's own work: adding a person to their branch, editing them, moving them to another
// shift — through the screen and through the API the screen calls. Everyone created here is named
// "E2E Yeni …" and deleted by the admin afterwards (setup also sweeps any a crashed run left).

const created: string[] = []
test.afterAll(async () => {
  if (world.blocked || !world.sessions?.admin) return
  const admin = new Direct(tokenOf('admin'))
  for (const id of created) await admin.delete(`/api/admin/employees/${id}?force=true`)
})

// Defects these tests found on staging. Marked expected-to-fail so the suite stays a regression
// signal; the day one is fixed its test "unexpectedly passes" and the marker must come off.
const KNOWN = {
  deactivate:
    'MƏLUM QÜSUR: employees.deactivate heç yerdə yoxlanmır — Tabelçi PUT /api/manager/employees/{id} ' +
    'isActive:false ilə işçini deaktiv edir (gözlənilən: 403).',
  addButton:
    'MƏLUM QÜSUR: Tabelçidə locations.view yoxdur → GET /api/manager/locations 403 → filial siyahısı boş ' +
    'qalır və «İşçi əlavə et» deaktivdir (EmployeesPage: disabled={isManager && locations.length === 0}).',
}

const MAIL = 'e2e.qrlog.invalid'
let seq = 0
const uniq = () => `${world.runId}${++seq}`

/** The manager form's body from the manager's own read — the PUT replaces every field it carries. */
function managerBody(e: any, change: Record<string, unknown>) {
  return {
    fullName: e.fullName,
    email: e.email,
    phoneNumber: e.phoneNumber,
    fatherName: e.fatherName,
    position: e.position,
    locationId: e.locationId,
    birthYear: e.birthYear,
    birthDate: e.birthDate,
    workStart: e.workStart,
    workEnd: e.workEnd,
    photoExempt: e.photoExempt,
    canFieldCheckIn: e.canFieldCheckIn,
    canShareDevice: e.canShareDevice,
    qrlessCheckInOverride: e.qrlessCheckInOverride,
    requireGeofenceOverride: e.requireGeofenceOverride,
    scheduleId: e.scheduleId,
    workCycleDays: e.workCycleDays,
    workCycleOnDays: e.workCycleOnDays,
    workCycleAnchor: e.workCycleAnchor,
    isActive: e.isActive,
    firstName: e.firstName,
    lastName: e.lastName,
    paperEmployer: e.paperEmployer,
    paperSite: e.paperSite,
    ...change,
  }
}

const form = (page: Page) => page.locator('form.card')
const field = (page: Page, label: string) =>
  form(page).locator(`div:has(> label.form-label:text-is("${label}")) > :is(input, select)`).first()

test.describe('Tabelçi — API', () => {
  test('işçi əlavə edir, redaktə edir, növbəsini dəyişir', async ({ apiAs }) => {
    needs('tabelci')
    const api = await apiAs('tabelci')
    const name = `${NAMES.created} API ${uniq()}`

    const add = await api.post('/api/manager/employees', {
      fullName: name,
      email: `yeni-${uniq()}@${MAIL}`,
      phoneNumber: null,
      fatherName: null,
      position: null,
      locationId: world.branches.A1,
      scheduleId: world.shifts.morning,
    })
    expect(add.status(), 'əlavə').toBe(200)
    const { id } = await add.json()
    created.push(id)

    let e = await api.json(`/api/manager/employees/${id}`)
    expect(e).toMatchObject({ fullName: name, locationId: world.branches.A1, scheduleId: world.shifts.morning, isActive: true })
    expect(e.manageable).toBe(true)

    await test.step('redaktə: ata adı', async () => {
      const r = await api.put(`/api/manager/employees/${id}`, managerBody(e, { fatherName: 'Testoğlu' }))
      expect(r.status()).toBe(200)
      e = await api.json(`/api/manager/employees/${id}`)
      expect(e.fatherName).toBe('Testoğlu')
      expect(e.scheduleId, 'redaktə növbəni silməməlidir').toBe(world.shifts.morning)
    })

    await test.step('növbə: Səhər → Axşam', async () => {
      const r = await api.put(`/api/manager/employees/${id}`, managerBody(e, { scheduleId: world.shifts.evening }))
      expect(r.status()).toBe(200)
      e = await api.json(`/api/manager/employees/${id}`)
      expect(e.scheduleId).toBe(world.shifts.evening)
      expect(e.fatherName, 'növbə dəyişikliyi ata adını silməməlidir').toBe('Testoğlu')
    })
  })

  test('işdən çıxartma hüququ olmadan işçini deaktiv edə bilmir', async ({ apiAs }) => {
    // Tabelçi does not hold employees.deactivate (AccessRoleTemplates: timekeeper).
    needs('tabelci')
    test.fail(true, KNOWN.deactivate)
    const api = await apiAs('tabelci')
    const add = await api.post('/api/manager/employees', {
      fullName: `${NAMES.created} deaktiv ${uniq()}`,
      email: `yeni-${uniq()}@${MAIL}`,
      phoneNumber: null,
      fatherName: null,
      position: null,
      locationId: world.branches.A1,
    })
    expect(add.status()).toBe(200)
    const { id } = await add.json()
    created.push(id)
    const e = await api.json(`/api/manager/employees/${id}`)

    const r = await api.put(`/api/manager/employees/${id}`, managerBody(e, { isActive: false }))
    const after = await api.json(`/api/manager/employees/${id}`)
    expect(after.isActive, 'işçi aktiv qalmalıdır').toBe(true)
    expect(r.status()).toBe(403)
  })
})

test.describe('Tabelçi — UI', () => {
  test.use({ storageState: world.sessions?.tabelci })

  test('«İşçi əlavə et» ilə öz filialına işçi əlavə edir', async ({ page, apiAs }, testInfo) => {
    needs('tabelci')
    test.fail(true, KNOWN.addButton)
    await page.goto('/admin/employees')
    await expect(page.getByRole('heading', { name: 'İşçilər', exact: true })).toBeVisible()
    const addButton = page.getByRole('button', { name: 'İşçi əlavə et' })
    await expect(addButton).toBeVisible()
    await shot(page, testInfo, 'tabelci-isciler-sehifesi')
    await expect(addButton, '«İşçi əlavə et» aktiv olmalıdır').toBeEnabled()
    await addButton.click()

    await expect(form(page).getByText('Yeni işçi', { exact: true })).toBeVisible()
    const last = `UI${uniq()}`
    await field(page, 'Ad').fill(NAMES.created)
    await field(page, 'Soyad').fill(last)
    await field(page, 'Email (istəyə bağlı)').fill(`yeni-${last.toLowerCase()}@${MAIL}`)
    await field(page, 'Filial').selectOption({ label: NAMES.branchA1 })
    await field(page, 'Növbə').selectOption({ label: `${NAMES.shiftMorning} · 08:00–17:00` })
    await shot(page, testInfo, 'tabelci-yeni-isci-formu')
    await form(page).getByRole('button', { name: 'Əlavə et və PIN ver' }).click()

    await expect(page.getByText('üçün yeni müvəqqəti PIN')).toBeVisible()
    await shot(page, testInfo, 'tabelci-isci-elave-edildi')

    const api = await apiAs('tabelci')
    const mine = await api.json<{ id: string; fullName: string; locationId: string }[]>('/api/manager/employees')
    const row = mine.find((x) => x.fullName === `${NAMES.created} ${last}`)
    expect(row, 'yeni işçi siyahıda olmalıdır').toBeDefined()
    created.push(row!.id)
    const e = await api.json(`/api/manager/employees/${row!.id}`)
    expect(e).toMatchObject({ locationId: world.branches.A1, scheduleId: world.shifts.morning })
  })

  test('ekrandan redaktə edir və növbəsini dəyişir', async ({ page, apiAs }, testInfo) => {
    needs('tabelci')
    const api = await apiAs('tabelci')
    const name = `${NAMES.created} redaktə ${uniq()}`
    const add = await api.post('/api/manager/employees', {
      fullName: name,
      email: `yeni-${uniq()}@${MAIL}`,
      phoneNumber: null,
      fatherName: null,
      position: null,
      locationId: world.branches.A1,
      scheduleId: world.shifts.morning,
    })
    expect(add.status()).toBe(200)
    const { id } = await add.json()
    created.push(id)

    const openEditor = async () => {
      await page.goto('/admin/employees')
      await page.getByPlaceholder('Ad, telefon nömrəsi və ya işçi ID-si üzrə axtarın').fill(name)
      // The row's link reads "<name> <father's name>" once a father's name is set.
      const link = page.getByRole('link', { name: new RegExp(`^${name}( |$)`) })
      const row = page.locator('tr', { has: link })
      await row.getByRole('button', { name: 'Redaktə', exact: true }).click()
      await expect(form(page).getByText('İşçini redaktə et', { exact: true })).toBeVisible()
    }

    await test.step('ata adını dəyişir', async () => {
      await openEditor()
      await field(page, 'Ata adı').fill('Ekranoğlu')
      await form(page).getByRole('button', { name: 'Yadda saxla' }).click()
      await expect(page.getByText('İşçi yeniləndi')).toBeVisible()
      await shot(page, testInfo, 'tabelci-redakte')
    })

    await test.step('növbəni Səhər → Axşam dəyişir', async () => {
      await openEditor()
      await expect(field(page, 'Növbə')).toHaveValue(world.shifts.morning)
      await field(page, 'Növbə').selectOption({ label: `${NAMES.shiftEvening} · 14:00–23:00` })
      await form(page).getByRole('button', { name: 'Yadda saxla' }).click()
      await expect(page.getByText('İşçi yeniləndi')).toBeVisible()
      await shot(page, testInfo, 'tabelci-novbe-deyisdi')
    })

    const e = await api.json(`/api/manager/employees/${id}`)
    expect(e).toMatchObject({ fatherName: 'Ekranoğlu', scheduleId: world.shifts.evening, locationId: world.branches.A1 })
  })
})
