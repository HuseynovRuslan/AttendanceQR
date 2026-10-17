import { existsSync, readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'

// Everything the suite creates on staging, by name. All of it lives in the staging `test` company,
// is prefixed "E2E", and is found again by that name on the next run rather than created twice.
// Nothing outside this list is ever written to.

export const AUTH_DIR = fileURLToPath(new URL('../.auth/', import.meta.url))
export const WORLD_FILE = `${AUTH_DIR}world.json`

// Logins of the E2E accounts. `.invalid` is a reserved top-level domain: these addresses can never
// belong to anybody, and nothing in QRLog sends mail anyway.
const MAIL = 'e2e.qrlog.invalid'

export const NAMES = {
  structure: 'E2E Struktur',
  branchA1: 'E2E Filial A1',
  branchA2: 'E2E Filial A2',
  branchB: 'E2E Filial B',
  shiftMorning: 'E2E Növbə Səhər',
  shiftEvening: 'E2E Növbə Axşam',
  /** Prefix of the employees the tests themselves add (and delete). */
  created: 'E2E Yeni',
} as const

export type Branch = 'A1' | 'A2' | 'B'

/** The five roles under test. `admin` is the company administrator given to the suite. */
export type RoleKey = 'admin' | 'operator' | 'tabelci' | 'musahideci' | 'menecer'
export const ROLE_KEYS: RoleKey[] = ['admin', 'operator', 'tabelci', 'musahideci', 'menecer']

export const ROLE_TITLES: Record<RoleKey, string> = {
  admin: 'Admin (Şirkət administratoru)',
  operator: 'Struktur operatoru',
  tabelci: 'Tabelçi',
  musahideci: 'Müşahidəçi',
  menecer: 'Köhnə Manager (Filial meneceri)',
}

export interface PersonSpec {
  name: string
  email: string
  home: Branch
  /** The account type on the employee record. Only the legacy manager is anything but Employee. */
  accountRole: 'Employee' | 'Manager'
  /** Branches a legacy manager manages — what the old model's role grant was. */
  managed?: Branch[]
  /** The role this person holds in the permission model, granted by the admin. */
  grant?: { template: string; scope: 'Structure' | 'Branch'; branches?: Branch[] }
  /** Whether the suite signs in as this person (and therefore rotates their PIN each run). */
  signsIn: boolean
}

export type PersonKey = Exclude<RoleKey, 'admin'> | 'workerA1' | 'workerA2' | 'workerB'

export const PEOPLE: Record<PersonKey, PersonSpec> = {
  operator: {
    name: 'E2E Operator',
    email: `operator@${MAIL}`,
    home: 'A1',
    accountRole: 'Employee',
    grant: { template: 'structure_operator', scope: 'Structure' },
    signsIn: true,
  },
  tabelci: {
    name: 'E2E Tabelçi',
    email: `tabelci@${MAIL}`,
    home: 'A1',
    accountRole: 'Employee',
    grant: { template: 'timekeeper', scope: 'Branch', branches: ['A1'] },
    signsIn: true,
  },
  musahideci: {
    name: 'E2E Müşahidəçi',
    email: `musahideci@${MAIL}`,
    home: 'A2',
    accountRole: 'Employee',
    grant: { template: 'observer', scope: 'Structure' },
    signsIn: true,
  },
  menecer: {
    name: 'E2E Menecer',
    email: `menecer@${MAIL}`,
    home: 'B',
    accountRole: 'Manager',
    managed: ['B'],
    signsIn: true,
  },
  // Plain staff, one per branch: what the roles above should and should not be able to reach.
  workerA1: { name: 'E2E İşçi A1', email: `isci-a1@${MAIL}`, home: 'A1', accountRole: 'Employee', signsIn: false },
  workerA2: { name: 'E2E İşçi A2', email: `isci-a2@${MAIL}`, home: 'A2', accountRole: 'Employee', signsIn: false },
  workerB: { name: 'E2E İşçi B', email: `isci-b@${MAIL}`, home: 'B', accountRole: 'Employee', signsIn: false },
}

export interface World {
  /** Set when nothing can run (no admin credentials, staging unreachable…), with the reason. */
  blocked?: string
  /** Set per role when that role cannot run, with the reason. */
  roleBlocked: Partial<Record<RoleKey, string>>
  mode: string
  runId: string
  branches: Record<Branch, string>
  structureId: string
  shifts: { morning: string; evening: string }
  /** Template key → role id in this company. */
  roles: Record<string, string>
  /** Template roles whose permissions were edited away from the template, with what changed. */
  rolesEdited: Record<string, string>
  people: Record<PersonKey, { id: string; name: string; email: string }>
  /** The admin's employee id (from the token). */
  adminId: string
  /** Logins for the UI login test: identifiers only, never a PIN. */
  logins: Record<RoleKey, string>
  /** Storage-state files holding each role's session. */
  sessions: Record<RoleKey, string>
}

export function loadWorld(): World {
  if (!existsSync(WORLD_FILE)) {
    return { blocked: 'Qlobal setup işləməyib (e2e/.auth/world.json yoxdur)' } as World
  }
  return JSON.parse(readFileSync(WORLD_FILE, 'utf8')) as World
}

/** Env name of the PIN the current run gave a role. Kept in memory only (process env). */
export function pinVar(role: RoleKey): string {
  return role === 'admin' ? 'E2E_ADMIN_PIN' : `E2E_RUN_${role.toUpperCase()}_PIN`
}
