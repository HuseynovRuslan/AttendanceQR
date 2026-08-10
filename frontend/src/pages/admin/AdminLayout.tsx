import { useEffect, useState } from 'react'
import { Link, NavLink, Outlet, useLocation } from 'react-router-dom'
import { useAuth } from '../../auth/AuthContext'
import { useBranding, useFeatureEnabled, FEATURE } from '../../branding/BrandingContext'
import { BrandLogo } from '../../components/BrandLogo'
import { NotificationBell } from '../../components/NotificationBell'
import { getTaskAccess } from '../../api/tasks'
import {
  IconAlert,
  IconBell,
  IconCalendar,
  IconChart,
  IconCheck,
  IconClipboard,
  IconClock,
  IconDownload,
  IconHome,
  IconLogout,
  IconMapPin,
  IconMenu,
  IconPhone,
  IconSun,
  IconUsers,
  IconX,
} from '../../components/icons'

const ROLE_DOT: Record<string, string> = { Admin: '#F59E0B', Manager: '#7CB342' }
const ROLE_LABEL: Record<string, string> = { Admin: 'Admin', Manager: 'Filial meneceri' }

const PAGE_META: Record<string, { title: string; sub: string }> = {
  '/admin/dashboard': { title: 'İdarəetmə paneli', sub: 'Ümumi baxış — canlı' },
  '/admin/tenants': { title: 'Şirkətlər', sub: 'Bütün müştərilər — yarat, söndür, aç' },
  '/admin/tasks': { title: 'Tapşırıqlar', sub: 'Komandanın ortaq görüləcək işlər siyahısı' },
  '/admin/today': { title: 'Davamiyyət', sub: 'Gün seçin — bugün canlı, keçmiş günlərə də baxın' },
  '/admin/reports': { title: 'Hesabatlar', sub: 'Tarix aralığı üzrə statistika' },
  '/admin/announcements': { title: 'Elanlar', sub: 'Bütün işçilərə bildiriş göndər' },
  '/admin/birthdays': { title: 'Doğum günləri', sub: 'Bu ay doğum günü olan işçilər' },
  '/admin/tabel': { title: 'Aylıq tabel', sub: 'Günlər üzrə davamiyyət cədvəli — çap və Excel' },
  '/admin/my-employees': { title: 'İşçilərim', sub: 'Öz filialınızın işçiləri — əlavə et, redaktə et' },
  '/admin/my-leaves': { title: 'Məzuniyyət / İcazə', sub: 'Öz işçiləriniz üçün icazə və məzuniyyət' },
  '/admin/positions': { title: 'Vəzifələr', sub: 'İşçi əlavə edərkən seçilən vəzifələrin siyahısı' },
  '/admin/schedules': { title: 'Növbələr', sub: 'Saatlar, iş günləri və rotasiya — bir dəfə qurulur, işçilərə təyin edilir' },
  '/admin/payroll': { title: 'Maaş', sub: 'Aylıq maaş − qayıb = ödəniləcək; Excel-ə çıxar' },
  '/admin/problems': { title: 'Problemlər', sub: 'Rədd edilmiş skanlar — kim, nə vaxt, niyə' },
  '/admin/field-visits': { title: 'Sahə ziyarətləri', sub: 'Səyyar işçilər — tapşır, kim getdi/getmədi izlə' },
  '/admin/open-records': { title: 'Çıxışı unudulan günlər', sub: 'Giriş edib çıxış etməyən günlər' },
  '/admin/locations': { title: 'Lokasiyalar', sub: 'Filial əlavə et / redaktə et' },
  '/admin/non-working-days': { title: 'Qeyri-iş günləri', sub: 'Bayram və istirahət günləri' },
  '/admin/leaves': { title: 'Məzuniyyət / İcazə', sub: 'Təsdiqlənmiş yoxluq qeydləri' },
  '/admin/employees': { title: 'İşçilər', sub: 'İşçilərin idarəsi və qeydiyyatı' },
  '/admin/bulk-invite': { title: 'Toplu əlavə', sub: 'Çoxlu işçini birdən əlavə et' },
  '/admin/device-changes': { title: 'Cihazlar', sub: 'Gözləyən tələblər və bağlı cihazlar' },
  '/admin/pin-resets': { title: 'PIN sıfırlama', sub: 'PIN-ini unudan işçilərin tələbləri' },
}

