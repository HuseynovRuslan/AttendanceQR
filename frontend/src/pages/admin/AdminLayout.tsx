import { NavLink, Outlet, useLocation } from 'react-router-dom'
import { useAuth } from '../../auth/AuthContext'
import { BrandLogo } from '../../components/BrandLogo'
import {
  IconChart,
  IconClipboard,
  IconLogout,
  IconPhone,
  IconSend,
} from '../../components/icons'

const ROLE_DOT: Record<string, string> = { Admin: '#F59E0B', Manager: '#7CB342' }
const ROLE_LABEL: Record<string, string> = { Admin: 'Admin', Manager: 'Ərazi meneceri' }

const PAGE_META: Record<string, { title: string; sub: string }> = {
  '/admin/today': { title: 'Bugünkü davamiyyət', sub: 'Canlı — hər 30 saniyədə yenilənir' },
  '/admin/reports': { title: 'Hesabatlar', sub: 'Tarix aralığı üzrə statistika' },
  '/admin/invite': { title: 'İşçi dəvəti', sub: 'Yeni işçi qeydiyyatı' },
  '/admin/device-changes': { title: 'Cihaz təsdiqləri', sub: 'Gözləyən tələblər' },
}

export function AdminLayout() {
  const { role, email, logout } = useAuth()
  const location = useLocation()
  const isAdmin = role === 'Admin'
  const meta = PAGE_META[location.pathname] ?? { title: 'Panel', sub: '' }

  const links = [
    { to: '/admin/today', label: 'Bugünkü davamiyyət', Icon: IconClipboard },
    { to: '/admin/reports', label: 'Hesabat', Icon: IconChart },
    ...(isAdmin ? [{ to: '/admin/invite', label: 'İşçi dəvəti', Icon: IconSend }] : []),
    ...(isAdmin ? [{ to: '/admin/device-changes', label: 'Cihaz təsdiqləri', Icon: IconPhone }] : []),
  ]

  return (
    <div className="app">
      <aside className="sidebar">
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
          <div>
            <div className="topbar-title">{meta.title}</div>
            <div className="topbar-sub">{meta.sub}</div>
          </div>
          <div className="topbar-right" />
        </div>
        <div className="content">
          <Outlet />
        </div>
      </main>
    </div>
  )
}
