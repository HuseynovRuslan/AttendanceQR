import { lazy, Suspense, useEffect } from 'react'
import { Navigate, Route, Routes, useLocation } from 'react-router-dom'
import { useAppUpdate } from './lib/useAppUpdate'
import { startOfflineSync } from './lib/offlineSync'
import { ProtectedRoute } from './components/ProtectedRoute'
import { AdminRoute, PanelPage } from './components/AdminRoute'
import { ImpersonationBanner } from './components/ImpersonationBanner'
import { AdminIndexRedirect } from './components/AdminIndexRedirect'
import { HomeRedirect } from './components/HomeRedirect'
import { useAuth } from './auth/AuthContext'
import { getImpersonation } from './api/client'
import { isOperatorHost } from './lib/host'
import { LoginPage } from './pages/LoginPage'
import { ActivatePage } from './pages/ActivatePage'
import { ForgotPinPage } from './pages/ForgotPinPage'
import { ScanPage } from './pages/ScanPage'
import { HistoryPage } from './pages/HistoryPage'
import { ProfilesPage } from './pages/ProfilesPage'
import { DeviceChangeRequestPage } from './pages/DeviceChangeRequestPage'
import { EmployeeLayout } from './pages/EmployeeLayout'
import { SetPinPage } from './pages/SetPinPage'
import { HomePage } from './pages/HomePage'
import { StatsPage } from './pages/StatsPage'
import { PrivacyPage } from './pages/PrivacyPage'
import { HelpChatPage } from './pages/HelpChatPage'
import { NotificationsPage } from './pages/NotificationsPage'
import { MenuPage } from './pages/MenuPage'

