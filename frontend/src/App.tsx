import { lazy, Suspense, useEffect } from 'react'
import { Route, Routes, useLocation } from 'react-router-dom'
import { useAppUpdate } from './lib/useAppUpdate'
import { ProtectedRoute } from './components/ProtectedRoute'
import { AdminRoute, AdminOnly } from './components/AdminRoute'
import { AdminIndexRedirect } from './components/AdminIndexRedirect'
import { HomeRedirect } from './components/HomeRedirect'
import { LoginPage } from './pages/LoginPage'
import { ActivatePage } from './pages/ActivatePage'
import { ScanPage } from './pages/ScanPage'
import { HistoryPage } from './pages/HistoryPage'
import { ProfilePage } from './pages/ProfilePage'
import { DeviceChangeRequestPage } from './pages/DeviceChangeRequestPage'
import { EmployeeLayout } from './pages/EmployeeLayout'
import { SetPinPage } from './pages/SetPinPage'
import { HomePage } from './pages/HomePage'
import { StatsPage } from './pages/StatsPage'
import { NotificationsPage } from './pages/NotificationsPage'
import { MenuPage } from './pages/MenuPage'

// The employee pages above are imported eagerly on purpose: they are the whole app for almost
// everyone who opens it, and ScanPage especially must never wait on a network round-trip — there is
// no service worker, and someone standing at the gate on a weak signal is exactly who would pay for
// a lazy chunk.
//
// Everything below is loaded on demand. The admin panel is the only consumer of leaflet (~150 kB)
// and jspdf, and the kiosk display is the only one that needs qrcode.react — none of which an
// employee's phone has any use for. Splitting by route is what keeps those libraries out of their
// download rather than merely off their screen.
const KioskPage = lazy(() => import('./pages/KioskPage').then(m => ({ default: m.KioskPage })))
const AdminLayout = lazy(() => import('./pages/admin/AdminLayout').then(m => ({ default: m.AdminLayout })))
const DashboardPage = lazy(() => import('./pages/admin/DashboardPage').then(m => ({ default: m.DashboardPage })))
const TodayPage = lazy(() => import('./pages/admin/TodayPage').then(m => ({ default: m.TodayPage })))
const ReportsPage = lazy(() => import('./pages/admin/ReportsPage').then(m => ({ default: m.ReportsPage })))
const LocationsPage = lazy(() => import('./pages/admin/LocationsPage').then(m => ({ default: m.LocationsPage })))
const NonWorkingDaysPage = lazy(() => import('./pages/admin/NonWorkingDaysPage').then(m => ({ default: m.NonWorkingDaysPage })))
const LeavesPage = lazy(() => import('./pages/admin/LeavesPage').then(m => ({ default: m.LeavesPage })))
const PrintQrPage = lazy(() => import('./pages/admin/PrintQrPage').then(m => ({ default: m.PrintQrPage })))
const EmployeesPage = lazy(() => import('./pages/admin/EmployeesPage').then(m => ({ default: m.EmployeesPage })))
const EmployeeProfilePage = lazy(() => import('./pages/admin/EmployeeProfilePage').then(m => ({ default: m.EmployeeProfilePage })))
const DeviceChangesPage = lazy(() => import('./pages/admin/DeviceChangesPage').then(m => ({ default: m.DeviceChangesPage })))
const PhotoAuditPage = lazy(() => import('./pages/admin/PhotoAuditPage').then(m => ({ default: m.PhotoAuditPage })))
const ProblemsPage = lazy(() => import('./pages/admin/ProblemsPage').then(m => ({ default: m.ProblemsPage })))
const OpenRecordsPage = lazy(() => import('./pages/admin/OpenRecordsPage').then(m => ({ default: m.OpenRecordsPage })))
const BulkInvitePage = lazy(() => import('./pages/admin/BulkInvitePage').then(m => ({ default: m.BulkInvitePage })))

/** Reloads the app once a newer build exists. Silent by design: employees will not tap an "update"
 *  banner, and an installed PWA is otherwise stuck on whatever bundle it launched with. Never fires
 *  mid-scan or mid-activation, where a reload would throw away work in progress. */
function AutoUpdater() {
  const newBuildId = useAppUpdate()
  const { pathname } = useLocation()

  useEffect(() => {
    if (!newBuildId) return
    if (pathname === '/scan' || pathname === '/activate') return

    // Belt and braces: if a reload somehow served the same stale bundle again (a cached index.html
    // would do it), we would spin forever. One attempt per published build, per tab.
    const key = 'attendanceqr.reloadedFor'
    if (sessionStorage.getItem(key) === newBuildId) return
    sessionStorage.setItem(key, newBuildId)
    window.location.reload()
  }, [newBuildId, pathname])

  return null
}

