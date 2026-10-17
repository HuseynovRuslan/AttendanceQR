import { Navigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'

/** /admin → the screen this person starts their day on. Somebody whose panel comes only from a role in
 *  the new model starts on «Mənim sahəm» — the rest of the panel opens for them after the switch. */
export function AdminIndexRedirect() {
  const { role } = useAuth()
  return <Navigate to={role === 'Admin' ? 'dashboard' : role === 'Manager' ? 'today' : 'my-area'} replace />
}
