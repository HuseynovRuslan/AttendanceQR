import { Navigate, useLocation } from 'react-router-dom'
import type { ReactNode } from 'react'
import { useAuth } from '../auth/AuthContext'

/** /admin/* — requires Admin or Manager. Employees are sent to their scan screen. */
export function AdminRoute({ children }: { children: ReactNode }) {
  const { isAuthenticated, role } = useAuth()
  const location = useLocation()
  if (!isAuthenticated) return <Navigate to="/login" state={{ from: location.pathname }} replace />
  if (role !== 'Admin' && role !== 'Manager') return <Navigate to="/scan" replace />
  return <>{children}</>
}

/** Admin-only sub-pages (invite, device approvals). Managers are bounced to the today board. */
export function AdminOnly({ children }: { children: ReactNode }) {
  const { role } = useAuth()
  if (role !== 'Admin') return <Navigate to="/admin/today" replace />
  return <>{children}</>
}
