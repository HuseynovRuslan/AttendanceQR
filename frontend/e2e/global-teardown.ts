import { existsSync, readFileSync, rmSync } from 'node:fs'
import { Direct } from './support/api'
import { ensureGrant, purgeCreated } from './support/provision'
import { AUTH_DIR, PEOPLE, loadWorld, type PersonKey } from './support/world'

// Leaves staging as setup found it, minus nothing it should keep:
//  - employees the tests added are deleted (setup also sweeps any a crashed run left);
//  - every E2E person is back on exactly their baseline role, whatever a live-change test did;
//  - every E2E person who signed in gets their PIN reset by the admin and the temporary PIN is
//    thrown away. That retires every session this run opened — the tokens in the traces included —
//    and leaves nobody holding a working E2E credential until the next run sets a new one.
// The E2E branches, structure, shifts and people stay, so the next run does not create them again.
export default async function globalTeardown(): Promise<void> {
  const world = loadWorld()
  try {
    if (world.blocked || !world.sessions?.admin || !existsSync(world.sessions.admin)) return
    const state = JSON.parse(readFileSync(world.sessions.admin, 'utf8'))
    const admin = new Direct(state.origins[0].localStorage[0].value as string)

    await purgeCreated(admin)
    for (const key of Object.keys(PEOPLE) as PersonKey[]) {
      const person = world.people?.[key]
      if (!person) continue
      await ensureGrant(admin, key, person.id, world.roles, world.structureId, world.branches)
      if (PEOPLE[key].signsIn) await admin.post(`/api/admin/employees/${person.id}/reset-pin`)
    }
  } finally {
    // Session files hold tokens: they do not outlive the run.
    rmSync(AUTH_DIR, { recursive: true, force: true })
  }
}
