import { Navigate, useLocation } from 'react-router-dom'
import type { ReactNode } from 'react'
import { useAuth } from '../auth/AuthContext'

/** Guards a route: unauthenticated users are sent to /login, remembering where they were headed. */
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
  return <>{children}</>
}
