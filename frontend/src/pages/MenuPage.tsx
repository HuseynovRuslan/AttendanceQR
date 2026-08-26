import { useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import type { ComponentType, SVGProps } from 'react'
import {
  getMyAvatar,
  getMyDeviceStatus,
  getMyProfile,
  getMySummary,
  type MyDeviceStatus,
  type MyProfile,
} from '../api/attendance'
import type { ReportTotals } from '../api/admin'
import { getVoteStatus } from '../api/vote'
import { useAuth } from '../auth/AuthContext'
import { useFeatureEnabled } from '../branding/BrandingContext'
import { getDeviceFingerprint } from '../lib/device'
import { avatarIsStale, dropAvatar, putAvatar } from '../lib/avatar'
import { fmtDayMonth } from '../lib/format'
import { Avatar } from '../components/Avatar'
import { AvatarPickerSheet } from '../components/AvatarPickerSheet'
import { InstallAppCard } from '../components/InstallAppCard'
import { AccountSwitcherSheet } from '../components/AccountSwitcherSheet'
import { PinChangeSheet } from '../components/PinChangeSheet'
import { PushToggle } from '../components/PushToggle'
import {
  IconCamera,
  IconChart,
  IconCheck,
  IconChevronDown,
  IconClock,
  IconKey,
  IconLogout,
  IconMapPin,
  IconPhone,
  IconSend,
  IconShield,
} from '../components/icons'

// Keep in step with versionName in frontend/android/app/build.gradle — a tester who sees one number
// in the Play listing and another at the bottom of the menu has no way to tell which build they are
// actually running, which is the one thing this line exists to answer.
const APP_VERSION = '1.0'

/** First day of the current month and today, as the summary endpoint wants them. */
function thisMonth(): { from: string; to: string } {
  const now = new Date()
  const ymd = (d: Date) =>
    `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
  return { from: ymd(new Date(now.getFullYear(), now.getMonth(), 1)), to: ymd(now) }
}

/**
 * The Profil tab — the employee's own profile, and everything reachable from it.
 *
 * There used to be two screens here, and both called themselves Profil: this tab, which was a list of
 * links under an identity card, and a sub-page behind "Profil məlumatları / PIN", which was three
 * password boxes and an email address. Neither was a profile. The one screen that should answer
 * "whose phone am I in right now" named nobody, which stopped being cosmetic the moment a single
 * handset started holding a whole crew's accounts.
 *
 * They are one screen now, built the way a phone builds a profile: a face and this month's figures,
 * then the name and what they do and where, then the two things you can actually DO, and only then
 * the list of everywhere else. Changing a PIN and switching account are both sheets — they are
 * actions, not identities, and neither is worth a screen of its own.
 *
 * The picture is one the employee CHOSE, and it is not the check-in selfie. That distinction is load
 * bearing: the selfie is a face-audit baseline, shown only where there is a reason to inspect a face
 * (a0e1772, f293ca1), and putting it here would have quietly undone that. This one is picked, it is
 * compared to nothing, and it earns its place by solving a real problem — a crew phone holding thirty
 * accounts shows thirty pairs of initials, and "Məmmədov Elçin" and "Məmmədov Elvin" are both ME.
 */
export function MenuPage() {
  const { logout, role, profiles, employeeId } = useAuth()
  const assistantOn = useFeatureEnabled('assistant')
  const [profile, setProfile] = useState<MyProfile | null>(null)
  const [totals, setTotals] = useState<ReportTotals | null>(null)
  const [device, setDevice] = useState<MyDeviceStatus | null>(null)
  // Months without a ballot have no vote screen worth opening, so the row isn't offered at all.
  const [hasBallot, setHasBallot] = useState(false)
  const [switching, setSwitching] = useState(false)
  const [changingPin, setChangingPin] = useState(false)
  const [pickingAvatar, setPickingAvatar] = useState(false)
  // Bumped after the picture changes, purely to make <Avatar> re-read the cache it renders from.
  const [avatarNonce, setAvatarNonce] = useState(0)
  const navigate = useNavigate()

  useEffect(() => {
    const { from, to } = thisMonth()
    void getMyProfile().then((r) => {
      if (r.status !== 200 || !r.data || !('fullName' in r.data)) return
      const me = r.data
      setProfile(me)
      if (!employeeId) return

      // Fetch the picture only when what we hold is out of date — which, after the first time, is
      // whenever they changed it. Every other profile open costs nothing.
      if (!me.hasAvatar) {
        dropAvatar(employeeId)
        setAvatarNonce((n) => n + 1)
      } else if (avatarIsStale(employeeId, me.avatarUpdatedAtUtc)) {
        void getMyAvatar().then((a) => {
          if (a.status === 200 && a.data && 'dataUrl' in a.data) {
            putAvatar(employeeId, a.data.dataUrl, me.avatarUpdatedAtUtc)
            setAvatarNonce((n) => n + 1)
          }
        })
      }
    })
    void getMySummary(from, to).then((r) => {
      if (r.status === 200 && r.data && 'totals' in r.data) setTotals(r.data.totals)
    })
    void getMyDeviceStatus(getDeviceFingerprint()).then((r) => {
      if (r.status === 200 && r.data && 'bound' in r.data) setDevice(r.data)
    })
    void getVoteStatus().then((r) => {
      if (r.status === 200 && r.data && 'candidates' in r.data) setHasBallot(r.data.enabled)
    })
  }, [employeeId])

  const subtitle = [profile?.position, profile?.locationName].filter(Boolean).join(' · ')

  return (
    <div className="flex flex-col gap-4">
      <div className="rounded-3xl border border-slate-100 bg-white p-5 shadow-sm">
        {/* Face, then figures — side by side, the way a phone opens a profile. The figures are this
            month's, because a month is the unit everything here is paid and reported in. */}
        <div className="flex items-center gap-5">
          {/* Tap the picture to change the picture, tap the name to change WHO YOU ARE. Two controls
              that sit next to each other and mean different things — which is exactly the split a
              phone makes, and the reason the switcher was moved off the avatar and onto the name. */}
          <button
            type="button"
            onClick={() => setPickingAvatar(true)}
            className="relative shrink-0 rounded-full transition active:scale-95"
            aria-label="Profil şəklini dəyiş"
          >
            <Avatar key={avatarNonce} employeeId={employeeId} name={profile?.fullName} size={80} />
            <span className="absolute -bottom-0.5 -right-0.5 flex h-6 w-6 items-center justify-center rounded-full border-2 border-white bg-slate-700 text-white">
              <IconCamera className="h-3 w-3" />
            </span>
          </button>
          {/* Tapping the figures opens the full history, the way tapping a count opens the list it
              counts — and saves the trip back out to the tab bar. */}
          <button
            type="button"
            onClick={() => navigate('/stats')}
            className="flex flex-1 items-center justify-around rounded-2xl py-1 transition active:bg-slate-50"
          >
            <Stat value={totals?.workDays} label="gün" />
            <Stat value={totals ? Math.round(totals.totalWorkedHours) : undefined} label="saat" />
            <Stat value={totals ? Math.round(totals.overtimeHours) : undefined} label="əlavə" />
          </button>
        </div>

        {/* The name is the switcher. Who you are and how to become somebody else are the same
            question, and on a crew phone this is tapped thirty times in the ten minutes before a
            shift — a row buried among nine others is not that. */}
        <button
          type="button"
          onClick={() => setSwitching(true)}
          className="mt-4 flex w-full items-center gap-2 rounded-2xl text-left transition active:bg-slate-50"
        >
          <span className="min-w-0 flex-1">
            <span className="flex items-center gap-1.5">
              <span className="truncate text-lg font-bold">{profile?.fullName ?? '…'}</span>
              <IconChevronDown className="h-4 w-4 shrink-0 text-slate-400" />
            </span>
            {subtitle && <span className="block truncate text-sm text-slate-500">{subtitle}</span>}
            {profile?.birthDate && (
              <span className="block text-sm text-slate-400">🎂 {fmtDayMonth(profile.birthDate)}</span>
            )}
            {/* Only the reassuring state lives here. An unbound or revoked context needs an
                explanation and a button, and gets the full card below instead of a green line. */}
            {device?.bound && <span className="block text-sm text-green-700">📱 Bu cihaz bağlıdır</span>}
          </span>
          {/* The count only appears once there is something to count — on a personal phone this badge
              would be a permanent "1" that explains nothing. */}
          {profiles.length > 1 && (
            <span className="shrink-0 rounded-full bg-blue-50 px-2.5 py-1 text-xs font-bold text-blue-700">
              {profiles.length}
            </span>
          )}
        </button>

        <div className="mt-4 flex gap-2">
          <button
            type="button"
            onClick={() => setChangingPin(true)}
            className="flex flex-1 items-center justify-center gap-2 rounded-2xl bg-slate-100 py-3 font-semibold text-slate-700 transition active:bg-slate-200"
          >
            <IconKey className="h-4 w-4" />
            PIN dəyiş
          </button>
          <Link
            to="/device-change-request"
            className="flex flex-1 items-center justify-center gap-2 rounded-2xl bg-slate-100 py-3 font-semibold text-slate-700 transition active:bg-slate-200"
          >
            <IconPhone className="h-4 w-4" />
            Yeni telefon
          </Link>
        </div>
      </div>

      {switching && <AccountSwitcherSheet onClose={() => setSwitching(false)} />}
      {pickingAvatar && (
        <AvatarPickerSheet
          onClose={() => setPickingAvatar(false)}
          onChanged={() => setAvatarNonce((n) => n + 1)}
        />
      )}
      {changingPin && <PinChangeSheet onClose={() => setChangingPin(false)} />}

      {totals && totals.absentDays > 0 && (
        <div className="rounded-3xl border border-red-100 bg-red-50 p-4 text-sm text-red-800">
          Bu ay <b>{totals.absentDays} gün</b> qayıb görünür. Səhv olduğunu düşünürsünüzsə rəhbərinizə
          deyin — düzəltmək mümkündür.
        </div>
      )}

      {/* Only when there is something to do about it — the bound case is a line in the card above. */}
      {device && !device.bound && <DeviceCard device={device} />}

      <InstallAppCard />

      <div className="divide-y divide-slate-100 overflow-hidden rounded-3xl border border-slate-100 bg-white shadow-sm">
        {/* Staff who also run the panel (admin/manager) get a way back — mirror of the sidebar's
            "İşçi rejimi" link. Plain employees never see this row. */}
        {(role === 'Admin' || role === 'Manager') && (
          <MenuRow to="/admin" Icon={IconChart} label="Admin panel" />
        )}
        {/* Only for workers an admin has marked as field workers — a plain office employee never sees it. */}
        {profile?.canFieldCheckIn && <MenuRow to="/field" Icon={IconMapPin} label="Səyyar / Sahə ziyarəti" />}
        <MenuRow to="/stats" Icon={IconClock} label="Skan tarixçəsi" />
        {/* Per-tenant flag: a company that switches the assistant off must not even see the row. */}
        {assistantOn && <MenuRow to="/help" Icon={IconSend} label="Süni intellekt köməkçisi" />}
        {/* Ayın işçisi modulu hələlik deaktivdir. */}
        {false && hasBallot && <MenuRow to="/vote" Icon={IconCheck} label="Ayın işçisi — səsvermə" />}
        {/* The app photographs their face and reads their position; where that goes has to be
            reachable from inside the app, not only from the website they never visit. */}
        <MenuRow to="/privacy" Icon={IconShield} label="Məlumatlarınız / məxfilik" />
      </div>

      {/* Notifications carry the announcements and the checkout reminder, so turning them off should
          be possible but never a casual tap. */}
      <details>
        <summary className="cursor-pointer list-none py-2 text-center text-sm text-slate-400">
          Bildiriş ayarları
        </summary>
        <div className="mt-1 rounded-3xl border border-slate-100 bg-white p-5 shadow-sm">
          <PushToggle />
        </div>
      </details>

      <button
        onClick={logout}
        className="flex items-center gap-3 rounded-3xl border border-red-100 bg-white p-4 font-semibold text-red-600 shadow-sm transition active:scale-[0.99]"
      >
        <span className="flex h-9 w-9 items-center justify-center rounded-full bg-red-50">
          <IconLogout className="h-5 w-5" />
        </span>
        {/* On a shared handset "Hesabdan çıxış" alone does not say whose session is about to end. */}
        {profiles.length > 1 && profile?.fullName ? `Çıxış — ${profile.fullName}` : 'Hesabdan çıxış'}
      </button>

      {/* "AttendanceQR" was the repository's name, never the product's. It is QRLog on the poster, the
          landing page, the invoice and now the Play listing; the one place it still said otherwise was
          the line an employee reads when they scroll to the bottom of the menu. */}
      <div className="pt-2 text-center text-xs text-slate-400">QRLog · Versiya {APP_VERSION}</div>
    </div>
  )
}

function Stat({ value, label }: { value: number | undefined; label: string }) {
  return (
    <span className="flex flex-col items-center">
      {/* tabular-nums so the three columns do not shuffle sideways as the figures grow during a month */}
      <span className="text-xl font-bold tabular-nums">{value ?? '—'}</span>
      <span className="text-xs text-slate-500">{label}</span>
    </span>
  )
}

/** Safari and the installed app are separate contexts, so "am I bound?" is a question the employee
 *  otherwise answers by walking to the poster and failing. Shown only when something is wrong — the
 *  bound case is a single green line in the identity card. */
function DeviceCard({ device }: { device: MyDeviceStatus }) {
  if (device.revoked) {
    return (
      <div className="rounded-3xl border border-red-100 bg-red-50 p-4">
        <div className="flex items-center gap-3">
          <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-red-100 text-lg">📱</span>
          <div className="min-w-0">
            <div className="font-semibold text-red-800">Bu cihaz bağlanmayıb</div>
            <div className="text-sm text-red-700">
              Skan edə bilməyəcəksiniz. «Yeni telefon» düyməsi ilə müraciət göndərin.
            </div>
          </div>
        </div>
      </div>
    )
  }

  return (
    <div className="rounded-3xl border border-amber-100 bg-amber-50 p-4">
      <div className="flex items-center gap-3">
        <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-amber-100 text-lg">📱</span>
        <div className="min-w-0">
          <div className="font-semibold text-amber-900">Bu cihaz hələ bağlanmayıb</div>
          <div className="text-sm text-amber-800">
            İş yerinizdə ilk dəfə skan edəndə avtomatik bağlanacaq.
          </div>
        </div>
      </div>
    </div>
  )
}

function MenuRow({
  to,
  Icon,
  label,
}: {
  to: string
  Icon: ComponentType<SVGProps<SVGSVGElement>>
  label: string
}) {
  return (
    <Link to={to} className="flex items-center gap-3 p-4 transition active:bg-slate-50">
      <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-slate-100 text-slate-600">
        <Icon className="h-5 w-5" />
      </span>
      <span className="font-semibold">{label}</span>
    </Link>
  )
}
