import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { getMyAttendance, getMyProfile, type AttendanceRecord, type MyProfile } from '../api/attendance'
import { EmptyCard, HistoryRow, SkeletonList } from '../components/employeeBits'
import { InstallHint } from '../components/InstallHint'
import { AnnouncementBanner } from '../components/AnnouncementBanner'
import { PushEnablePrompt } from '../components/PushEnablePrompt'
import { MissedCheckoutBanner } from '../components/MissedCheckoutBanner'
import { firstName, initials, todayState, type TodayState } from '../lib/att'
import { fmtDuration, fmtTime } from '../lib/format'

export function HomePage() {
  const navigate = useNavigate()
  const [profile, setProfile] = useState<MyProfile | null>(null)
  const [records, setRecords] = useState<AttendanceRecord[]>([])
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    void load()
  }, [])

  async function load() {
    setLoading(true)
    const [p, a] = await Promise.all([getMyProfile(), getMyAttendance()])
    if (p.status === 200 && p.data && 'fullName' in p.data) setProfile(p.data)
    if (a.status === 200 && Array.isArray(a.data)) {
      setRecords([...a.data].sort((x, y) => (x.attendanceDate < y.attendanceDate ? 1 : -1)))
    }
    setLoading(false)
  }

  const today = todayState(records)
  const recent = records.slice(0, 3)

  // Today is the employee's birthday? Compare day + month (any year) in the device's local date.
  const isBirthday = (() => {
    if (!profile?.birthDate) return false
    const parts = profile.birthDate.split('-')
    if (parts.length !== 3) return false
    const now = new Date()
    return now.getMonth() + 1 === Number(parts[1]) && now.getDate() === Number(parts[2])
  })()

  return (
    <div className="flex flex-col gap-4">
      <InstallHint />
      <AnnouncementBanner />
      <div className="flex items-center gap-4 rounded-3xl border border-slate-100 bg-white p-5 shadow-sm">
        <div className="flex h-14 w-14 shrink-0 items-center justify-center rounded-full bg-blue-100 text-xl font-bold text-blue-700">
          {initials(profile?.fullName)}
        </div>
        <div className="min-w-0">
          <div className="truncate text-lg font-bold">Salam, {firstName(profile?.fullName) || '…'} 👋</div>
          <div className="truncate text-sm text-slate-500">{profile?.locationName ?? profile?.email ?? ''}</div>
        </div>
      </div>

      {isBirthday && (
        <div className="rounded-3xl border border-pink-200 bg-gradient-to-r from-pink-50 to-amber-50 p-5 text-center shadow-sm">
          <div className="text-5xl">🎂</div>
          <div className="mt-1 text-xl font-extrabold text-pink-700">
            Ad günün mübarək, {firstName(profile?.fullName)}!
          </div>
          <div className="mt-1 text-sm text-slate-600">Bütün komanda səni təbrik edir 🎉</div>
        </div>
      )}

      {/* Second place the reminder can be switched on — self-hides once it is. */}
      <PushEnablePrompt />

      <MissedCheckoutBanner />

      <TodayCard today={today} />

      <div className="grid grid-cols-2 gap-3">
        <ActionButton tone="green" label="Giriş et" active={today.kind === 'none'} onClick={() => navigate('/scan')} />
        <ActionButton tone="blue" label="Çıxış et" active={today.kind === 'in'} onClick={() => navigate('/scan')} />
      </div>

      <div>
        <div className="mb-2 flex items-center justify-between px-1">
          <h2 className="font-bold">Son davamiyyət</h2>
          <button onClick={() => navigate('/stats')} className="text-sm font-semibold text-blue-600">
            Hamısına bax
          </button>
        </div>
        {loading ? (
          <SkeletonList />
        ) : recent.length === 0 ? (
          <EmptyCard text="Hələ qeyd yoxdur" />
        ) : (
          <div className="flex flex-col gap-2">
            {recent.map((r) => (
              <HistoryRow key={r.recordId} r={r} />
            ))}
          </div>
        )}
      </div>
    </div>
  )
}

function TodayCard({ today }: { today: TodayState }) {
  const base = 'rounded-3xl p-5 shadow-sm border'
  if (today.kind === 'none') {
    return (
      <div className={`${base} border-slate-100 bg-white`}>
        <div className="text-sm font-semibold text-slate-400">Bu gün</div>
        <div className="mt-1 text-lg font-bold">Hələ giriş etməmisiniz</div>
        <div className="mt-1 text-sm text-slate-500">Giriş üçün aşağıdakı mavi düyməni basıb QR skan edin.</div>
      </div>
    )
  }
  if (today.kind === 'in') {
    return (
      <div className={`${base} border-green-200 bg-green-50`}>
        <div className="text-sm font-semibold text-slate-500">Bu gün · işdəsiniz</div>
        <div className="mt-1 text-2xl font-extrabold">Giriş {fmtTime(today.checkIn)}</div>
        <div className="mt-1 text-sm text-slate-600">Hələ çıxış etməmisiniz.</div>
      </div>
    )
  }
  return (
    <div className={`${base} border-blue-200 bg-blue-50`}>
      <div className="text-sm font-semibold text-slate-500">Bu gün · tamamlandı ✓</div>
      <div className="mt-1 text-2xl font-extrabold">
        {fmtTime(today.checkIn)} – {fmtTime(today.checkOut)}
      </div>
      <div className="mt-1 text-sm text-slate-600">{fmtDuration(today.checkIn, today.checkOut)} işlədiniz.</div>
    </div>
  )
}

function ActionButton({
  tone,
  label,
  active,
  onClick,
}: {
  tone: 'green' | 'blue'
  label: string
  active: boolean
  onClick: () => void
}) {
  const activeCls = tone === 'green' ? 'bg-green-500 text-white shadow-green-500/30' : 'bg-blue-600 text-white shadow-blue-600/30'
  return (
    <button
      onClick={onClick}
      className={`flex h-24 flex-col items-center justify-center gap-1 rounded-3xl text-lg font-bold shadow-sm transition active:scale-[0.98] ${
        active ? `${activeCls} shadow-lg` : 'border border-slate-200 bg-white text-slate-400'
      }`}
    >
      <span className="text-3xl">{tone === 'green' ? '↙' : '↗'}</span>
      {label}
    </button>
  )
}
