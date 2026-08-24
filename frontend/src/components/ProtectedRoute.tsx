import { Navigate, useLocation } from 'react-router-dom'
import { getImpersonation } from '../api/client'
import type { ReactNode } from 'react'
import { useAuth } from '../auth/AuthContext'

/** Guards a route: unauthenticated users are sent to /login, remembering where they were headed. */
/** The employee shell — where somebody records their own attendance. Mirrors ImpersonationBoundary. */
const EMPLOYEE_ONLY = ['/home', '/scan', '/field', '/stats', '/history', '/vote', '/menu', '/notifications']

export function ProtectedRoute({ children }: { children: ReactNode }) {
  const { isAuthenticated, mustChangePin } = useAuth()
  const location = useLocation()
  if (!isAuthenticated) {
    return <Navigate to="/login" state={{ from: location.pathname }} replace />
  }
  // Account still on a temporary PIN — force the "set your PIN" screen before anything else.
  if (mustChangePin && location.pathname !== '/set-pin') {
    return <Navigate to="/set-pin" replace />
  }
  // An operator impersonating a customer's admin has no business on the employee side: a check-in
  // there would be filed under the borrowed admin, with the operator's face in it, and would become
  // that person's face-audit baseline. The server refuses those writes outright (ImpersonationBoundary
  // — it is the rule, this is only the door), but a screen that opens a camera and then fails on save
  // is worse than one that never opens. Every protected screen passes through here, so the list lives
  // in one place.
  if (getImpersonation() && EMPLOYEE_ONLY.some((p) => location.pathname.startsWith(p))) {
    return <Navigate to="/admin" replace />
  }
  return <>{children}</>
}
