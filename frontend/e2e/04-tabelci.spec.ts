import type { Page } from '@playwright/test'
import { test, expect, world, shot, tokenOf } from './support/fixtures'
import { Direct } from './support/api'
import { updateBody } from './support/provision'
import { NAMES } from './support/world'

// The Tabelçi's own work: adding a person to their branch, editing them, moving them to another
// shift — through the screen and through the API the screen calls. Everyone created here is named
// "E2E Yeni …" and deleted by the admin afterwards (setup also sweeps any a crashed run left).

const created: string[] = []
test.afterAll(async () => {
  const admin = new Direct(tokenOf('admin'))
  for (const id of created) await admin.delete(`/api/admin/employees/${id}?force=true`)
})

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

  // Regression (staging E2E, 2026-10-17, fixed in fix/e2e-findings): a Tabelçi does not hold
  // «employees.deactivate» (AccessRoleTemplates: timekeeper), and the profile edit let them switch people off.
  test('işdən çıxartma hüququ olmadan işçini deaktiv də, yenidən aktiv də edə bilmir', async ({ apiAs, admin }) => {
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
    expect(e.manageable, 'redaktə edə bilir').toBe(true)
    expect(e.deactivatable, 'kart deaktiv hüququ olmadığını deyir').toBe(false)

    const off = await api.put(`/api/manager/employees/${id}`, managerBody(e, { isActive: false }))
    expect(off.status()).toBe(403)
    expect((await off.json()).error).toBe('DeactivateNotAllowed')
    expect((await api.json(`/api/manager/employees/${id}`)).isActive, 'işçi aktiv qalmalıdır').toBe(true)

    // Back on is the same right: the admin switches the person off, the Tabelçi may not undo it.
    const card = await admin.ok('GET', `/api/admin/employees/${id}`)
    await admin.ok('PUT', `/api/admin/employees/${id}`, updateBody(card, { isActive: false }))
    const offCard = await api.json(`/api/manager/employees/${id}`)
    const on = await api.put(`/api/manager/employees/${id}`, managerBody(offCard, { isActive: true }))
    expect(on.status()).toBe(403)
    expect((await api.json(`/api/manager/employees/${id}`)).isActive, 'işçi deaktiv qalmalıdır').toBe(false)
  })

  // Regression (2026-10-17): the staff form's branches came from /locations («locations.view»), which a
  // Tabelçi does not hold. Their own branches come from /employee-branches now — and only those.
  test('filial seçimləri yalnız öz sahəsindəndir, locations.view verilmir', async ({ apiAs }) => {
    const api = await apiAs('tabelci')
    const branches = await api.json<{ id: string; name: string; canCreate: boolean; canMove: boolean }[]>(
      '/api/manager/employee-branches',
    )
    expect(branches.map((b) => b.id)).toEqual([world.branches.A1])
    expect(branches[0]).toMatchObject({ canCreate: true, canMove: true })
    // The branch list itself stays closed to them.
    expect((await api.get('/api/manager/locations')).status()).toBe(403)
    expect((await api.get('/api/admin/locations')).status()).toBe(403)
  })
})

test.describe('Tabelçi — UI', () => {
  test.use({ storageState: world.sessions?.tabelci })

  test('«İşçi əlavə et» ilə öz filialına işçi əlavə edir', async ({ page, apiAs }, testInfo) => {
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

  test('işçi kartında «Redaktə et» var, «Deaktiv et» yoxdur', async ({ page }, testInfo) => {
    await page.goto(`/admin/employees/${world.people.workerA1.id}`)
    await expect(page.getByRole('button', { name: 'Redaktə et', exact: true })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Deaktiv et', exact: true })).toHaveCount(0)
    await expect(page.getByRole('button', { name: 'Aktiv et', exact: true })).toHaveCount(0)
    await shot(page, testInfo, 'tabelci-isci-karti')
  })
})
