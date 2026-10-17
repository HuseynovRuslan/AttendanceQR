import { mkdirSync, rmSync, writeFileSync } from 'node:fs'
import { Direct, LoginError, companyName, loginToken } from './support/api'
import { E2EConfigError, EXPECTED_COMPANY, adminCredentials } from './support/env'
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
// Anything that stops it is an error with its reason, never a silent skip: a CI job without the secret, a
// wrong company, a login refused — each fails the run before a single test, and says why.
//
// Credentials. Only the company administrator's are given to the suite, through the environment. Every
// other account is an E2E person the admin created through the admin's own "İşçi əlavə et → Müvəqqəti
// PIN" path; each run the admin resets their PIN (the same "PIN sıfırla" a person would press), the run
// signs in with the temporary PIN and sets a new random one through /set-pin's endpoint — the flow every
// new employee goes through. Those PINs live in this process's memory only. Teardown resets them again,
// which also retires every session this run created. Login binds no device (binding happens at
// activation-by-link and at scan, neither of which the suite does), so nothing here goes around
// device binding.

function writeSecret(path: string, value: string): void {
  writeFileSync(path, value, { mode: 0o600 })
}

export default async function globalSetup(): Promise<void> {
  rmSync(AUTH_DIR, { recursive: true, force: true })
  mkdirSync(AUTH_DIR, { recursive: true, mode: 0o700 })
  const adminCreds = adminCredentials()

  // Whose data this is, before anything is written: the API's own answer for the staging site.
  const company = await companyName().catch((e: Error) => {
    throw new E2EConfigError(`staging API-yə qoşulmaq olmadı: ${e.message}`)
  })
  if (company !== EXPECTED_COMPANY) {
    throw new E2EConfigError(`API «${company ?? '—'}» şirkətini qaytardı, gözlənilən «${EXPECTED_COMPANY}» — heç nə yazılmadı`)
  }

  let adminToken: string
  try {
    adminToken = await loginToken(adminCreds)
  } catch (e) {
    throw e instanceof LoginError
      ? new E2EConfigError(`admin girişi alınmadı (HTTP ${e.status}${e.code ? `, ${e.code}` : ''}) — E2E_ADMIN_* secret-lərini yoxlayın`)
      : e
  }
  const admin = new Direct(adminToken)
  const runId = new Date().toISOString().replace(/\D/g, '').slice(2, 14)
  const world = { runId, rolesEdited: {}, sessions: {}, tokens: {}, logins: {} } as unknown as World
  world.adminId = tokenSubject(adminToken)
  // The admin can also sign in with their e-mail, which pages print: the scrubber redacts it from every
  // artifact (this process's env is the reporter's).
  const adminCard = await admin.get<{ email?: string }>(`/api/admin/employees/${world.adminId}`)
  if (adminCard.body?.email) process.env.E2E_ADMIN_EMAIL = adminCard.body.email

  const me = await admin.ok<{ mode: string; companyAdmin: boolean }>('GET', '/api/me/access')
  world.mode = me.mode
  if (!me.companyAdmin) throw new E2EConfigError('verilən hesab şirkət administratoru deyil — test dünyasını qura bilmir')
  if (me.mode !== 'Enforced') throw new E2EConfigError(`şirkət ${me.mode} rejimindədir; bu dəst Enforced üçündür`)

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

  // Sessions: the admin's from the login above; each E2E person gets new credentials. The browser gets a
  // display token (storage state); the real one gets a file of its own, read only outside the page.
  world.logins.admin = ''
  writeSecret((world.tokens.admin = `${AUTH_DIR}admin.token`), adminToken)
  writeSecret((world.sessions.admin = `${AUTH_DIR}admin.json`), storageState(adminToken))
  for (const role of ROLE_KEYS.filter((r): r is Exclude<RoleKey, 'admin'> => r !== 'admin')) {
    const person = world.people[role]
    world.logins[role] = person.email
    const { pin, token } = await rotate(admin, person.id, person.email).catch((e: Error) => {
      throw new E2EConfigError(`${role} hesabı hazırlana bilmədi: ${e.message}`)
    })
    process.env[pinVar(role)] = pin
    writeSecret((world.tokens[role] = `${AUTH_DIR}${role}.token`), token)
    writeSecret((world.sessions[role] = `${AUTH_DIR}${role}.json`), storageState(token))
  }
  writeFileSync(WORLD_FILE, JSON.stringify(world, null, 2))
}
