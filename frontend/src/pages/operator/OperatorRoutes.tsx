import { Navigate, Route, Routes } from 'react-router-dom'
import { OperatorLayout } from './OperatorLayout'
import { BillingPage } from './BillingPage'
import { HealthPage } from './HealthPage'
import { GlobalAnnouncementsPage } from './GlobalAnnouncementsPage'
import { TeamPage } from './TeamPage'
// The four operator sections live in TenantsPage (they were its tabs); the operator shell routes them
// as pages. TenantsPage's own tabbed wrapper is no longer mounted anywhere — the sidebar replaces it.
import { SuperAudit, SuperUsers, TenantsTab } from '../admin/TenantsPage'
// İcmal is the group board itself. The old overview was a strip of platform counters; this one shows
// the companies, their people and their sites, and each card opens that company's own panel.
import { GroupBoardPage } from '../hq/GroupBoardPage'

/**
 * Routing for the platform operator console (admin.qrlog.az). Rendered only on the operator host and
 * only for an authenticated operator who is NOT impersonating (see App.tsx — while impersonating, the
 * operator holds a tenant token and the normal tenant routes take over).
 */
export function OperatorRoutes() {
  return (
    <Routes>
      <Route element={<OperatorLayout />}>
        <Route index element={<GroupBoardPage embedded />} />
        <Route path="tenants" element={<TenantsTab />} />
        <Route path="health" element={<HealthPage />} />
        <Route path="billing" element={<BillingPage />} />
        <Route path="announcements" element={<GlobalAnnouncementsPage />} />
        <Route path="team" element={<TeamPage />} />
        <Route path="users" element={<SuperUsers />} />
        <Route path="audit" element={<SuperAudit />} />
      </Route>
      {/* Unknown path (incl. leftover tenant-admin URLs like /admin/tenants) → the İcmal home. */}
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  )
}