const MOBILE_BREAKPOINT = 680

export function AdminLayout() {
  const { role, email, logout } = useAuth()
  const branding = useBranding()
  const location = useLocation()
  const isAdmin = role === 'Admin'
  const isManager = role === 'Manager'

  // Per-tenant plan switches. A hidden menu item is only cosmetic — the backend 403s these anyway
  // (see [RequireFeature]); this just stops offering a screen that would refuse.
  const payrollOn = useFeatureEnabled(FEATURE.Payroll)
  const announcementsOn = useFeatureEnabled(FEATURE.Announcements)

  // The shared "Tapşırıqlar" board — also an id allowlist, and available to managers too (a person
  // who is a manager in one company still needs their task list), so it is not gated on isAdmin.
  const [canTasks, setCanTasks] = useState(false)
  useEffect(() => {
    if (!isAdmin && !isManager) return
    void getTaskAccess().then((r) => {
      if (r.status === 200 && r.data) setCanTasks(r.data.canAccess)
    })
  }, [isAdmin, isManager])
  const meta = PAGE_META[location.pathname]
    ?? (location.pathname.endsWith('/print-qr') ? { title: 'Çap üçün QR', sub: 'Lokasiya üçün sabit kod' }
      : location.pathname.startsWith('/admin/employees/') ? { title: 'İşçi profili', sub: 'İşçinin tam məlumatı və əməliyyatlar' }
      : { title: 'Panel', sub: '' })

  const [sidebarOpen, setSidebarOpen] = useState(false)

  // Close the drawer on every navigation, and if the window is resized past the mobile
  // breakpoint while it happens to be open (e.g. rotating a tablet, or a resizable dev window).
  useEffect(() => setSidebarOpen(false), [location.pathname])
  useEffect(() => {
    function onResize() {
      if (window.innerWidth > MOBILE_BREAKPOINT) setSidebarOpen(false)
    }
    window.addEventListener('resize', onResize)
    return () => window.removeEventListener('resize', onResize)
  }, [])

  const links = [
    ...(isAdmin ? [{ to: '/admin/dashboard', label: 'İdarəetmə paneli', Icon: IconHome }] : []),
    ...(canTasks ? [{ to: '/admin/tasks', label: 'Tapşırıqlar', Icon: IconCheck }] : []),
    { to: '/admin/today', label: 'Bugünkü davamiyyət', Icon: IconClipboard },
    { to: '/admin/reports', label: 'Hesabat', Icon: IconChart },
    { to: '/admin/tabel', label: 'Aylıq tabel', Icon: IconClipboard },
    ...(isManager ? [{ to: '/admin/my-employees', label: 'İşçilərim', Icon: IconUsers }] : []),
    ...(isManager ? [{ to: '/admin/my-leaves', label: 'Məzuniyyət / İcazə', Icon: IconSun }] : []),
    ...(isManager ? [{ to: '/admin/schedules', label: 'Növbələr', Icon: IconCalendar }] : []),
    ...(isAdmin && payrollOn ? [{ to: '/admin/payroll', label: 'Maaş', Icon: IconDownload }] : []),
    ...(isAdmin && announcementsOn ? [{ to: '/admin/announcements', label: 'Elanlar', Icon: IconBell }] : []),
    ...(isAdmin ? [{ to: '/admin/birthdays', label: 'Doğum günləri', Icon: IconSun }] : []),
    // Foto Audit is hidden on purpose — see the note on the removed route in App.tsx.
    { to: '/admin/problems', label: 'Problemlər', Icon: IconAlert },
    // Field visits — Admin + Manager (no gate), the endpoints enforce the role.
    { to: '/admin/field-visits', label: 'Sahə ziyarətləri', Icon: IconMapPin },
    // Admin + Manager — managers approve their own locations' forgot-checkout requests.
    ...(isAdmin ? [{ to: '/admin/open-records', label: 'Çıxışı unudulan günlər', Icon: IconClock }] : []),
    ...(isAdmin ? [{ to: '/admin/locations', label: 'Lokasiyalar', Icon: IconMapPin }] : []),
    ...(isAdmin ? [{ to: '/admin/non-working-days', label: 'Qeyri-iş günləri', Icon: IconCalendar }] : []),
    ...(isAdmin ? [{ to: '/admin/leaves', label: 'Məzuniyyət / İcazə', Icon: IconSun }] : []),
    ...(isAdmin ? [{ to: '/admin/employees', label: 'İşçilər', Icon: IconUsers }] : []),
    // "Toplu əlavə" is no longer its own sidebar row — it lives inside the "İşçi əlavə et" flow on
    // the employees page (single-add first, then a bulk tab), so onboarding is one place. Route kept.
    ...(isAdmin ? [{ to: '/admin/positions', label: 'Vəzifələr', Icon: IconClipboard }] : []),
    ...(isAdmin ? [{ to: '/admin/schedules', label: 'Növbələr', Icon: IconCalendar }] : []),
    ...(isAdmin ? [{ to: '/admin/device-changes', label: 'Cihazlar', Icon: IconPhone }] : []),
    ...(isAdmin ? [{ to: '/admin/pin-resets', label: 'PIN sıfırlama', Icon: IconAlert }] : []),
    // Platform-operator screens (Şirkətlər, Qrup paneli) no longer live in a company's admin — they
    // moved to the separate operator console on admin.qrlog.az.
  ]

  return (
    <div className="app">
      {sidebarOpen && <div className="sidebar-overlay" onClick={() => setSidebarOpen(false)} />}

      <aside className={`sidebar${sidebarOpen ? ' open' : ''}`}>
        <div className="sidebar-logo">
          <div className="logo-mark">
            <BrandLogo size={34} />
          </div>
          <div className="logo-text">
            <div className="t1">{branding.displayName || 'Davamiyyət'}</div>
            <div className="t2">Davamiyyət sistemi</div>
          </div>
        </div>

        <div className="sidebar-role">
          <div className="role-badge">
            <span className="role-dot" style={{ background: ROLE_DOT[role ?? ''] ?? '#7CB342' }} />
            <div>
              <div className="role-name">{email ?? '—'}</div>
              <div className="role-area">{ROLE_LABEL[role ?? ''] ?? role}</div>
            </div>
          </div>
        </div>

        <nav className="sidebar-nav">
          {links.map(({ to, label, Icon }) => (
            <NavLink key={to} to={to} className={({ isActive }) => `nav-item${isActive ? ' active' : ''}`}>
              <Icon />
              {label}
            </NavLink>
          ))}
        </nav>

        <div className="sidebar-footer">
          {/* Admins/managers also clock in and out themselves — one tap over to the employee shell
              (scan lives behind its centre button). Without this they'd have to type the URL. */}
          <Link to="/home" className="nav-item" style={{ color: 'var(--c400)' }}>
            <IconPhone />
            İşçi rejimi (skan)
          </Link>
          <button
            onClick={logout}
            className="nav-item"
            style={{ color: 'var(--c400)' }}
          >
            <IconLogout />
            Çıxış
          </button>
        </div>
      </aside>

      <main className="main">
        <div className="topbar">
          <div style={{ display: 'flex', alignItems: 'center', gap: 12, minWidth: 0 }}>
            <button
              className="hamburger-btn"
              onClick={() => setSidebarOpen((v) => !v)}
              aria-label={sidebarOpen ? 'Menyunu bağla' : 'Menyunu aç'}
            >
              {sidebarOpen ? <IconX /> : <IconMenu />}
            </button>
            <div style={{ minWidth: 0 }}>
              <div className="topbar-title">{meta.title}</div>
              <div className="topbar-sub">{meta.sub}</div>
            </div>
          </div>
          <div className="topbar-right">{isAdmin && <NotificationBell />}</div>
        </div>
        <div className="content">
          <Outlet />
        </div>
      </main>
    </div>
  )
}
