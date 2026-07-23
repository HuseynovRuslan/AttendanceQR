import { useEffect, useState } from 'react'
import { Link, NavLink, Outlet, useLocation } from 'react-router-dom'
import { useAuth } from '../../auth/AuthContext'
import { useBranding } from '../../branding/BrandingContext'
import { BrandLogo } from '../../components/BrandLogo'
import { NotificationBell } from '../../components/NotificationBell'
import { getIsSuperAdmin } from '../../api/admin'
import { getTaskAccess } from '../../api/tasks'
import {
  IconAlert,
  IconBell,
  IconCalendar,
  IconCamera,
  IconCheck,
  IconChart,
  IconClipboard,
  IconClock,
  IconDownload,
  IconHome,
  IconLogout,
  IconMapPin,
  IconMenu,
  IconPhone,
  IconRefresh,
  IconSun,
  IconUsers,
  IconX,
} from '../../components/icons'

const ROLE_DOT: Record<string, string> = { Admin: '#F59E0B', Manager: '#7CB342' }
const ROLE_LABEL: Record<string, string> = { Admin: 'Admin', Manager: 'Filial meneceri' }

const PAGE_META: Record<string, { title: string; sub: string }> = {
  '/admin/dashboard': { title: 'İdarəetmə paneli', sub: 'Ümumi baxış — canlı' },
  '/admin/tenants': { title: 'Şirkətlər', sub: 'Bütün müştərilər — yarat, söndür, aç' },
  '/admin/live': { title: 'Canlı lövhə', sub: 'İndi kim işdədir — hər 20 saniyədə avtomatik yenilənir' },
  '/admin/today': { title: 'Davamiyyət', sub: 'Gün seçin — bugün canlı, keçmiş günlərə də baxın' },
  '/admin/reports': { title: 'Hesabatlar', sub: 'Tarix aralığı üzrə statistika' },
  '/admin/announcements': { title: 'Elanlar', sub: 'Bütün işçilərə bildiriş göndər' },
  '/admin/birthdays': { title: 'Doğum günləri', sub: 'Bu ay doğum günü olan işçilər' },
  '/admin/tabel': { title: 'Aylıq tabel', sub: 'Günlər üzrə davamiyyət cədvəli — çap və Excel' },
  '/admin/my-employees': { title: 'İşçilərim', sub: 'Öz filialınızın işçiləri — əlavə et, redaktə et' },
  '/admin/my-leaves': { title: 'Məzuniyyət / İcazə', sub: 'Öz işçiləriniz üçün icazə və məzuniyyət' },
  '/admin/positions': { title: 'Vəzifələr', sub: 'İşçi əlavə edərkən seçilən vəzifələrin siyahısı' },
  '/admin/vote': { title: 'Ayın işçisi', sub: 'Filial üzrə səsvermə nəticələri' },
  '/admin/payroll': { title: 'Maaş', sub: 'Aylıq maaş − qayıb = ödəniləcək; Excel-ə çıxar' },
  '/admin/photo-audit': { title: 'Foto Audit', sub: 'Giriş şəklini referans ilə müqayisə et' },
  '/admin/problems': { title: 'Problemlər', sub: 'Rədd edilmiş skanlar — kim, nə vaxt, niyə' },
  '/admin/open-records': { title: 'Çıxışı unudulan günlər', sub: 'Giriş edib çıxış etməyən günlər' },
  '/admin/locations': { title: 'Lokasiyalar', sub: 'Filial əlavə et / redaktə et' },
  '/admin/non-working-days': { title: 'Qeyri-iş günləri', sub: 'Bayram və istirahət günləri' },
  '/admin/leaves': { title: 'Məzuniyyət / İcazə', sub: 'Təsdiqlənmiş yoxluq qeydləri' },
  '/admin/tasks': { title: 'Tapşırıqlar', sub: 'Menecer və sahə nəzarətçilərinə tapşırıq ver, icrasını izlə' },
  '/admin/task-permissions': { title: 'Tapşırıq icazələri', sub: 'Kim kimə tapşırıq verə bilər — bölmə yalnız onlarda görünür' },
  '/admin/employees': { title: 'İşçilər', sub: 'İşçilərin idarəsi və qeydiyyatı' },
  '/admin/bulk-invite': { title: 'Toplu əlavə', sub: 'Çoxlu işçini birdən əlavə et' },
  '/admin/device-changes': { title: 'Cihazlar', sub: 'Gözləyən tələblər və bağlı cihazlar' },
}

const MOBILE_BREAKPOINT = 680

