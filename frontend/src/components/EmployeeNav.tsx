import { NavLink } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'

/**
 * Persistent header for every employee-facing screen (scan, history, profile, device-change).
 * Deliberately minimal — large text, few choices — for an older, non-technical workforce.
 */
export function EmployeeNav({ title }: { title: string }) {
  const { logout } = useAuth()

  const linkClass = ({ isActive }: { isActive: boolean }) =>
    `px-3 py-2 rounded-lg text-base font-semibold transition ${
      isActive ? 'bg-white text-slate-900' : 'text-slate-300 hover:text-white hover:bg-slate-800'
    }`

  return (
    <header className="border-b border-slate-800 bg-slate-900">
      <div className="flex items-center justify-between px-4 py-3">
        <span className="font-semibold text-white">{title}</span>
        <button
          onClick={logout}
          className="text-sm text-slate-300 hover:text-white bg-slate-800 rounded-lg px-3 py-1.5"
        >
          Çıxış
        </button>
      </div>
      <nav className="flex items-center gap-2 px-4 pb-3">
        <NavLink to="/scan" className={linkClass}>
          Skan
        </NavLink>
        <NavLink to="/history" className={linkClass}>
          Tarixçəm
        </NavLink>
        <NavLink to="/profile" className={linkClass}>
          Profil
        </NavLink>
      </nav>
    </header>
  )
}
