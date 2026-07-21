import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import type { ComponentType, SVGProps } from 'react'
import { getMyDeviceStatus, getMyProfile, type MyDeviceStatus, type MyProfile } from '../api/attendance'
import { useAuth } from '../auth/AuthContext'
import { getDeviceFingerprint } from '../lib/device'
import { initials } from '../lib/att'
import { IconChart, IconClock, IconLogout, IconPhone, IconUser } from '../components/icons'

const APP_VERSION = '2.0.0'

export function MenuPage() {
  const { logout, email, role } = useAuth()
  const [profile, setProfile] = useState<MyProfile | null>(null)
  const [device, setDevice] = useState<MyDeviceStatus | null>(null)

  useEffect(() => {
    void getMyProfile().then((r) => {
      if (r.status === 200 && r.data && 'fullName' in r.data) setProfile(r.data)
    })
    void getMyDeviceStatus(getDeviceFingerprint()).then((r) => {
      if (r.status === 200 && r.data && 'bound' in r.data) setDevice(r.data)
    })
  }, [])

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-center gap-4 rounded-3xl border border-slate-100 bg-white p-5 shadow-sm">
        <div className="flex h-16 w-16 shrink-0 items-center justify-center rounded-full bg-blue-100 text-2xl font-bold text-blue-700">
          {initials(profile?.fullName)}
        </div>
        <div className="min-w-0">
          <div className="truncate text-lg font-bold">{profile?.fullName ?? '…'}</div>
          <div className="truncate text-sm text-slate-500">{profile?.email ?? email}</div>
          {profile?.locationName && (
            <div className="truncate text-sm text-slate-400">
              {profile.locationName}
              {profile.position ? ` · ${profile.position}` : ''}
            </div>
          )}
        </div>
      </div>

      <DeviceCard device={device} />

      <div className="divide-y divide-slate-100 overflow-hidden rounded-3xl border border-slate-100 bg-white shadow-sm">
        {/* Staff who also run the panel (admin/manager) get a way back — mirror of the sidebar's
            "İşçi rejimi" link. Plain employees never see this row. */}
        {(role === 'Admin' || role === 'Manager') && (
          <MenuRow to="/admin" Icon={IconChart} label="Admin panel" />
        )}
        <MenuRow to="/profile" Icon={IconUser} label="Profil məlumatları / PIN" />
        <MenuRow to="/stats" Icon={IconClock} label="Skan tarixçəsi" />
        <MenuRow to="/device-change-request" Icon={IconPhone} label="Yeni telefon tələbi" />
      </div>

      <button
        onClick={logout}
        className="flex items-center gap-3 rounded-3xl border border-red-100 bg-white p-4 font-semibold text-red-600 shadow-sm transition active:scale-[0.99]"
      >
        <span className="flex h-9 w-9 items-center justify-center rounded-full bg-red-50">
          <IconLogout className="h-5 w-5" />
        </span>
        Hesabdan çıxış
      </button>

      <div className="pt-2 text-center text-xs text-slate-400">AttendanceQR · Versiya {APP_VERSION}</div>
    </div>
  )
}

/** Safari and the installed app are separate contexts, so "am I bound?" is a question the employee
 *  otherwise answers by walking to the poster and failing. Three states, three different actions. */
function DeviceCard({ device }: { device: MyDeviceStatus | null }) {
  if (!device) return null

  if (device.bound) {
    return (
      <div className="rounded-3xl border border-green-100 bg-green-50 p-4">
        <div className="flex items-center gap-3">
          <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-green-100 text-lg">📱</span>
          <div className="min-w-0">
            <div className="font-semibold text-green-800">Bu cihaz bağlıdır ✅</div>
            <div className="text-sm text-green-700">
              {device.deviceLabel ?? 'Bu cihaz'} — skan edə bilərsiniz
              {device.activeDeviceCount > 1 && ` · ${device.activeDeviceCount} cihazınız var`}
            </div>
          </div>
        </div>
      </div>
    )
  }

  // Revoked is not "not yet bound": no amount of scanning brings it back, only an admin.
  if (device.revoked) {
    return (
      <div className="rounded-3xl border border-red-100 bg-red-50 p-4">
        <div className="flex items-center gap-3">
          <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-red-100 text-lg">🚫</span>
          <div className="min-w-0">
            <div className="font-semibold text-red-800">Bu cihaz ləğv edilib</div>
            <div className="text-sm text-red-700">Administrator ilə əlaqə saxlayın — skan işləməyəcək.</div>
          </div>
        </div>
      </div>
    )
  }

  return (
    <div className="rounded-3xl border border-amber-100 bg-amber-50 p-4">
      <div className="flex items-center gap-3">
        <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-amber-100 text-lg">⚠️</span>
        <div className="min-w-0">
          <div className="font-semibold text-amber-900">Bu cihaz hələ bağlanmayıb</div>
          <div className="text-sm text-amber-800">
            {device.autoBindEnabled
              ? 'İş yerində bir dəfə QR skan edin — cihaz özü bağlanacaq.'
              : 'Skan işləməyəcək. «Yeni telefon tələbi» göndərin.'}
          </div>
        </div>
      </div>
    </div>
  )
}

function MenuRow({ to, Icon, label }: { to: string; Icon: ComponentType<SVGProps<SVGSVGElement>>; label: string }) {
  return (
    <Link to={to} className="flex items-center gap-3 p-4 transition active:bg-slate-50">
      <span className="flex h-9 w-9 items-center justify-center rounded-full bg-slate-100 text-slate-600">
        <Icon className="h-5 w-5" />
      </span>
      <span className="flex-1 font-semibold">{label}</span>
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round" className="h-5 w-5 text-slate-300">
        <polyline points="9 6 15 12 9 18" />
      </svg>
    </Link>
  )
}
