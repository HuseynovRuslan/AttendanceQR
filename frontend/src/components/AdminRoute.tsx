import { Navigate, useLocation } from 'react-router-dom'
import type { ReactNode } from 'react'
import { useAuth } from '../auth/AuthContext'
import { canOpen } from '../lib/panelAccess'

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

/**
 * One page of the panel, gated by the shared table in lib/panelAccess.
 *
 * Replaces the old <AdminOnly>, which decided role in the router while AdminLayout decided it again
 * for the menu. They drifted: seven rows a manager could see bounced them silently back to the today
 * board for two commits, because a silent redirect is indistinguishable from a mis-tap.
 *
 * So this says no out loud. A refusal a person can read is a refusal somebody reports.
 */
export function PanelPage({ path, children }: { path: string; children: ReactNode }) {
  const { role } = useAuth()
  if (canOpen(path, role)) return <>{children}</>

  return (
    <div className="card card-pad" style={{ maxWidth: 520 }}>
      <div className="card-title">Bu səhifəyə icazəniz yoxdur</div>
      <p className="muted" style={{ fontSize: 13, lineHeight: 1.7, margin: '8px 0 0' }}>
        Bu bölmə bütün şirkətə aiddir, ona görə yalnız admin aça bilir. Sizə lazımdırsa,
        şirkətin adminindən xahiş edin.
      </p>
    </div>
  )
}
