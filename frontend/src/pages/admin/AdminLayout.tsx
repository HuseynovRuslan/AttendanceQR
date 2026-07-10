import { useEffect, useState } from 'react'
import { NavLink, Outlet, useLocation } from 'react-router-dom'
import { useAuth } from '../../auth/AuthContext'
import { BrandLogo } from '../../components/BrandLogo'
import { NotificationBell } from '../../components/NotificationBell'
import {
  IconAlert,
  IconCalendar,
  IconCamera,
  IconChart,
  IconClipboard,
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
const ROLE_LABEL: Record<string, string> = { Admin: 'Admin', Manager: 'Ərazi meneceri' }

const PAGE_META: Record<string, { title: string; sub: string }> = {
  '/admin/dashboard': { title: 'İdarəetmə paneli', sub: 'Ümumi baxış — canlı' },
  '/admin/today': { title: 'Bugünkü davamiyyət', sub: 'Canlı — hər 30 saniyədə yenilənir' },
  '/admin/reports': { title: 'Hesabatlar', sub: 'Tarix aralığı üzrə statistika' },
  '/admin/photo-audit': { title: 'Foto Audit', sub: 'Giriş şəklini referans ilə müqayisə et' },
  '/admin/problems': { title: 'Problemlər', sub: 'Rədd edilmiş skanlar — kim, nə vaxt, niyə' },
  '/admin/locations': { title: 'Lokasiyalar', sub: 'Ərazi əlavə et / redaktə et' },
  '/admin/non-working-days': { title: 'Qeyri-iş günləri', sub: 'Bayram və istirahət günləri' },
  '/admin/leaves': { title: 'Məzuniyyət / İcazə', sub: 'Təsdiqlənmiş yoxluq qeydləri' },
  '/admin/employees': { title: 'İşçilər', sub: 'İşçilərin idarəsi və qeydiyyatı' },
  '/admin/device-changes': { title: 'Cihaz təsdiqləri', sub: 'Gözləyən tələblər' },
}

const MOBILE_BREAKPOINT = 680

export function AdminLayout() {
  const { role, email, logout } = useAuth()
  const location = useLocation()
  const isAdmin = role === 'Admin'
  const meta = PAGE_META[location.pathname]
    ?? (location.pathname.endsWith('/print-qr') ? { title: 'Çap üçün QR', sub: 'Lokasiya üçün sabit kod' } : { title: 'Panel', sub: '' })

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
    { to: '/admin/today', label: 'Bugünkü davamiyyət', Icon: IconClipboard },
    { to: '/admin/reports', label: 'Hesabat', Icon: IconChart },
    // Admin + Manager (no isAdmin gate) — managers audit their own locations' employees.
    { to: '/admin/photo-audit', label: 'Foto Audit', Icon: IconCamera },
    { to: '/admin/problems', label: 'Problemlər', Icon: IconAlert },
    ...(isAdmin ? [{ to: '/admin/locations', label: 'Lokasiyalar', Icon: IconMapPin }] : []),
    ...(isAdmin ? [{ to: '/admin/non-working-days', label: 'Qeyri-iş günləri', Icon: IconCalendar }] : []),
    ...(isAdmin ? [{ to: '/admin/leaves', label: 'Məzuniyyət / İcazə', Icon: IconSun }] : []),
    ...(isAdmin ? [{ to: '/admin/employees', label: 'İşçilər', Icon: IconUsers }] : []),
    ...(isAdmin ? [{ to: '/admin/device-changes', label: 'Cihaz təsdiqləri', Icon: IconPhone }] : []),
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
            <div className="t1">Bakı Abadlıq</div>
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
