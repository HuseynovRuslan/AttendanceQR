import { useEffect, useState } from 'react'
import { Link, NavLink, Outlet, useLocation } from 'react-router-dom'
import { useAuth } from '../../auth/AuthContext'
import { useBranding, useFeatureEnabled, FEATURE } from '../../branding/BrandingContext'
import { BrandLogo } from '../../components/BrandLogo'
import { NotificationBell } from '../../components/NotificationBell'
import { getSidebarBadges, type SidebarBadges } from '../../api/notifications'
import {
  IconAlert,
  IconBell,
  IconBriefcase,
  IconBuilding,
  IconCalendar,
  IconChart,
  IconCheck,
  IconClipboard,
  IconClock,
  IconGift,
  IconHome,
  IconKey,
  IconLogout,
  IconMapPin,
  IconMenu,
  IconMoney,
  IconPhone,
  IconRefresh,
  IconSun,
  IconTable,
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
  '/admin/field-visits': { title: 'Sahə ziyarətləri', sub: 'Səyyar işçilər — tapşır, görülən işi və şəkli yoxla' },
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

  // The team board is no longer allowlisted — it is tenant-scoped, so every admin and manager of the
  // company shares theirs and sees no other. The /api/tasks/access probe it used to need is gone.
  const meta = PAGE_META[location.pathname]
    ?? (location.pathname.endsWith('/print-qr') ? { title: 'Çap üçün QR', sub: 'Lokasiya üçün sabit kod' }
      : location.pathname.startsWith('/admin/employees/') ? { title: 'İşçi profili', sub: 'İşçinin tam məlumatı və əməliyyatlar' }
      : { title: 'Panel', sub: '' })

  const [sidebarOpen, setSidebarOpen] = useState(false)

  // Shelf counts for the rows that WAIT on the admin. Refetched on every navigation rather than on a
  // timer: approving a device request is itself a navigation, so the badge drops the moment the work
  // is done, and an idle tab doesn't poll. A failed fetch just means no badges — never an error.
  const [badges, setBadges] = useState<SidebarBadges | null>(null)
  useEffect(() => {
    if (!isAdmin) return
    void getSidebarBadges().then((r) => {
      if (r.status === 200 && r.data && 'deviceChanges' in r.data) setBadges(r.data)
    }).catch(() => {})
  }, [isAdmin, location.pathname])

  const badgeFor: Record<string, number | undefined> = {
    '/admin/device-changes': badges?.deviceChanges,
    '/admin/pin-resets': badges?.pinResets,
    '/admin/open-records': badges?.openRecords,
    '/admin/tasks': badges?.overdueTasks,
  }

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

  // Grouped by CADENCE, not by feature: what an admin opens every morning sits at the top, what
  // they open once a month sits at the bottom, and the section label answers "where would that be?"
  // without reading nineteen rows. A section whose every row is filtered out (role, tenant flag)
  // disappears with them — an empty heading would just be new clutter.
  //
  // Foto Audit stays hidden on purpose (see the removed route's note in App.tsx); "Toplu əlavə"
  // lives inside the employees page's add flow; the platform-operator screens moved to the separate
  // console on admin.qrlog.az.
  const sections: { title: string | null; links: { to: string; label: string; Icon: typeof IconHome }[] }[] = [
    {
      title: null, // the everyday landing screens need no caption over them
      links: [
        ...(isAdmin ? [{ to: '/admin/dashboard', label: 'İdarəetmə paneli', Icon: IconHome }] : []),
        ...(isAdmin || isManager ? [{ to: '/admin/tasks', label: 'Tapşırıqlar', Icon: IconCheck }] : []),
        ...(isAdmin && announcementsOn ? [{ to: '/admin/announcements', label: 'Elanlar', Icon: IconBell }] : []),
      ],
    },
    {
      title: 'Davamiyyət',
      links: [
        { to: '/admin/today', label: 'Bugünkü davamiyyət', Icon: IconClipboard },
        // Admin + Manager (no gate) — the endpoints enforce the role.
        { to: '/admin/field-visits', label: 'Sahə ziyarətləri', Icon: IconMapPin },
        { to: '/admin/problems', label: 'Problemlər', Icon: IconAlert },
        ...(isAdmin ? [{ to: '/admin/open-records', label: 'Çıxışı unudulan günlər', Icon: IconClock }] : []),
      ],
    },
    {
      title: 'Hesabat',
      links: [
        { to: '/admin/reports', label: 'Hesabat', Icon: IconChart },
        { to: '/admin/tabel', label: 'Aylıq tabel', Icon: IconTable },
        ...(isAdmin && payrollOn ? [{ to: '/admin/payroll', label: 'Maaş', Icon: IconMoney }] : []),
      ],
    },
    {
      title: 'İşçilər',
      links: [
        ...(isAdmin ? [{ to: '/admin/employees', label: 'İşçilər', Icon: IconUsers }] : []),
        ...(isManager ? [{ to: '/admin/my-employees', label: 'İşçilərim', Icon: IconUsers }] : []),
        ...(isAdmin ? [{ to: '/admin/leaves', label: 'Məzuniyyət / İcazə', Icon: IconSun }] : []),
        ...(isManager ? [{ to: '/admin/my-leaves', label: 'Məzuniyyət / İcazə', Icon: IconSun }] : []),
        { to: '/admin/schedules', label: 'Növbələr', Icon: IconRefresh },
        ...(isAdmin ? [{ to: '/admin/positions', label: 'Vəzifələr', Icon: IconBriefcase }] : []),
        ...(isAdmin ? [{ to: '/admin/birthdays', label: 'Doğum günləri', Icon: IconGift }] : []),
      ],
    },
    {
      // The inbox screens: rows that WAIT for the admin, as opposed to screens they go look at.
      title: 'Müraciətlər',
      links: [
        ...(isAdmin ? [{ to: '/admin/device-changes', label: 'Cihazlar', Icon: IconPhone }] : []),
        ...(isAdmin ? [{ to: '/admin/pin-resets', label: 'PIN sıfırlama', Icon: IconKey }] : []),
      ],
    },
    {
      title: 'Tənzimləmələr',
      links: [
        ...(isAdmin ? [{ to: '/admin/locations', label: 'Lokasiyalar', Icon: IconBuilding }] : []),
        ...(isAdmin ? [{ to: '/admin/non-working-days', label: 'Qeyri-iş günləri', Icon: IconCalendar }] : []),
      ],
    },
  ].filter((s) => s.links.length > 0)

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
          {sections.map((section, i) => (
            <div key={section.title ?? `s${i}`} className="nav-section">
              {section.title && <div className="nav-section-title">{section.title}</div>}
              {section.links.map(({ to, label, Icon }) => (
                <NavLink key={to} to={to} className={({ isActive }) => `nav-item${isActive ? ' active' : ''}`}>
                  <Icon />
                  <span className="nav-label">{label}</span>
                  {(badgeFor[to] ?? 0) > 0 && (
                    <span className="nav-badge">{badgeFor[to]! > 99 ? '99+' : badgeFor[to]}</span>
                  )}
                </NavLink>
              ))}
            </div>
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
