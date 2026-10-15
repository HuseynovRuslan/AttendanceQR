/**
 * The role editor's checkboxes, in the order an admin thinks about a job rather than the catalogue's
 * alphabet: what they see, the people, their phones, field work, leave, shifts, pay, branches — and the
 * rights about rights last, where nobody ticks one by accident.
 */
export interface PermissionGroup {
  title: string
  keys: string[]
}

const GROUPS: { title: string; prefixes: string[] }[] = [
  { title: 'Panel və davamiyyət', prefixes: ['dashboard', 'attendance'] },
  { title: 'İşçilər', prefixes: ['employees'] },
  { title: 'Telefonlar', prefixes: ['devices'] },
  { title: 'Səyyar iş və giriş qaydaları', prefixes: ['field_checkin', 'checkin_rules', 'field_visits'] },
  { title: 'Məzuniyyət', prefixes: ['leaves'] },
  { title: 'Növbələr', prefixes: ['schedules'] },
  { title: 'Maaş', prefixes: ['payroll'] },
  { title: 'Filiallar', prefixes: ['locations'] },
  { title: 'Bildiriş və tapşırıqlar', prefixes: ['notifications', 'tasks'] },
  { title: 'Şirkət ayarları', prefixes: ['holidays', 'positions', 'announcements', 'votes', 'equipment', 'billing', 'diag'] },
  { title: 'Struktur və səlahiyyətlər', prefixes: ['structures', 'assignments', 'roles'] },
]

const OTHER = 'Digər'

const prefixOf = (key: string) => key.split('.')[0]

/** Every key exactly once, in a fixed group order; within a group the order given. Empty groups are left out. */
export function groupPermissions(keys: string[]): PermissionGroup[] {
  const byTitle = new Map<string, string[]>()
  for (const key of keys) {
    const group = GROUPS.find((g) => g.prefixes.includes(prefixOf(key)))
    const title = group?.title ?? OTHER
    const list = byTitle.get(title) ?? []
    list.push(key)
    byTitle.set(title, list)
  }
  return [...GROUPS.map((g) => g.title), OTHER]
    .filter((title) => byTitle.has(title))
    .map((title) => ({ title, keys: byTitle.get(title)! }))
}

export const SCOPE_LABEL: Record<'Company' | 'Structure' | 'Branch', string> = {
  Company: 'Bütün şirkət',
  Structure: 'Struktur',
  Branch: 'Filial',
}

/** Template roles get a short everyday name in lists; a company's own roles keep theirs. */
export const TEMPLATE_HINT: Record<string, string> = {
  company_admin: 'Hər şey, bütün şirkətdə',
  structure_operator: 'Strukturun bütün filiallarında gündəlik iş',
  timekeeper: 'Seçilmiş filiallarda tabel və işçilər',
  observer: 'Yalnız baxır, heç nə dəyişmir',
  legacy_manager: 'Bugünkü menecer — işçi formasından gəlir',
  pin_helper: 'Bugünkü menecerin PIN imkanı — işçi formasından gəlir',
}
