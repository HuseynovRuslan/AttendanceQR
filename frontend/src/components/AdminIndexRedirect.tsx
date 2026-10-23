import { Navigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { useAccess } from '../auth/AccessContext'
import { canOpen } from '../lib/panelAccess'

/** /admin → the screen this person starts their day on. Somebody whose panel comes only from a role in
 *  the new model starts on «Mənim sahəm» — the rest of the panel opens for them after the switch. */
export function AdminIndexRedirect() {
  const { role } = useAuth()
  const { access } = useAccess()
  // Enforced: the Company administrator starts on the dashboard, as today's admin; anybody else on the day's
  // board if their role reaches it (today's manager does), else on what they may open.
  if (access?.mode === 'Enforced') {
    const start = access.companyAdmin ? 'dashboard'
      : (['today', 'dashboard'] as const).find((p) => canOpen(p, role, access)) ?? 'my-area'
    return <Navigate to={start} replace />
  }
  return <Navigate to={role === 'Admin' ? 'dashboard' : role === 'Manager' ? 'today' : 'my-area'} replace />
}
