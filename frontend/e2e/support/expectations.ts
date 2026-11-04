import type { RoleKey } from './world'

// What each role is supposed to be, written out by hand from the role templates
// (src/AttendanceQR.Domain/Access/AccessRoleTemplates.cs) and the panel table
// (frontend/src/lib/panelAccess.ts) — deliberately NOT imported from them: a test that reads the
// rules from the code under test agrees with any change to that code. If a template changes on
// purpose, this file changes with it, in the same review.

export const TEMPLATE_PERMISSIONS: Record<string, string[]> = {
  structure_operator: [
    'dashboard.view', 'attendance.view', 'attendance.photos.view', 'attendance.export',
    'employees.view', 'employees.contacts.view', 'employees.create', 'employees.update',
    'employees.deactivate', 'employees.reset_pin', 'employees.change_phone',
    'devices.view', 'devices.approve', 'devices.revoke',
    'schedules.view', 'schedules.manage', 'schedules.assign',
    'leaves.view', 'leaves.manage', 'locations.view', 'locations.qr_print',
    'field_visits.view', 'field_visits.manage', 'assignments.view', 'notifications.staff_alerts',
  ],
  timekeeper: [
    'dashboard.view', 'attendance.view', 'attendance.export',
    'employees.view', 'employees.contacts.view', 'employees.create', 'employees.update', 'employees.reset_pin',
    'devices.view', 'devices.approve',
    'schedules.view', 'schedules.manage', 'schedules.assign',
    'leaves.view', 'leaves.manage', 'field_visits.view', 'notifications.staff_alerts',
  ],
  observer: [
    'dashboard.view', 'attendance.view', 'attendance.export', 'employees.view',
    'leaves.view', 'schedules.view', 'locations.view', 'field_visits.view',
  ],
}

/** The template each role under test holds (the legacy manager's comes from its account type). */
export const ROLE_TEMPLATE: Partial<Record<RoleKey, string>> = {
  operator: 'structure_operator',
  tabelci: 'timekeeper',
  musahideci: 'observer',
}

// Sidebar entries every panel user has.
const COMMON = [
  'İdarəetmə paneli', 'Bugünkü davamiyyət', 'Problemlər', 'Növbə uyğunsuzluğu', 'Sahə ziyarətləri',
  'İşçilər', 'Məzuniyyət / İcazə', 'Doğum günləri', 'Çıxışı unudulan günlər',
  'Hesabat', 'Aylıq tabel', 'Öz düzəlişləri', 'Növbələr', 'Mənim sahəm',
]

/** Company-wide pages: only the company administrator opens them in Enforced mode. */
export const ADMIN_ONLY = ['Elanlar', 'Vəzifələr', 'Kompüter inventarizasiyası', 'Qeyri-iş günləri', 'Abunəlik']

/** Exactly the sidebar (the <nav> labels) each scoped role should see in an Enforced company. */
export const EXPECTED_MENU: Record<Exclude<RoleKey, 'admin'>, string[]> = {
  operator: [...COMMON, 'PIN sıfırlama', 'Cihazlar', 'Radius uyğunsuzluğu', 'Filiallar', 'Səlahiyyətlər'],
  tabelci: [...COMMON, 'PIN sıfırlama', 'Cihazlar'],
  musahideci: [...COMMON, 'Radius uyğunsuzluğu', 'Filiallar'],
  menecer: [...COMMON, 'PIN sıfırlama', 'Cihazlar', 'Radius uyğunsuzluğu', 'Filiallar', 'Tapşırıqlar', 'Maaş'],
}

/** Must be in the company administrator's sidebar, whatever feature switches are on. */
export const ADMIN_MUST_HAVE = [...COMMON, 'PIN sıfırlama', 'Cihazlar', 'Filiallar', 'Səlahiyyətlər', 'Vəzifələr', 'Qeyri-iş günləri']

/** A page each scoped role must be refused when it types the URL in, and the route. */
export const FORBIDDEN_PAGE: Record<Exclude<RoleKey, 'admin'>, { path: string; label: string }> = {
  operator: { path: '/admin/positions', label: 'Vəzifələr' },
  tabelci: { path: '/admin/locations', label: 'Filiallar' },
  musahideci: { path: '/admin/pin-resets', label: 'PIN sıfırlama' },
  menecer: { path: '/admin/access', label: 'Səlahiyyətlər' },
}