export function AdminLayout() {
  const { role, email, logout } = useAuth()
  const branding = useBranding()
  const location = useLocation()
  const isAdmin = role === 'Admin'
  const isManager = role === 'Manager'

  // Managing tenants is not a role — it is a config allowlist of employee ids, so only the server can
  // answer this. Asked once here rather than guessed, so the menu never offers a screen that 403s.
  const [isSuperAdmin, setIsSuperAdmin] = useState(false)
  useEffect(() => {
    if (!isAdmin) return
    void getIsSuperAdmin().then((r) => {
      if (r.status === 200 && r.data) setIsSuperAdmin(r.data.isSuperAdmin)
    })
  }, [isAdmin])

  // The Tapşırıqlar nav item is shown only to admins, granted assigners and their recipients — not
  // to every manager. The server answers who this is (see AdminTasksController.Access).
  const [canSeeTasks, setCanSeeTasks] = useState(false)
  useEffect(() => {
    void getTaskAccess().then((r) => {
      if (r.status === 200 && r.data) setCanSeeTasks(r.data.canSee)
    })
  }, [])

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
    { to: '/admin/live', label: 'Canlı lövhə', Icon: IconRefresh },
    { to: '/admin/today', label: 'Bugünkü davamiyyət', Icon: IconClipboard },
    { to: '/admin/reports', label: 'Hesabat', Icon: IconChart },
    { to: '/admin/tabel', label: 'Aylıq tabel', Icon: IconClipboard },
    ...(isManager ? [{ to: '/admin/my-employees', label: 'İşçilərim', Icon: IconUsers }] : []),
    ...(isManager ? [{ to: '/admin/my-leaves', label: 'Məzuniyyət / İcazə', Icon: IconSun }] : []),
    // Tapşırıqlar — shown only to admins + granted assigners/recipients (not every manager).
    ...((isAdmin || canSeeTasks) ? [{ to: '/admin/tasks', label: 'Tapşırıqlar', Icon: IconClipboard }] : []),
    ...(isAdmin ? [{ to: '/admin/task-permissions', label: 'Tapşırıq icazələri', Icon: IconCheck }] : []),
    ...(isAdmin ? [{ to: '/admin/payroll', label: 'Maaş', Icon: IconDownload }] : []),
    ...(isAdmin ? [{ to: '/admin/announcements', label: 'Elanlar', Icon: IconBell }] : []),
    ...(isAdmin ? [{ to: '/admin/birthdays', label: 'Doğum günləri', Icon: IconSun }] : []),
    ...(isAdmin ? [{ to: '/admin/vote', label: 'Ayın işçisi', Icon: IconCheck }] : []),
    // Admin + Manager (no isAdmin gate) — managers audit their own locations' employees.
    { to: '/admin/photo-audit', label: 'Foto Audit', Icon: IconCamera },
    { to: '/admin/problems', label: 'Problemlər', Icon: IconAlert },
    // Admin + Manager — managers approve their own locations' forgot-checkout requests.
    ...(isAdmin ? [{ to: '/admin/open-records', label: 'Çıxışı unudulan günlər', Icon: IconClock }] : []),
    ...(isAdmin ? [{ to: '/admin/locations', label: 'Lokasiyalar', Icon: IconMapPin }] : []),
    ...(isAdmin ? [{ to: '/admin/non-working-days', label: 'Qeyri-iş günləri', Icon: IconCalendar }] : []),
    ...(isAdmin ? [{ to: '/admin/leaves', label: 'Məzuniyyət / İcazə', Icon: IconSun }] : []),
    ...(isAdmin ? [{ to: '/admin/employees', label: 'İşçilər', Icon: IconUsers }] : []),
    ...(isAdmin ? [{ to: '/admin/bulk-invite', label: 'Toplu əlavə', Icon: IconUsers }] : []),
    ...(isAdmin ? [{ to: '/admin/positions', label: 'Vəzifələr', Icon: IconClipboard }] : []),
    ...(isAdmin ? [{ to: '/admin/device-changes', label: 'Cihazlar', Icon: IconPhone }] : []),
    // Across every company, not inside one — only the operator sees it.
    ...(isSuperAdmin ? [{ to: '/admin/tenants', label: 'Şirkətlər', Icon: IconUsers }] : []),
    ...(isSuperAdmin ? [{ to: '/hq', label: 'Qrup paneli', Icon: IconChart }] : []),
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
          {/* Managers see the bell too now — it carries their task alerts (admins also get device/birthday). */}
          <div className="topbar-right"><NotificationBell /></div>
        </div>
        <div className="content">
          <Outlet />
        </div>
      </main>
    </div>
  )
}
