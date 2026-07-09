import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import type { ComponentType, SVGProps } from 'react'
import { getMyProfile, type MyProfile } from '../api/attendance'
import { useAuth } from '../auth/AuthContext'
import { initials } from '../lib/att'
import { IconClock, IconLogout, IconPhone, IconUser } from '../components/icons'

const APP_VERSION = '2.0.0'

export function MenuPage() {
  const { logout, email } = useAuth()
  const [profile, setProfile] = useState<MyProfile | null>(null)

  useEffect(() => {
    void getMyProfile().then((r) => {
      if (r.status === 200 && r.data && 'fullName' in r.data) setProfile(r.data)
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

      <div className="divide-y divide-slate-100 overflow-hidden rounded-3xl border border-slate-100 bg-white shadow-sm">
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
