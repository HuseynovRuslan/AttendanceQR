import { useEffect, useMemo, useState } from 'react'
import './operator.css'
import { BrandLogo } from '../../components/BrandLogo'
import { NavLink, Outlet, useLocation } from 'react-router-dom'
import { useAuth } from '../../auth/AuthContext'
import { getSuperMe } from '../../api/admin'
import { OperatorContext } from './OperatorContext'
import {
  IconBell,
  IconChart,
  IconCheck,
  IconClipboard,
  IconClock,
  IconDownload,
  IconHome,
  IconLogout,
  IconMenu,
  IconUsers,
  IconX,
} from '../../components/icons'

type NavItem = { to: string; label: string; Icon: typeof IconHome; end: boolean; perm?: string }

// The operator console's own navigation — nothing here is a single company's screen; every item looks
// ACROSS tenants. A `perm` hides the item unless the operator's role holds that power (server enforces it
// regardless); everything else is a read any operator can see.
const LINKS: NavItem[] = [
  { to: '/', label: 'İcmal', Icon: IconHome, end: true },
  { to: '/tenants', label: 'Şirkətlər', Icon: IconClipboard, end: false },
  { to: '/health', label: 'Sağlamlıq', Icon: IconClock, end: false },
  // Business sections — hidden from «Qrup rəhbəri», who is the CUSTOMER's group head and has no
  // business seeing our plans, prices, invoices or the operator trail. The server gates these on
  // the same permission; this only stops him being shown a door that answers 403.
  { to: '/billing', label: 'Ödənişlər', Icon: IconDownload, end: false, perm: 'ViewBusiness' },
  { to: '/announcements', label: 'Qlobal elan', Icon: IconBell, end: false, perm: 'Announce' },
  { to: '/users', label: 'İstifadəçilər', Icon: IconUsers, end: false },
  { to: '/team', label: 'Komanda', Icon: IconCheck, end: false, perm: 'ManageTeam' },
  { to: '/audit', label: 'Audit', Icon: IconChart, end: false, perm: 'ViewBusiness' },
]

const TITLES: Record<string, string> = {
  '/': 'İcmal',
  '/tenants': 'Şirkətlər',
  '/health': 'Sağlamlıq',
  '/billing': 'Ödənişlər',
  '/announcements': 'Qlobal elan',
  '/users': 'İstifadəçilər',
  '/team': 'Komanda',
  '/audit': 'Audit',
}

const ROLE_LABEL: Record<string, string> = { Full: 'Tam səlahiyyət', Support: 'Dəstək', Billing: 'Faktura' }

const MOBILE_BREAKPOINT = 680

export function OperatorLayout() {
  const { email, logout } = useAuth()
  const location = useLocation()
  const [sidebarOpen, setSidebarOpen] = useState(false)
  const [operator, setOperator] = useState<{ role: string | null; permissions: string[] }>({ role: null, permissions: [] })

  useEffect(() => {
    void getSuperMe().then((r) => {
      if (r.status === 200 && r.data) setOperator({ role: r.data.role, permissions: r.data.permissions ?? [] })
    })
  }, [])

  useEffect(() => setSidebarOpen(false), [location.pathname])
  useEffect(() => {
    function onResize() {
      if (window.innerWidth > MOBILE_BREAKPOINT) setSidebarOpen(false)
    }
    window.addEventListener('resize', onResize)
    return () => window.removeEventListener('resize', onResize)
  }, [])

  // The part before the @ — a name, not an address. Memoised only to keep it out of every render's
  // work; it changes once, at sign-in.
  const displayName = useMemo(() => (email ? email.split('@')[0] : '—'), [email])

  const title = TITLES[location.pathname] ?? 'SuperAdmin paneli'
  const links = LINKS.filter((l) => !l.perm || operator.permissions.includes(l.perm))

  return (
    // `op-shell` is what makes this console dark. The frame is shared with the tenant admin panel,
    // which stays light on purpose, so the re-skin is scoped to this class rather than applied to the
    // shell's own selectors — see operator.css.
    <div className="app op-shell">
      {sidebarOpen && <div className="sidebar-overlay" onClick={() => setSidebarOpen(false)} />}

      <aside className={`sidebar${sidebarOpen ? ' open' : ''}`}>
        <div className="sidebar-logo">
          {/* The product's own mark, like every other shell — this pointed at /brand/qrlog.svg, which
              is a sentinel path rather than a picture, so the console wore the old logo. */}
          <div className="logo-mark">
            <BrandLogo size={34} />
          </div>
          <div className="logo-text">
            <div className="t1">QRLog</div>
            <div className="t2">SuperAdmin paneli</div>
          </div>
        </div>

        <div className="sidebar-role">
          <div className="role-badge">
            <span className="role-dot" style={{ background: '#1E70C8' }} />
            <div style={{ minWidth: 0 }}>
              {/* The address is the identity but not the label: «baxmmc.ai@gmail.com» overflowed the
                  sidebar, so the local part is shown and the whole thing stays in the tooltip. */}
              <div className="role-name" title={email ?? undefined}>{displayName}</div>
              <div className="op-role-badge">
                {operator.role ? ROLE_LABEL[operator.role] ?? operator.role : 'Operator'}
              </div>
            </div>
          </div>
        </div>

        <nav className="sidebar-nav">
          {links.map(({ to, label, Icon, end }) => (
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
          <OperatorContext.Provider value={operator}>
            <Outlet />
          </OperatorContext.Provider>
        </div>
      </main>
    </div>
  )
}