// The employee pages above are imported eagerly on purpose: they are the whole app for almost
// everyone who opens it, and the ScanPage component especially must render instantly (its pre-scan
// UI, GPS and device checks) rather than wait on a lazy route chunk at the gate. Its ONE heavy
// dependency, the html5-qrcode library (~110 kB gzipped), is the exception: ScanPage imports it
// dynamically and prefetches it at mount, so the QR decoder stays out of the cold-start bundle every
// employee downloads to reach Login/Home, yet is in hand by the time the camera opens (and the
// service worker caches it after the first scan).
//
// Everything below is loaded on demand. The admin panel is the only consumer of leaflet (~150 kB)
// and jspdf, and the kiosk display is the only one that needs qrcode.react — none of which an
// employee's phone has any use for. Splitting by route is what keeps those libraries out of their
// download rather than merely off their screen.
const KioskPage = lazy(() => import('./pages/KioskPage').then(m => ({ default: m.KioskPage })))
const AdminLayout = lazy(() => import('./pages/admin/AdminLayout').then(m => ({ default: m.AdminLayout })))
const DashboardPage = lazy(() => import('./pages/admin/DashboardPage').then(m => ({ default: m.DashboardPage })))
const TodayPage = lazy(() => import('./pages/admin/TodayPage').then(m => ({ default: m.TodayPage })))
const TasksPage = lazy(() => import('./pages/admin/TasksPage').then(m => ({ default: m.TasksPage })))
const GroupBoardPage = lazy(() => import('./pages/hq/GroupBoardPage').then(m => ({ default: m.GroupBoardPage })))
const PayrollPage = lazy(() => import('./pages/admin/PayrollPage').then(m => ({ default: m.PayrollPage })))
const BillingPage = lazy(() => import('./pages/admin/BillingPage').then(m => ({ default: m.BillingPage })))
const AnnouncementsPage = lazy(() => import('./pages/admin/AnnouncementsPage').then(m => ({ default: m.AnnouncementsPage })))
const BirthdaysPage = lazy(() => import('./pages/admin/BirthdaysPage').then(m => ({ default: m.BirthdaysPage })))
const VotePage = lazy(() => import('./pages/VotePage').then(m => ({ default: m.VotePage })))
const ManagerEmployeesPage = lazy(() => import('./pages/manager/ManagerEmployeesPage').then(m => ({ default: m.ManagerEmployeesPage })))
const ManagerLeavesPage = lazy(() => import('./pages/manager/ManagerLeavesPage').then(m => ({ default: m.ManagerLeavesPage })))
const TabelPage = lazy(() => import('./pages/admin/TabelPage').then(m => ({ default: m.TabelPage })))
const PositionsPage = lazy(() => import('./pages/admin/PositionsPage').then(m => ({ default: m.PositionsPage })))
const EquipmentPage = lazy(() => import('./pages/admin/EquipmentPage').then(m => ({ default: m.EquipmentPage })))
const SchedulesPage = lazy(() => import('./pages/admin/SchedulesPage').then(m => ({ default: m.SchedulesPage })))
const VoteResultsPage = lazy(() => import('./pages/admin/VoteResultsPage').then(m => ({ default: m.VoteResultsPage })))
const ReportsPage = lazy(() => import('./pages/admin/ReportsPage').then(m => ({ default: m.ReportsPage })))
const LocationsPage = lazy(() => import('./pages/admin/LocationsPage').then(m => ({ default: m.LocationsPage })))
const NonWorkingDaysPage = lazy(() => import('./pages/admin/NonWorkingDaysPage').then(m => ({ default: m.NonWorkingDaysPage })))
const LeavesPage = lazy(() => import('./pages/admin/LeavesPage').then(m => ({ default: m.LeavesPage })))
const PrintQrPage = lazy(() => import('./pages/admin/PrintQrPage').then(m => ({ default: m.PrintQrPage })))
const EmployeesPage = lazy(() => import('./pages/admin/EmployeesPage').then(m => ({ default: m.EmployeesPage })))
const EmployeeProfilePage = lazy(() => import('./pages/admin/EmployeeProfilePage').then(m => ({ default: m.EmployeeProfilePage })))
const DeviceChangesPage = lazy(() => import('./pages/admin/DeviceChangesPage').then(m => ({ default: m.DeviceChangesPage })))
const PinResetsPage = lazy(() => import('./pages/admin/PinResetsPage').then(m => ({ default: m.PinResetsPage })))
const ProblemsPage = lazy(() => import('./pages/admin/ProblemsPage').then(m => ({ default: m.ProblemsPage })))
const OpenRecordsPage = lazy(() => import('./pages/admin/OpenRecordsPage').then(m => ({ default: m.OpenRecordsPage })))
const BulkInvitePage = lazy(() => import('./pages/admin/BulkInvitePage').then(m => ({ default: m.BulkInvitePage })))
const FieldVisitsAdminPage = lazy(() => import('./pages/admin/FieldVisitsAdminPage').then(m => ({ default: m.FieldVisitsAdminPage })))
const FieldVisitsPage = lazy(() => import('./pages/FieldVisitsPage').then(m => ({ default: m.FieldVisitsPage })))
// The operator console (admin.qrlog.az) is its own chunk — only that host ever loads it.
const OperatorRoutes = lazy(() => import('./pages/operator/OperatorRoutes').then(m => ({ default: m.OperatorRoutes })))
const OperatorLoginPage = lazy(() => import('./pages/operator/OperatorLoginPage').then(m => ({ default: m.OperatorLoginPage })))

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

/** Drains any scans made offline back to the server — on load and whenever the connection returns.
 *  No-op when signed out or the queue is empty. */
function OfflineSyncer() {
  // Re-run when the SIGNED-IN employee changes. The queue only replays its owner's scans, and login
  // is an SPA state change with no remount — so with an empty dep list the one moment a waiting scan
  // becomes replayable (its owner signing in on a shared phone) was the one moment nothing drained,
  // and by the next cold open it was past the 18-hour window and discarded.
  const { employeeId } = useAuth()
  useEffect(() => startOfflineSync(), [employeeId])
  return null
}

/** Warm the QR-scanner chunk in the background at idle, after the first paint. ScanPage imports
 *  html5-qrcode dynamically (to keep it out of the cold-start bundle), so without this an employee who
 *  went offline between opening the app and their first-ever scan would find the chunk uncached and be
 *  unable to scan. requestIdleCallback keeps it off the critical path; the service worker caches it
 *  once fetched, restoring the "available offline after first app load" guarantee. */
function ScannerPrefetch() {
  useEffect(() => {
    const warm = () => void import('html5-qrcode').catch(() => {})
    const w = window as unknown as {
      requestIdleCallback?: (cb: () => void, opts?: { timeout: number }) => number
      cancelIdleCallback?: (id: number) => void
    }
    if (w.requestIdleCallback) {
      const id = w.requestIdleCallback(warm, { timeout: 4000 })
      return () => w.cancelIdleCallback?.(id)
    }
    const t = setTimeout(warm, 2500)
    return () => clearTimeout(t)
  }, [])
  return null
}

