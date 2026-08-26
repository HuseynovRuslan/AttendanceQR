import { useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { getMyDeviceStatus, getMyProfile, getMySummary, type MyDeviceStatus, type MyProfile } from '../api/attendance'
import type { ReportTotals } from '../api/admin'
import { getDeviceFingerprint } from '../lib/device'
import { initials } from '../lib/att'
import { fmtDayMonth } from '../lib/format'
import { SubPageHeader } from '../components/SubPageHeader'
import { PushToggle } from '../components/PushToggle'
import { PinChangeSheet } from '../components/PinChangeSheet'
import { IconKey, IconPhone } from '../components/icons'

/** First day of the current month and today, as the API wants them. */
function thisMonth(): { from: string; to: string } {
  const now = new Date()
  const ymd = (d: Date) =>
    `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
  return { from: ymd(new Date(now.getFullYear(), now.getMonth(), 1)), to: ymd(now) }
}

/**
 * The employee's own profile.
 *
 * It used to be a PIN-change form wearing the word "Profil" — three password boxes, an email address
 * and nothing else. Nothing on it said who you were, which mattered the moment one handset started
 * holding several accounts: the one screen that should answer "whose phone am I in right now" did
 * not name anybody.
 *
 * So it is built the way a phone builds a profile — a face, then the numbers that mean something to
 * this person, then who they are, and only then the things you can DO. The PIN form moved into a
 * sheet, where an action belongs; it is not an identity.
 *
 * No photograph, deliberately. The check-in selfie is shown to an ADMIN on a face-flagged row and
 * nowhere else, and the profile photo card was removed on purpose once before (a0e1772). Initials
 * carry the same recognition here and put nobody's face on a screen that is handed around a work
 * site.
 */
export function ProfilePage() {
  const [profile, setProfile] = useState<MyProfile | null>(null)
  const [totals, setTotals] = useState<ReportTotals | null>(null)
  const [device, setDevice] = useState<MyDeviceStatus | null>(null)
  const [changingPin, setChangingPin] = useState(false)
  const navigate = useNavigate()

  useEffect(() => {
    const { from, to } = thisMonth()
    void getMyProfile().then((r) => {
      if (r.status === 200 && r.data && 'fullName' in r.data) setProfile(r.data)
    })
    void getMySummary(from, to).then((r) => {
      if (r.status === 200 && r.data && 'totals' in r.data) setTotals(r.data.totals)
    })
    void getMyDeviceStatus(getDeviceFingerprint()).then((r) => {
      if (r.status === 200 && r.data && 'bound' in r.data) setDevice(r.data)
    })
  }, [])

  const subtitle = [profile?.position, profile?.locationName].filter(Boolean).join(' · ')

  return (
    <div className="min-h-screen bg-slate-50 text-slate-900">
      <SubPageHeader title={profile?.fullName ?? 'Profil'} />

      <main className="flex flex-col gap-4 p-4">
        <div className="rounded-3xl border border-slate-100 bg-white p-5 shadow-sm">
          {/* Face, then figures — side by side, the way a phone opens a profile. The numbers are this
              month's, because a month is the unit everything else here is paid and reported in. */}
          <div className="flex items-center gap-5">
            <div className="flex h-20 w-20 shrink-0 items-center justify-center rounded-full bg-blue-100 text-2xl font-bold text-blue-700">
              {initials(profile?.fullName)}
            </div>
            {/* Tapping the figures opens the full history, the way tapping a count opens the list it
                counts. Saves the trip back out to the tab bar. */}
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

          <div className="mt-4">
            <div className="text-lg font-bold">{profile?.fullName ?? '…'}</div>
            {subtitle && <div className="text-sm text-slate-500">{subtitle}</div>}
            {profile?.birthDate && (
              <div className="text-sm text-slate-400">🎂 {fmtDayMonth(profile.birthDate)}</div>
            )}
            {/* Whether this browser context is bound is the question that decides if the poster will
                work, and it belongs beside the identity rather than three screens away. */}
            {device && (
              <div className={`mt-1 text-sm ${device.bound ? 'text-green-700' : 'text-amber-700'}`}>
                {device.bound ? '📱 Bu cihaz bağlıdır' : '📱 Bu cihaz hələ bağlanmayıb'}
              </div>
            )}
          </div>

          {/* The two things you can actually do from here, as buttons rather than list rows —
              the same shape a profile puts "Edit profile" in. */}
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

        {totals && totals.absentDays > 0 && (
          <div className="rounded-3xl border border-red-100 bg-red-50 p-4 text-sm text-red-800">
            Bu ay <b>{totals.absentDays} gün</b> qayıb görünür. Səhv olduğunu düşünürsünüzsə rəhbərinizə
            deyin — düzəltmək mümkündür.
          </div>
        )}

        {/* Notifications carry the announcements and the checkout reminder, so turning them off is
            allowed but never a casual tap — it stays behind a toggle at the bottom, as before. */}
        <div className="rounded-3xl border border-slate-100 bg-white p-5 shadow-sm">
          <div className="mb-3 font-semibold">Bildirişlər</div>
          <PushToggle />
        </div>
      </main>

      {changingPin && <PinChangeSheet onClose={() => setChangingPin(false)} />}
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
