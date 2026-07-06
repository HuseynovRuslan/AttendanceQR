import { Route, Routes } from 'react-router-dom'
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
import { AdminLayout } from './pages/admin/AdminLayout'
import { DashboardPage } from './pages/admin/DashboardPage'
import { TodayPage } from './pages/admin/TodayPage'
import { ReportsPage } from './pages/admin/ReportsPage'
import { LocationsPage } from './pages/admin/LocationsPage'
import { NonWorkingDaysPage } from './pages/admin/NonWorkingDaysPage'
import { PrintQrPage } from './pages/admin/PrintQrPage'
import { EmployeesPage } from './pages/admin/EmployeesPage'
import { DeviceChangesPage } from './pages/admin/DeviceChangesPage'

export default function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route path="/activate" element={<ActivatePage />} />
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
