import { Navigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { roleHome } from '../lib/jwt'

/** Sends "/" and unknown paths to the right place: login if signed out, else the role's home. */
export function HomeRedirect() {
  const { isAuthenticated, role } = useAuth()
  if (!isAuthenticated) return <Navigate to="/login" replace />
  return <Navigate to={roleHome(role)} replace />
}
