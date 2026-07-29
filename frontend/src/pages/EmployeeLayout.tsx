import { NavLink, Outlet } from 'react-router-dom'
import type { ComponentType, SVGProps } from 'react'
import { BrandLogo } from '../components/BrandLogo'
import { useBranding } from '../branding/BrandingContext'
import { IconBell, IconChart, IconHome, IconUser } from '../components/icons'

type Tab = { to: string; label: string; Icon: ComponentType<SVGProps<SVGSVGElement>> }

// Four flat tabs, purposeful names (not the generic Home/Stats/Notifications/Menu). Scan is NOT a tab:
// it lives as the hero on "Bu gün", so the bar has no raised centre circle — the exact thing that made
// this read like every other attendance app.
const TABS: Tab[] = [
  { to: '/home', label: 'Bu gün', Icon: IconHome },
  { to: '/stats', label: 'Saatlarım', Icon: IconChart },
  { to: '/notifications', label: 'Xəbərlər', Icon: IconBell },
  { to: '/menu', label: 'Profil', Icon: IconUser },
]

/**
 * Employee mobile shell: light theme, a sticky brand header, a scrollable content area, and a fixed
 * flat bottom tab bar. Scanning is reached from the "Bu gün" hero, not a bottom-bar button.
 */
export function EmployeeLayout() {
  const branding = useBranding()
  // QRLog-branded tenants: the logo pill already says "QRLog", so show the company name beside it.
  // bax (leaf) keeps the original "AttendanceQR" wordmark untouched.
  const isQrlog = branding.logoUrl === '/brand/qrlog.svg'

  return (
    <div className="min-h-screen bg-slate-50 text-slate-900 flex flex-col">
      <header className="sticky top-0 z-20 bg-white border-b border-slate-100 h-14 flex items-center gap-2 px-4">
        {isQrlog ? (
          <>
            <BrandLogo size={26} />
            {branding.displayName && (
              <span className="font-extrabold text-lg tracking-tight">{branding.displayName}</span>
            )}
          </>
        ) : (
          // No tenant branding (bax / unbranded host): show the QRLog product logo, not the old
          // "AttendanceQR" placeholder wordmark.
          <img
            src="/brand/qrlog-logo.png"
            alt="QRLog"
            style={{ height: 26, width: 'auto', borderRadius: 5, border: '1px solid rgba(15,27,45,0.08)' }}
          />
        )}
      </header>

      <main className="flex-1 w-full max-w-md mx-auto px-4 pt-4 pb-28">
        <Outlet />
      </main>

      <nav className="fixed inset-x-0 bottom-0 z-30 bg-white border-t border-slate-200">
        <div className="mx-auto grid h-16 max-w-md grid-cols-4">
          {TABS.map((t) => (
            <TabLink key={t.to} {...t} />
          ))}
        </div>
      </nav>
    </div>
  )
}

function TabLink({ to, label, Icon }: Tab) {
  return (
    <NavLink
      to={to}
      className={({ isActive }) =>
        `flex flex-col items-center justify-center gap-1 text-[11px] font-semibold leading-none transition ${
          isActive ? 'text-blue-600' : 'text-slate-400'
        }`
      }
    >
      <Icon className="h-6 w-6" />
      <span className="whitespace-nowrap">{label}</span>
    </NavLink>
  )
}
