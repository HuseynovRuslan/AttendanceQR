import { Navigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'

/** /admin index — Admin lands on the dashboard overview, Manager on the today board (no
 * cross-location breakdown to show them). */
export function AdminIndexRedirect() {
  const { role } = useAuth()
  return <Navigate to={role === 'Admin' ? 'dashboard' : 'today'} replace />
}
