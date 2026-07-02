import { NavLink, Outlet } from 'react-router-dom'
import { useAuth } from '../../auth/AuthContext'

export function AdminLayout() {
  const { role, email, logout } = useAuth()
  const isAdmin = role === 'Admin'

  const links = [
    { to: '/admin/today', label: 'Bugünkü davamiyyət' },
    { to: '/admin/reports', label: 'Hesabat' },
    ...(isAdmin ? [{ to: '/admin/invite', label: 'İşçi dəvəti' }] : []),
    ...(isAdmin ? [{ to: '/admin/device-changes', label: 'Cihaz təsdiqləri' }] : []),
  ]

  return (
    <div className="min-h-screen bg-slate-100">
      <header className="bg-slate-900 text-white">
        <div className="max-w-6xl mx-auto px-4 py-3 flex items-center justify-between flex-wrap gap-3">
          <div className="flex items-center gap-6 flex-wrap">
            <span className="font-bold text-lg">AttendanceQR</span>
            <nav className="flex gap-1 flex-wrap">
              {links.map((l) => (
                <NavLink
                  key={l.to}
                  to={l.to}
                  className={({ isActive }) =>
                    `px-3 py-1.5 rounded-lg text-sm transition ${
                      isActive ? 'bg-blue-600 text-white' : 'text-slate-300 hover:bg-slate-800'
                    }`
                  }
                >
                  {l.label}
                </NavLink>
              ))}
            </nav>
          </div>
          <div className="flex items-center gap-3 text-sm">
            <span className="text-slate-400">
              {email} · {role}
            </span>
            <button
              onClick={logout}
              className="bg-slate-800 hover:bg-slate-700 rounded-lg px-3 py-1.5"
            >
              Çıxış
            </button>
          </div>
        </div>
      </header>

      <main className="max-w-6xl mx-auto p-4">
        <Outlet />
      </main>
    </div>
  )
}
