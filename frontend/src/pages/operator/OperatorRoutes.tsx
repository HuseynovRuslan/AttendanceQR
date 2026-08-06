import { Navigate, Route, Routes } from 'react-router-dom'
import { OperatorLayout } from './OperatorLayout'
import { BillingPage } from './BillingPage'
import { HealthPage } from './HealthPage'
import { GlobalAnnouncementsPage } from './GlobalAnnouncementsPage'
// The four operator sections live in TenantsPage (they were its tabs); the operator shell routes them
// as pages. TenantsPage's own tabbed wrapper is no longer mounted anywhere — the sidebar replaces it.
import { SuperAudit, SuperOverview, SuperUsers, TenantsTab } from '../admin/TenantsPage'

/**
 * Routing for the platform operator console (admin.qrlog.az). Rendered only on the operator host and
 * only for an authenticated operator who is NOT impersonating (see App.tsx — while impersonating, the
 * operator holds a tenant token and the normal tenant routes take over).
 */
export function OperatorRoutes() {
  return (
    <Routes>
      <Route element={<OperatorLayout />}>
        <Route index element={<SuperOverview />} />
        <Route path="tenants" element={<TenantsTab />} />
        <Route path="health" element={<HealthPage />} />
        <Route path="billing" element={<BillingPage />} />
        <Route path="announcements" element={<GlobalAnnouncementsPage />} />
        <Route path="users" element={<SuperUsers />} />
        <Route path="audit" element={<SuperAudit />} />
      </Route>
      {/* Unknown path (incl. leftover tenant-admin URLs like /admin/tenants) → the İcmal home. */}
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  )
}