export default function App() {
  return (
    <>
      <ImpersonationBanner />
      <AutoUpdater />
      <OfflineSyncer />
      <ScannerPrefetch />
      {/* One boundary around every route: React needs a Suspense ancestor for any lazy element, and
          the eager employee routes never suspend, so they never see it. */}
      <Suspense fallback={<RouteFallback />}>
        <AppRoutes />
      </Suspense>
    </>
  )
}

function AppRoutes() {
  const { isAuthenticated } = useAuth()

  // The operator console is a wholly separate surface on admin.qrlog.az — not a screen inside any
  // company. While the operator is impersonating a tenant admin they hold a TENANT token, so fall
  // through to the normal tenant routes (that tenant's admin) instead of the operator shell.
  if (isOperatorHost()) {
    if (!isAuthenticated) return <OperatorLoginPage />
    if (!getImpersonation()) return <OperatorRoutes />
  }

  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route path="/activate" element={<ActivatePage />} />
      <Route path="/forgot-pin" element={<ForgotPinPage />} />
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

      {/* What this app takes off your phone. Its own header, so it renders outside the tab shell —
          and it was written, then never routed, so the consent screen's "Ətraflı məlumat" link had
          nowhere to go. */}
      <Route
        path="/privacy"
        element={
          <ProtectedRoute>
            <PrivacyPage />
          </ProtectedRoute>
        }
      />

      {/* AI Köməkçi — the support chat. Own header (no tab bar): a conversation wants the whole
          screen, and the fixed composer would fight the bottom tabs for the same edge. */}
      <Route
        path="/help"
        element={
          <ProtectedRoute>
            <HelpChatPage />
          </ProtectedRoute>
        }
      />

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
      {/* Field / mobile attendance for the worker (GPS + selfie, no QR). */}
      <Route
        path="/field"
        element={
          <ProtectedRoute>
            <FieldVisitsPage />
          </ProtectedRoute>
        }
      />
      <Route
        path="/vote"
        element={
          <ProtectedRoute>
            <VotePage />
          </ProtectedRoute>
        }
      />
      {/* Outside the admin layout on purpose: this is shown full-screen, often on a projector, and
          a sidebar of a single company's menu items would undercut what it is showing. */}
      <Route
        path="/hq"
        element={
          <ProtectedRoute>
            <GroupBoardPage />
          </ProtectedRoute>
        }
      />
      {/* The profile IS the Profil tab now — one screen, not two that both used the word. Kept as a
          redirect rather than deleted: the AI assistant hands this path out, and an installed app can
          still be sitting on it. */}
      <Route path="/profile" element={<Navigate to="/menu" replace />} />
      {/* Several accounts on one handset — the crew phone. Not lazy-loaded: it sits one tap from the
          scanner on a device that is used where there is no signal to fetch a chunk with. */}
      <Route
        path="/profiles"
        element={
          <ProtectedRoute>
            <ProfilesPage />
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
              <PanelPage path="dashboard">
                <DashboardPage />
              </PanelPage>
          }
        />
        {/* Live board — Admin + Manager (manager scoped to their locations server-side, like today). */}
        <Route path="today" element={<PanelPage path="today"><TodayPage /></PanelPage>} />
        {/* Shared task board — access is a server-side id allowlist, so the route stays open (admin +
            manager) and the page/API 403s anyone not on the list. */}
        <Route path="tasks" element={<PanelPage path="tasks"><TasksPage /></PanelPage>} />
        <Route path="reports" element={<PanelPage path="reports"><ReportsPage /></PanelPage>} />
        {/* Payroll — Admin only (salaries are sensitive; a manager must not see them). */}
        <Route
          path="payroll"
          element={
              <PanelPage path="payroll">
                <PayrollPage />
              </PanelPage>
          }
        />
        {/* Abunəlik — Admin only. It carries what the company pays and whether it is settled; a
            manager runs a branch, not the contract. */}
        <Route
          path="billing"
          element={
              <PanelPage path="billing">
                <BillingPage />
              </PanelPage>
          }
        />
        <Route
          path="announcements"
          element={
              <PanelPage path="announcements">
                <AnnouncementsPage />
              </PanelPage>
          }
        />
        {/* Admin + Manager — the endpoint scopes a manager to their own branch, same as Hesabat. */}
        <Route path="tabel" element={<PanelPage path="tabel"><TabelPage /></PanelPage>} />
        {/* Manager write surface — the endpoints are Manager-only + location-scoped server-side. */}
        <Route path="my-employees" element={<PanelPage path="my-employees"><ManagerEmployeesPage /></PanelPage>} />
        <Route path="my-leaves" element={<PanelPage path="my-leaves"><ManagerLeavesPage /></PanelPage>} />
        <Route
          path="positions"
          element={
              <PanelPage path="positions">
                <PositionsPage />
              </PanelPage>
          }
        />
        {/* Equipment register — Admin only: it spans every office and site, and is not a branch
            screen. Arrived from `staging` wrapped in <AdminOnly>, which main replaced with the
            access table; see lib/panelAccess. */}
        <Route
          path="equipment"
          element={
              <PanelPage path="equipment">
                <EquipmentPage />
              </PanelPage>
          }
        />
        {/* Admin + Manager. A manager's writes are scope-checked server-side: they may define a
            shift, but not edit one another branch's staff are on. */}
        <Route path="schedules" element={<PanelPage path="schedules"><SchedulesPage /></PanelPage>} />
        <Route
          path="vote"
          element={
              <PanelPage path="vote">
                <VoteResultsPage />
              </PanelPage>
          }
        />
        <Route
          path="birthdays"
          element={
              <PanelPage path="birthdays">
                <BirthdaysPage />
              </PanelPage>
          }
        />
        {/* Foto audit is HIDDEN on purpose (owner, 2026-08-09): it listed every check-in selfie, which
            is browsing staff photos rather than auditing a specific doubt. A selfie is now opened only
            from a face-flagged row ("Şəklə bax" on the today board / employee profile). The page file
            is still in the repo — restoring it is this route + the lazy import + the nav entry. The
            route is REMOVED, not just the nav link: a hidden link on a live route is not hidden. */}
        {/* Rejected-scan log — Admin + Manager (manager scoped to their locations server-side). */}
        <Route path="problems" element={<PanelPage path="problems"><ProblemsPage /></PanelPage>} />
        {/* Field visits board + assign — Admin + Manager (the endpoints gate on Admin,Manager). */}
        <Route path="field-visits" element={<PanelPage path="field-visits"><FieldVisitsAdminPage /></PanelPage>} />
        {/* Unclosed days — Admin only, since fixing a record (setting a check-out) is Admin only. */}
        <Route
          path="open-records"
          element={
              <PanelPage path="open-records">
                <OpenRecordsPage />
              </PanelPage>
          }
        />
        <Route
          path="locations"
          element={
              <PanelPage path="locations">
                <LocationsPage />
              </PanelPage>
          }
        />
        <Route
          path="non-working-days"
          element={
              <PanelPage path="non-working-days">
                <NonWorkingDaysPage />
              </PanelPage>
          }
        />
        <Route
          path="leaves"
          element={
              <PanelPage path="leaves">
                <LeavesPage />
              </PanelPage>
          }
        />
        <Route
          path="locations/:locationId/print-qr"
          element={
              <PanelPage path="locations/:locationId/print-qr">
                <PrintQrPage />
              </PanelPage>
          }
        />
        <Route
          path="employees"
          element={
              <PanelPage path="employees">
                <EmployeesPage />
              </PanelPage>
          }
        />
        <Route
          path="employees/:id"
          element={
              <PanelPage path="employees/:id">
                <EmployeeProfilePage />
              </PanelPage>
          }
        />
        <Route
          path="bulk-invite"
          element={
              <PanelPage path="bulk-invite">
                <BulkInvitePage />
              </PanelPage>
          }
        />
        {/* Company management ("Şirkətlər") moved OUT of the tenant admin to the operator console on
            admin.qrlog.az — a customer's admin panel no longer carries platform-operator screens. */}
        <Route
          path="device-changes"
          element={
              <PanelPage path="device-changes">
                <DeviceChangesPage />
              </PanelPage>
          }
        />
        <Route
          path="pin-resets"
          element={
              <PanelPage path="pin-resets">
                <PinResetsPage />
              </PanelPage>
          }
        />
      </Route>

      {/* "/" and anything unknown → login, or the role's home if already signed in. */}
      <Route path="*" element={<HomeRedirect />} />
    </Routes>
  )
}
