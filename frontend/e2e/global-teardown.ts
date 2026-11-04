import { existsSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { Direct } from './support/api'
import { ensureGrant, purgeCreated } from './support/provision'
import { AUTH_DIR, NAMES, PEOPLE, ROLE_KEYS, WORLD_FILE, type PersonKey, type World } from './support/world'

// Closes what the run opened, then CHECKS that it is closed — a cleanup nobody verifies is a hope:
//  1. employees the tests added are deleted (setup also sweeps any a crashed run left);
//  2. every E2E person is back on exactly their baseline role, whatever a live-change test did;
//  3. every E2E person who signed in gets their PIN reset by the admin and the temporary PIN is thrown
//     away — which retires every session this run opened, and leaves nobody holding a working E2E
//     credential until the next run sets a new one;
//  4. the session files are deleted.
// Each is then verified against the server; any miss fails the run, with what was found.
// The E2E branches, structure, shifts and people stay, so the next run does not create them again.

type Check = { check: string; ok: boolean; detail?: string }

export default async function globalTeardown(): Promise<void> {
  if (!existsSync(WORLD_FILE)) {
    rmSync(AUTH_DIR, { recursive: true, force: true })
    return
  }
  const world = JSON.parse(readFileSync(WORLD_FILE, 'utf8')) as World
  const tokens = Object.fromEntries(
    ROLE_KEYS.filter((r) => world.tokens?.[r] && existsSync(world.tokens[r])).map((r) => [r, readFileSync(world.tokens[r], 'utf8')]),
  )
  const checks: Check[] = []
  try {
    if (!tokens.admin) throw new Error('admin sessiyası yoxdur — təmizləmə edilə bilmədi')
    const admin = new Direct(tokens.admin)

    // 1–3: close.
    await purgeCreated(admin)
    for (const key of Object.keys(PEOPLE) as PersonKey[]) {
      const person = world.people?.[key]
      if (!person) continue
      await ensureGrant(admin, key, person.id, world.roles, world.structureId, world.branches)
      if (PEOPLE[key].signsIn) await admin.ok('POST', `/api/admin/employees/${person.id}/reset-pin`)
    }

    // 1: no employee a test added is left, active or switched off.
    const left: string[] = []
    for (const showLeft of [false, true]) {
      const r = await admin.ok<{ items: { fullName: string }[] }>(
        'GET',
        `/api/admin/employees?pageSize=200&showLeft=${showLeft}&search=${encodeURIComponent('E2E')}`,
      )
      left.push(...r.items.filter((e) => e.fullName.startsWith(NAMES.created)).map((e) => e.fullName))
    }
    checks.push({ check: 'testlərin əlavə etdiyi işçilər silinib', ok: left.length === 0, detail: left.join(', ') || undefined })

    // 2: every E2E person on exactly their baseline grant.
    for (const key of Object.keys(PEOPLE) as PersonKey[]) {
      const person = world.people?.[key]
      if (!person) continue
      const rows = await admin.ok<{ roleId: string; system: boolean; revokedAtUtc: string | null }[]>(
        'GET',
        `/api/admin/access/assignments?employeeId=${person.id}`,
      )
      const manual = rows.filter((r) => !r.system && !r.revokedAtUtc)
      const grant = PEOPLE[key].grant
      const expected = grant ? (grant.scope === 'Branch' ? (grant.branches ?? []).length : 1) : 0
      const ok = manual.length === expected && manual.every((r) => grant && r.roleId === world.roles[grant.template])
      checks.push({ check: `${person.name}: yalnız ilkin rol`, ok, detail: ok ? undefined : `${manual.length} aktiv əl ilə təyinat` })
    }

    // 3: the run's sessions are closed, and nobody holds a working E2E PIN.
    for (const role of ROLE_KEYS.filter((r) => r !== 'admin')) {
      if (!tokens[role]) continue
      const probe = await new Direct(tokens[role]).get('/api/me/access')
      checks.push({ check: `${role}: run-ın sessiyası bağlanıb`, ok: probe.status === 401, detail: `HTTP ${probe.status}` })
      const person = world.people[role as PersonKey]
      const card = await admin.ok<{ mustChangePin: boolean }>('GET', `/api/admin/employees/${person.id}`)
      checks.push({ check: `${role}: işlək PIN qalmayıb`, ok: card.mustChangePin === true })
    }
  } catch (e) {
    checks.push({ check: 'təmizləmə', ok: false, detail: (e as Error).message })
  } finally {
    // 4: the files that held tokens.
    rmSync(AUTH_DIR, { recursive: true, force: true })
    checks.push({ check: 'sessiya faylları silinib', ok: !existsSync(AUTH_DIR) })
  }

  mkdirSync('test-results', { recursive: true })
  writeFileSync('test-results/e2e-teardown.json', JSON.stringify(checks, null, 2))
  const failed = checks.filter((c) => !c.ok)
  console.log(`\n[e2e] Təmizləmə yoxlaması: ${checks.length - failed.length}/${checks.length} keçdi.`)
  if (failed.length) {
    throw new Error(`E2E təmizləməsi tamamlanmadı:\n${failed.map((c) => `  - ${c.check}${c.detail ? ` (${c.detail})` : ''}`).join('\n')}`)
  }
}
