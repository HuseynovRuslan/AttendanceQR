import { mkdirSync, rmSync, writeFileSync } from 'node:fs'
import { Direct, LoginError, loginToken } from './support/api'
import { credentials } from './support/env'
import { TEMPLATE_PERMISSIONS } from './support/expectations'
import {
  ensureBranches,
  ensureGrant,
  ensurePerson,
  ensureShifts,
  ensureStructure,
  purgeCreated,
  rotate,
  storageState,
  tokenSubject,
} from './support/provision'
import { AUTH_DIR, PEOPLE, ROLE_KEYS, WORLD_FILE, pinVar, type PersonKey, type RoleKey, type World } from './support/world'

// Brings the staging `test` company into the state the specs expect, then hands each role a fresh
// session. Idempotent: everything is looked up by its E2E name first and only created when missing,
// so a second run reuses the first run's branches, structure, shifts and people.
//
// Credentials. Only the company administrator's are given to the suite. Every other account is an
// E2E person the admin created through the admin's own "İşçi əlavə et → Müvəqqəti PIN" path; each
// run the admin resets their PIN (the same "PIN sıfırla" a person would press), the run signs in
// with the temporary PIN and sets a new random one through /set-pin's endpoint — the flow every new
// employee goes through. Those PINs live in this process's memory only. Teardown resets them again,
// which also retires every session this run created. Login binds no device (binding happens at
// activation-by-link and at scan, neither of which the suite does), so nothing here goes around
// device binding.

export default async function globalSetup(): Promise<void> {
  rmSync(AUTH_DIR, { recursive: true, force: true })
  mkdirSync(AUTH_DIR, { recursive: true })
  const runId = new Date().toISOString().replace(/\D/g, '').slice(2, 14)
  const world = { runId, roleBlocked: {}, rolesEdited: {}, sessions: {}, logins: {} } as World
  const save = () => writeFileSync(WORLD_FILE, JSON.stringify(world, null, 2))

  const adminCreds = credentials('E2E_ADMIN')
  if (!adminCreds) {
    world.blocked = 'Admin giriş məlumatı yoxdur: E2E_ADMIN_PHONE və E2E_ADMIN_PIN (env və ya frontend/.env.e2e.local)'
    return save()
  }

  let adminToken: string
  try {
    adminToken = await loginToken(adminCreds)
  } catch (e) {
    world.blocked =
      e instanceof LoginError
        ? `Admin girişi alınmadı (HTTP ${e.status}${e.code ? `, ${e.code}` : ''})`
        : `Staging-ə qoşulmaq olmadı: ${(e as Error).message}`
    return save()
  }
  const admin = new Direct(adminToken)
  world.adminId = tokenSubject(adminToken)

  const me = await admin.ok<{ mode: string; companyAdmin: boolean }>('GET', '/api/me/access')
  world.mode = me.mode
  if (!me.companyAdmin) {
    world.blocked = 'Verilən hesab şirkət administratoru deyil — test dünyasını qura bilmir'
    return save()
  }

  const roles = await admin.ok<{ id: string; templateKey: string; permissions: string[]; archived: boolean }[]>(
    'GET',
    '/api/admin/access/roles?includeArchived=true',
  )
  world.roles = Object.fromEntries(roles.filter((r) => r.templateKey).map((r) => [r.templateKey, r.id]))
  for (const [tpl, perms] of Object.entries(TEMPLATE_PERMISSIONS)) {
    const r = roles.find((x) => x.templateKey === tpl)
    if (!r) {
      world.rolesEdited[tpl] = 'rol tapılmadı'
      continue
    }
    const added = r.permissions.filter((p) => !perms.includes(p))
    const removed = perms.filter((p) => !r.permissions.includes(p))
    if (r.archived || added.length || removed.length) {
      world.rolesEdited[tpl] = [
        r.archived ? 'arxivdədir' : '',
        added.length ? `əlavə: ${added.join(', ')}` : '',
        removed.length ? `çıxarılıb: ${removed.join(', ')}` : '',
      ]
        .filter(Boolean)
        .join('; ')
    }
  }

  world.branches = await ensureBranches(admin)
  world.structureId = await ensureStructure(admin, world.branches)
  world.shifts = await ensureShifts(admin, world.branches)
  await purgeCreated(admin)

  world.people = {} as World['people']
  for (const key of Object.keys(PEOPLE) as PersonKey[]) {
    world.people[key] = await ensurePerson(admin, key, world.branches)
    await ensureGrant(admin, key, world.people[key].id, world.roles, world.structureId, world.branches)
  }

  // Sessions. The admin's comes from the login above; each E2E person gets new credentials.
  // The admin's login is a real phone number: it stays in the environment, not in world.json.
  world.logins.admin = ''
  writeFileSync((world.sessions.admin = `${AUTH_DIR}admin.json`), storageState(adminToken))
  for (const role of ROLE_KEYS.filter((r): r is Exclude<RoleKey, 'admin'> => r !== 'admin')) {
    const person = world.people[role]
    world.logins[role] = person.email
    try {
      const { pin, token } = await rotate(admin, person.id, person.email)
      process.env[pinVar(role)] = pin
      writeFileSync((world.sessions[role] = `${AUTH_DIR}${role}.json`), storageState(token))
    } catch (e) {
      world.roleBlocked[role] = `Hesab hazırlana bilmədi: ${(e as Error).message}`
    }
  }
  save()
}
