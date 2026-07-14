import { NavLink, Outlet, useNavigate } from 'react-router-dom'
import type { ComponentType, SVGProps } from 'react'
import { BrandLogo } from '../components/BrandLogo'
import { useBranding } from '../branding/BrandingContext'
import { IconBell, IconChart, IconHome, IconQr, IconUser } from '../components/icons'

type Tab = { to: string; label: string; Icon: ComponentType<SVGProps<SVGSVGElement>> }

const LEFT: Tab[] = [
  { to: '/home', label: 'Ana səhifə', Icon: IconHome },
  { to: '/stats', label: 'Statistika', Icon: IconChart },
]
const RIGHT: Tab[] = [
  { to: '/notifications', label: 'Bildirişlər', Icon: IconBell },
  { to: '/menu', label: 'Menyu', Icon: IconUser },
]

/**
 * Employee mobile shell: light theme, a sticky brand header, a scrollable content area, and a fixed
 * bottom tab bar whose center is an elevated circular Scan FAB (the primary action → /scan).
 */
export function EmployeeLayout() {
  const navigate = useNavigate()
  const branding = useBranding()
  // QRLog-branded tenants: the logo pill already says "QRLog", so show the company name beside it.
  // bax (leaf) keeps the original "AttendanceQR" wordmark untouched.
  const isQrlog = branding.logoUrl === '/brand/qrlog.svg'

  return (
    <div className="min-h-screen bg-slate-50 text-slate-900 flex flex-col">
      <header className="sticky top-0 z-20 bg-white border-b border-slate-100 h-14 flex items-center gap-2 px-4">
        <BrandLogo size={26} />
        {isQrlog ? (
          <span className="font-extrabold text-lg tracking-tight">{branding.displayName}</span>
        ) : (
          <span className="font-extrabold text-lg tracking-tight">
            Attendance<span className="text-blue-600">QR</span>
          </span>
        )}
      </header>

      <main className="flex-1 w-full max-w-md mx-auto px-4 pt-4 pb-28">
        <Outlet />
      </main>

      <nav className="fixed inset-x-0 bottom-0 z-30 bg-white border-t border-slate-200">
        <div className="relative mx-auto grid h-16 max-w-md grid-cols-5">
          {LEFT.map((t) => (
            <TabLink key={t.to} {...t} />
          ))}
          <span aria-hidden />
          {RIGHT.map((t) => (
            <TabLink key={t.to} {...t} />
          ))}

          <button
            onClick={() => navigate('/scan')}
            aria-label="Skan et"
            className="absolute left-1/2 -top-6 h-16 w-16 -translate-x-1/2 rounded-full bg-blue-600 text-white shadow-lg shadow-blue-600/30 ring-4 ring-slate-50 flex items-center justify-center transition active:scale-95"
          >
            <IconQr className="h-7 w-7" />
          </button>
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
        `flex flex-col items-center justify-center gap-1 text-[10px] font-semibold leading-none transition ${
          isActive ? 'text-blue-600' : 'text-slate-400'
        }`
      }
    >
      <Icon className="h-6 w-6" />
      <span className="whitespace-nowrap">{label}</span>
    </NavLink>
  )
}
