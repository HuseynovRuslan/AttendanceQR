import { useEffect } from 'react'
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
import { KioskPage } from './pages/KioskPage'
import { EmployeeLayout } from './pages/EmployeeLayout'
import { SetPinPage } from './pages/SetPinPage'
import { HomePage } from './pages/HomePage'
import { StatsPage } from './pages/StatsPage'
import { NotificationsPage } from './pages/NotificationsPage'
import { MenuPage } from './pages/MenuPage'
import { AdminLayout } from './pages/admin/AdminLayout'
import { DashboardPage } from './pages/admin/DashboardPage'
import { TodayPage } from './pages/admin/TodayPage'
import { ReportsPage } from './pages/admin/ReportsPage'
import { LocationsPage } from './pages/admin/LocationsPage'
import { NonWorkingDaysPage } from './pages/admin/NonWorkingDaysPage'
import { LeavesPage } from './pages/admin/LeavesPage'
import { PrintQrPage } from './pages/admin/PrintQrPage'
import { EmployeesPage } from './pages/admin/EmployeesPage'
import { DeviceChangesPage } from './pages/admin/DeviceChangesPage'
import { PhotoAuditPage } from './pages/admin/PhotoAuditPage'
import { ProblemsPage } from './pages/admin/ProblemsPage'
import { OpenRecordsPage } from './pages/admin/OpenRecordsPage'
import { BulkInvitePage } from './pages/admin/BulkInvitePage'

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

export default function App() {
  return (
    <>
      <AutoUpdater />
      <AppRoutes />
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
