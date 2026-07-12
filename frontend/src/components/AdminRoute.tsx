import { Navigate, useLocation } from 'react-router-dom'
import type { ReactNode } from 'react'
import { useAuth } from '../auth/AuthContext'

/** /admin/* — requires Admin or Manager. Employees are sent to their scan screen. */
export function AdminRoute({ children }: { children: ReactNode }) {
  const { isAuthenticated, role, mustChangePin } = useAuth()
  const location = useLocation()
  if (!isAuthenticated) return <Navigate to="/login" state={{ from: location.pathname }} replace />
  // A manager/admin created with (or reset to) a temporary PIN sets their own PIN first.
  if (mustChangePin) return <Navigate to="/set-pin" replace />
  if (role !== 'Admin' && role !== 'Manager') return <Navigate to="/scan" replace />
  return <>{children}</>
}

/** Admin-only sub-pages (invite, device approvals). Managers are bounced to the today board. */
export function AdminOnly({ children }: { children: ReactNode }) {
  const { role } = useAuth()
  if (role !== 'Admin') return <Navigate to="/admin/today" replace />
  return <>{children}</>
}
