import { Navigate, useLocation } from 'react-router-dom'
import type { ReactNode } from 'react'
import { useAuth } from '../auth/AuthContext'
import { AccessProvider, useAccess } from '../auth/AccessContext'
import { canOpen, wouldOpen } from '../lib/panelAccess'

/**
 * /admin/* — today's Admin and Manager, as before, and since Phase 4 anybody holding a role in the new
 * permission model (a timekeeper or an observer who is plain staff today). Everybody else is sent to their
 * scan screen.
 */
export function AdminRoute({ children }: { children: ReactNode }) {
  const { isAuthenticated, mustChangePin } = useAuth()
  const location = useLocation()
  if (!isAuthenticated) return <Navigate to="/login" state={{ from: location.pathname }} replace />
  // A manager/admin created with (or reset to) a temporary PIN sets their own PIN first.
  if (mustChangePin) return <Navigate to="/set-pin" replace />
  return (
    <AccessProvider>
      <PanelDoor>{children}</PanelDoor>
    </AccessProvider>
  )
}

/** Today's two panel roles walk straight in, without waiting on anything; anybody else waits for
 *  /api/me/access to say whether the panel is theirs. */
function PanelDoor({ children }: { children: ReactNode }) {
  const { role } = useAuth()
  const { access, loading } = useAccess()
  if (role === 'Admin' || role === 'Manager') return <>{children}</>
  if (loading) {
    return (
      <div className="muted" style={{ padding: 32, textAlign: 'center' }}>
        Yüklənir…
      </div>
    )
  }
  if (access?.panel) return <>{children}</>
  return <Navigate to="/scan" replace />
}

/**
 * One page of the panel, gated by the shared table in lib/panelAccess.
 *
 * Replaces the old <AdminOnly>, which decided role in the router while AdminLayout decided it again
 * for the menu. They drifted: seven rows a manager could see bounced them silently back to the today
 * board for two commits, because a silent redirect is indistinguishable from a mis-tap.
 *
 * So this says no out loud — and says which no it is: a page the new role will open after the switch,
 * or a page that is somebody else's altogether. A refusal a person can read is a refusal somebody reports.
 */
export function PanelPage({ path, children }: { path: string; children: ReactNode }) {
  const { role } = useAuth()
  const { access } = useAccess()
  if (canOpen(path, role, access)) return <>{children}</>

  if (wouldOpen(path, access)) {
    return (
      <div className="card card-pad" style={{ maxWidth: 560 }}>
        <div className="card-title">Bu bölmə keçiddən sonra açılacaq</div>
        <p className="muted" style={{ fontSize: 13, lineHeight: 1.7, margin: '8px 0 0' }}>
          Rolunuz bu bölməni əhatə edir, amma şirkət yeni səlahiyyət qaydasına hələ keçməyib. Keçid
          təsdiqlənəndə bölmə sizin sahənizlə açılacaq. Hələlik «Mənim sahəm» səhifəsində nəyin harada
          açılacağını görə bilərsiniz.
        </p>
      </div>
    )
  }

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
