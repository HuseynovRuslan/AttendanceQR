import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { getMyAttendance, getMyProfile, type AttendanceRecord, type MyProfile } from '../api/attendance'
import { EmptyCard, HistoryRow, SkeletonList } from '../components/employeeBits'
import { InstallHint } from '../components/InstallHint'
import { AnnouncementBanner } from '../components/AnnouncementBanner'
import { AwardCard } from '../components/AwardCard'
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

  // One tap fewer: on the first open of a session, if the employee hasn't checked in yet, jump
  // straight to the scanner. Once per session (a sessionStorage flag) so returning to Home to read
  // their hours doesn't bounce them back out — and never when they've already checked in.
  useEffect(() => {
    if (loading || today.kind !== 'none') return
    if (sessionStorage.getItem('attendanceqr.autoScan')) return
    sessionStorage.setItem('attendanceqr.autoScan', '1')
    navigate('/scan')
  }, [loading, today.kind, navigate])

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
      {/* Ayın işçisi modulu deaktivdir — mükafat kartı da gizlidir. */}
      {false && <AwardCard />}

      {/* Their own count, not just an auditor's list. Someone who can see the number climbing fixes
          the habit themselves, well before it has to become a conversation with a manager. */}
      {(profile?.unverifiedCheckIns ?? 0) > 0 && (
        <div className="rounded-3xl border border-amber-200 bg-amber-50 p-4">
          <div className="font-bold text-amber-900">
            ⚠️ Bu ay {profile?.unverifiedCheckIns} girişinizin şəkli təsdiqlənməyib
          </div>
          <p className="mt-1 text-sm text-amber-800">
            Giriş şəklində üzünüz görünmür. Növbəti dəfə telefonu üzünüzə tutub çəkin.
          </p>
        </div>
      )}
      <div className="flex items-center gap-4 rounded-3xl border border-slate-100 bg-white p-5 shadow-sm">
        <div className="flex h-14 w-14 shrink-0 items-center justify-center rounded-full bg-blue-100 text-xl font-bold text-blue-700">
          {initials(profile?.fullName)}
        </div>
        <div className="min-w-0">
          <div className="truncate text-xl font-bold">Salam, {firstName(profile?.fullName) || '…'} 👋</div>
          <div className="truncate text-sm text-slate-500">{profile?.locationName ?? profile?.email ?? ''}</div>
          {/* Their own hours, right where they open the app — so they know when to come and when to
              leave without asking. Only shown when a shift actually resolves for them. */}
          {profile?.shiftStart && profile?.shiftEnd && (
            <div className="mt-1 text-base font-semibold text-blue-700">
              🕐 Növbəniz: {profile.shiftStart}–{profile.shiftEnd}
            </div>
          )}
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

      <TodayCard today={today} shiftEnd={profile?.shiftEnd} onCheckOut={() => navigate('/scan')} />

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

/** When a still-open check-in is "overdue" — the shift is over and they haven't checked out. The
 *  expected end is the first occurrence of the shift-end time AFTER check-in, so an overnight shift
 *  (ends next morning) is handled too; a 30-min grace avoids nagging someone leaving on time. As a
 *  shift-agnostic backstop, anything still open past 13 hours is overdue whatever the hours say. */
function isOverdue(checkInIso: string, shiftEnd?: string | null): boolean {
  const checkIn = new Date(checkInIso)
  const hoursOpen = (Date.now() - checkIn.getTime()) / 3_600_000
  if (hoursOpen > 13) return true
  if (!shiftEnd) return false
  const [h, m] = shiftEnd.split(':').map(Number)
  if (!Number.isFinite(h) || !Number.isFinite(m)) return false
  const end = new Date(checkIn)
  end.setHours(h, m, 0, 0)
  if (end.getTime() <= checkIn.getTime()) end.setDate(end.getDate() + 1) // overnight
  return Date.now() > end.getTime() + 30 * 60_000
}

function TodayCard({ today, shiftEnd, onCheckOut }: { today: TodayState; shiftEnd?: string | null; onCheckOut: () => void }) {
  const base = 'rounded-3xl p-5 shadow-sm border'
  if (today.kind === 'none') {
    return (
      <div className={`${base} border-slate-100 bg-white`}>
        <div className="text-sm font-semibold text-slate-400">Bu gün</div>
        <div className="mt-1 text-xl font-bold">Hələ giriş etməmisiniz</div>
        <div className="mt-1 text-base text-slate-500">Giriş üçün aşağıdakı yaşıl «Giriş et» düyməsini basıb QR kodu skan edin.</div>
      </div>
    )
  }
  if (today.kind === 'in') {
    const overdue = isOverdue(today.checkIn, shiftEnd)
    // Overdue → red and insistent; otherwise green but always with a direct "Çıxış et", because a
    // forgotten check-out reads as zero hours and costs the employee that day's pay.
    return (
      <div className={`${base} ${overdue ? 'border-red-300 bg-red-50' : 'border-green-200 bg-green-50'}`}>
        <div className={`text-sm font-semibold ${overdue ? 'text-red-500' : 'text-slate-500'}`}>
          {overdue ? 'Bu gün · çıxış gözlənilir' : 'Bu gün · işdəsiniz'}
        </div>
        <div className="mt-1 text-2xl font-extrabold">Giriş {fmtTime(today.checkIn)}</div>
        <div className={`mt-1 text-sm ${overdue ? 'font-semibold text-red-700' : 'text-slate-600'}`}>
          {overdue
            ? '⚠️ Növbəniz bitib, hələ çıxış etməmisiniz — çıxışı unutmayın!'
            : `Hələ çıxış etməmisiniz.${shiftEnd ? ` Növbə bitir: ${shiftEnd}.` : ''}`}
        </div>
        <button
          onClick={onCheckOut}
          className={`mt-3 w-full rounded-2xl py-4 text-lg font-bold text-white active:scale-[.99] transition ${overdue ? 'bg-red-600' : 'bg-blue-600'}`}
        >
          Çıxış et
        </button>
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
      className={`flex h-28 flex-col items-center justify-center gap-1 rounded-3xl text-xl font-bold shadow-sm transition active:scale-[0.98] ${
        active ? `${activeCls} shadow-lg` : 'border border-slate-200 bg-white text-slate-400'
      }`}
    >
      <span className="text-4xl">{tone === 'green' ? '↙' : '↗'}</span>
      {label}
    </button>
  )
}