/** Shown while a lazily-loaded route's chunk is in flight. Deliberately quiet: on a fast connection
 *  it flashes for a few frames, and a spinner there reads as jank rather than progress. */
function RouteFallback() {
  return <div style={{ minHeight: '60vh' }} aria-busy="true" />
}

export default function App() {
  return (
    <>
      <AutoUpdater />
      {/* One boundary around every route: React needs a Suspense ancestor for any lazy element, and
          the eager employee routes never suspend, so they never see it. */}
      <Suspense fallback={<RouteFallback />}>
        <AppRoutes />
      </Suspense>
    </>
  )
}

function AppRoutes() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route path="/activate" element={<ActivatePage />} />
      {/* Employee mobile shell: bottom-tab pages share the EmployeeLayout (light theme + tab bar). */}
      <Route
        element={
          <ProtectedRoute>
            <EmployeeLayout />
          </ProtectedRoute>
        }
      >
        <Route path="/home" element={<HomePage />} />
        <Route path="/stats" element={<StatsPage />} />
        <Route path="/notifications" element={<NotificationsPage />} />
        <Route path="/menu" element={<MenuPage />} />
      </Route>

      {/* Full-screen scanner (no bottom bar) — reached from the center Scan button. */}
      <Route
        path="/scan"
        element={
          <ProtectedRoute>
            <ScanPage />
          </ProtectedRoute>
        }
      />
      <Route
        path="/history"
        element={
          <ProtectedRoute>
            <HistoryPage />
          </ProtectedRoute>
        }
      />
      <Route
        path="/profile"
        element={
          <ProtectedRoute>
            <ProfilePage />
          </ProtectedRoute>
        }
      />
      {/* Forced first-login PIN set (temp-PIN accounts). Guards route every other path here. */}
      <Route
        path="/set-pin"
        element={
          <ProtectedRoute>
            <SetPinPage />
          </ProtectedRoute>
        }
      />
      <Route
        path="/device-change-request"
        element={
          <ProtectedRoute>
            <DeviceChangeRequestPage />
          </ProtectedRoute>
        }
      />

      {/* Kiosk: one URL per location, no login. */}
      <Route path="/kiosk/:locationId" element={<KioskPage />} />
      <Route path="/kiosk" element={<KioskPage />} />

      {/* Admin / Manager panel. */}
      <Route
        path="/admin"
        element={
          <AdminRoute>
            <AdminLayout />
          </AdminRoute>
        }
      >
        <Route index element={<AdminIndexRedirect />} />
        <Route
          path="dashboard"
          element={
            <AdminOnly>
              <DashboardPage />
            </AdminOnly>
          }
        />
        <Route path="today" element={<TodayPage />} />
        <Route path="reports" element={<ReportsPage />} />
        {/* Photo audit — open to Admin + Manager (like today/reports); managers are scoped to their
            own locations server-side via LocationScopeRules, so no AdminOnly wrapper. */}
        <Route path="photo-audit" element={<PhotoAuditPage />} />
        {/* Rejected-scan log — Admin + Manager (manager scoped to their locations server-side). */}
        <Route path="problems" element={<ProblemsPage />} />
        {/* Unclosed days — Admin only, since fixing a record (setting a check-out) is Admin only. */}
        <Route
          path="open-records"
          element={
            <AdminOnly>
              <OpenRecordsPage />
            </AdminOnly>
          }
        />
        <Route
          path="locations"
          element={
            <AdminOnly>
              <LocationsPage />
            </AdminOnly>
          }
        />
        <Route
          path="non-working-days"
          element={
            <AdminOnly>
              <NonWorkingDaysPage />
            </AdminOnly>
          }
        />
        <Route
          path="leaves"
          element={
            <AdminOnly>
              <LeavesPage />
            </AdminOnly>
          }
        />
        <Route
          path="locations/:locationId/print-qr"
          element={
            <AdminOnly>
              <PrintQrPage />
            </AdminOnly>
          }
        />
        <Route
          path="employees"
          element={
            <AdminOnly>
              <EmployeesPage />
            </AdminOnly>
          }
        />
        <Route
          path="employees/:id"
          element={
            <AdminOnly>
              <EmployeeProfilePage />
            </AdminOnly>
          }
        />
        <Route
          path="bulk-invite"
          element={
            <AdminOnly>
              <BulkInvitePage />
            </AdminOnly>
          }
        />
        <Route
          path="device-changes"
          element={
            <AdminOnly>
              <DeviceChangesPage />
            </AdminOnly>
          }
        />
      </Route>

      {/* "/" and anything unknown → login, or the role's home if already signed in. */}
      <Route path="*" element={<HomeRedirect />} />
    </Routes>
  )
}
