import { useEffect, useState } from 'react'
import { NavLink, Outlet, useLocation } from 'react-router-dom'
import { useAuth } from '../../auth/AuthContext'
import {
  IconBell,
  IconChart,
  IconClipboard,
  IconClock,
  IconDownload,
  IconHome,
  IconLogout,
  IconMenu,
  IconUsers,
  IconX,
} from '../../components/icons'

// The operator console's own navigation — nothing here is a single company's screen; every item looks
// ACROSS tenants. Grows as the platform features land (Billing, Sağlamlıq, Elanlar, Komanda).
const LINKS = [
  { to: '/', label: 'İcmal', Icon: IconHome, end: true },
  { to: '/tenants', label: 'Şirkətlər', Icon: IconClipboard, end: false },
  { to: '/health', label: 'Sağlamlıq', Icon: IconClock, end: false },
  { to: '/billing', label: 'Ödənişlər', Icon: IconDownload, end: false },
  { to: '/announcements', label: 'Qlobal elan', Icon: IconBell, end: false },
  { to: '/users', label: 'İstifadəçilər', Icon: IconUsers, end: false },
  { to: '/audit', label: 'Audit', Icon: IconChart, end: false },
]

const TITLES: Record<string, string> = {
  '/': 'İcmal',
  '/tenants': 'Şirkətlər',
  '/health': 'Sağlamlıq',
  '/billing': 'Ödənişlər',
  '/announcements': 'Qlobal elan',
  '/users': 'İstifadəçilər',
  '/audit': 'Audit',
}

const MOBILE_BREAKPOINT = 680

export function OperatorLayout() {
  const { email, logout } = useAuth()
  const location = useLocation()
  const [sidebarOpen, setSidebarOpen] = useState(false)

  useEffect(() => setSidebarOpen(false), [location.pathname])
  useEffect(() => {
    function onResize() {
      if (window.innerWidth > MOBILE_BREAKPOINT) setSidebarOpen(false)
    }
    window.addEventListener('resize', onResize)
    return () => window.removeEventListener('resize', onResize)
  }, [])

  const title = TITLES[location.pathname] ?? 'Operator paneli'

  return (
    <div className="app">
      {sidebarOpen && <div className="sidebar-overlay" onClick={() => setSidebarOpen(false)} />}

      <aside className={`sidebar${sidebarOpen ? ' open' : ''}`}>
        <div className="sidebar-logo">
          <div className="logo-mark">
            <img src="/brand/qrlog.svg" alt="QRLog" width={34} height={34} />
          </div>
          <div className="logo-text">
            <div className="t1">QRLog</div>
            <div className="t2">Operator paneli</div>
          </div>
        </div>

        <div className="sidebar-role">
          <div className="role-badge">
            <span className="role-dot" style={{ background: '#1E70C8' }} />
            <div>
              <div className="role-name">{email ?? '—'}</div>
              <div className="role-area">Operator</div>
            </div>
          </div>
        </div>

        <nav className="sidebar-nav">
          {LINKS.map(({ to, label, Icon, end }) => (
            <NavLink key={to} to={to} end={end} className={({ isActive }) => `nav-item${isActive ? ' active' : ''}`}>
              <Icon />
              {label}
            </NavLink>
          ))}
        </nav>

        <div className="sidebar-footer">
          <button onClick={logout} className="nav-item" style={{ color: 'var(--c400)' }}>
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
              <div className="topbar-title">{title}</div>
              <div className="topbar-sub">Bütün şirkətlər üzrə platforma idarəetməsi</div>
            </div>
          </div>
        </div>
        <div className="content">
          <Outlet />
        </div>
      </main>
    </div>
  )
}
