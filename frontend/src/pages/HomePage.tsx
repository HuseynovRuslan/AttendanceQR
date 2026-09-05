import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { getMyAttendance, getMyProfile, getMySummary, type AttendanceRecord, type MyProfile } from '../api/attendance'
import { rememberQrless } from '../lib/qrless'
import { getMyFieldVisits, type MyFieldVisit } from '../api/fieldVisits'
import { useAuth } from '../auth/AuthContext'
import { EmptyCard, HistoryRow, SkeletonList } from '../components/employeeBits'
import { InstallHint, installNudgeActive } from '../components/InstallHint'
import { AnnouncementBanner } from '../components/AnnouncementBanner'
import { OfflineRejectBanner } from '../components/OfflineRejectBanner'
import { readRejectsFor, REJECTS_CHANGED } from '../lib/offlineRejects'
import { FieldVisitCards } from '../components/FieldVisitCards'
import { AwardCard } from '../components/AwardCard'
import { PushEnablePrompt } from '../components/PushEnablePrompt'
import { MissedCheckoutBanner } from '../components/MissedCheckoutBanner'
import { AssistantFab } from '../components/AssistantFab'
import { firstName, initials, nightShiftState, todayState, todayStr, withPendingScans, type TodayState } from '../lib/att'
import { QUEUE_CHANGED, scansFor, type QueuedScan } from '../lib/offlineQueue'
import { fmtDuration, fmtTime } from '../lib/format'
import { IconQr } from '../components/icons'
import { PhotoWarningGate } from '../components/PhotoWarningGate'

/** First day of the current month as "yyyy-MM-dd", for the month-to-date summary query. */
function firstOfMonthStr(): string {
  const n = new Date()
  return `${n.getFullYear()}-${String(n.getMonth() + 1).padStart(2, '0')}-01`
}

export function HomePage() {
  const navigate = useNavigate()
  const { employeeId } = useAuth()
  const [profile, setProfile] = useState<MyProfile | null>(null)
  const [records, setRecords] = useState<AttendanceRecord[]>([])
  const [month, setMonth] = useState<{ workDays: number; hours: number } | null>(null)
  const [fieldVisits, setFieldVisits] = useState<MyFieldVisit[]>([])
  const [loading, setLoading] = useState(true)
  // Show at most ONE nudge (install OR notifications), so the home isn't a stack of asks. Seeded
  // synchronously to avoid a first-render flash; InstallHint keeps it in sync if dismissed mid-session.
  const [installShown, setInstallShown] = useState(() => installNudgeActive())

  useEffect(() => {
    void load()
  }, [])

  async function load() {
    setLoading(true)
    const from = firstOfMonthStr()
    const to = todayStr()
    // Field visits ride along in the same load so the auto-scan gate below knows about them before it
    // fires — but a field-endpoint failure must never break the whole home, hence the .catch fallback.
    const [p, a, s, f] = await Promise.all([
      getMyProfile(),
      getMyAttendance(),
      getMySummary(from, to),
      getMyFieldVisits().catch(() => ({ status: 0, data: [] as MyFieldVisit[] })),
    ])
    setFieldVisits(f.status === 200 && Array.isArray(f.data) ? f.data : [])
    if (p.status === 200 && p.data && 'fullName' in p.data) {
      setProfile(p.data)
      // The scan screen needs the branch fact even with no signal — kept on the phone, per person.
      rememberQrless(employeeId, p.data.qrlessCheckIn === true)
    }
    if (a.status === 200 && Array.isArray(a.data)) {
      setRecords([...a.data].sort((x, y) => (x.attendanceDate < y.attendanceDate ? 1 : -1)))
    }
    // Personal month-to-date — /summary is role-scoped, so take the caller's OWN row (the only row for
    // a plain employee), same as StatsPage does.
    if (s.status === 200 && s.data && 'rows' in s.data) {
      const mine = s.data.rows.find((r) => r.employeeId === employeeId)
      if (mine) setMonth({ workDays: mine.workDays, hours: Math.round(mine.totalWorkedHours) })
    }
    setLoading(false)
  }

  // Re-fetch just the field visits after a check-in/out on the home card — no page-wide loading flip.
  async function reloadFieldVisits() {
    const r = await getMyFieldVisits().catch(() => ({ status: 0, data: [] as MyFieldVisit[] }))
    if (r.status === 200 && Array.isArray(r.data)) setFieldVisits(r.data)
  }

  /**
   * Scans still sitting on this phone, waiting for a signal.
   *
   * The day below is built from these as well as from the server, because a card that only knows
   * what the server knows told people their offline check-in had not happened — and they scanned
   * again, which the server later reads as a check-out. See withPendingScans.
   */
  const [queued, setQueued] = useState<QueuedScan[]>([])
  useEffect(() => {
    let alive = true
    const refresh = () => { void scansFor(employeeId).then((q) => { if (alive) setQueued(q) }) }
    refresh()
    window.addEventListener(QUEUE_CHANGED, refresh)
    // The drain runs on reconnect, and the queue emptying has to reach this screen too.
    window.addEventListener('online', refresh)
    return () => {
      alive = false
      window.removeEventListener(QUEUE_CHANGED, refresh)
      window.removeEventListener('online', refresh)
    }
  }, [employeeId])

  const today = withPendingScans(todayState(records), queued)
  // A night worker's shift lives on YESTERDAY's row until noon. Without this the screen tells someone
  // who has been at work since eight in the evening that they have not checked in — see nightShiftState.
  const night = nightShiftState(records, profile?.shiftStart, profile?.shiftEnd)
  const recent = records.slice(0, 3)
  // A worker with an open/assigned field visit has no QR poster to scan — don't bounce them to /scan.
  const hasActionableField = fieldVisits.some((v) => v.status === 'Assigned' || v.status === 'CheckedIn')

  // One tap fewer: on the first open of a session, if the employee hasn't checked in yet, jump
  // straight to the scanner. Once per session (a sessionStorage flag) so returning to Home to read
  // their hours doesn't bounce them back out — and never when they've already checked in.
  // Kept in state, not read inline in the effect below: the drain that writes a reject runs in
  // parallel with load(), so whichever finishes first would otherwise decide whether the employee
  // ever sees the warning.
  const [hasRejects, setHasRejects] = useState(() => readRejectsFor(employeeId).length > 0)
  useEffect(() => {
    const refresh = () => setHasRejects(readRejectsFor(employeeId).length > 0)
    refresh()
    window.addEventListener(REJECTS_CHANGED, refresh)
    return () => window.removeEventListener(REJECTS_CHANGED, refresh)
  }, [employeeId])

  useEffect(() => {
    if (loading || today.kind !== 'none' || hasActionableField) return
    // A dropped offline scan produces exactly this state — no check-in today — and jumping straight
    // to the scanner would bury the banner that explains why. Let them read it first.
    if (hasRejects) return
    if (sessionStorage.getItem('attendanceqr.autoScan')) return
    sessionStorage.setItem('attendanceqr.autoScan', '1')
    navigate('/scan')
  }, [loading, today.kind, hasActionableField, hasRejects, navigate])

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
      <AssistantFab />
      {/* The "meet the assistant" dialog is gone (owner's call, 2026-08-24): it announced itself to
          every employee once, and an announcement people did not ask for is a door in the way of the
          one thing they opened the app to do. The assistant itself is unchanged — the blue button and
          the menu row are still there for anyone who wants it. */}
      {/* Before everything, and over everything: it is the only thing on this screen somebody else
          wrote to this person by hand, and the only one that has to be acknowledged. */}
      <PhotoWarningGate />
      <InstallHint onShown={setInstallShown} />
      {/* First, because it is the only thing here that costs the employee a day's pay if unread. */}
      <OfflineRejectBanner />
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

      {/* Month-to-date, at a glance — their own numbers, no tap required, and a way into the full
          breakdown. Passive: it informs, it doesn't ask for anything. */}
      {month && (
        <button
          onClick={() => navigate('/stats')}
          className="flex items-center justify-between rounded-3xl border border-slate-100 bg-white p-4 text-left shadow-sm transition active:scale-[0.99]"
        >
          <div className="flex items-center gap-3">
            <span className="text-2xl">📊</span>
            <div>
              <div className="text-sm font-semibold text-slate-400">Bu ay</div>
              <div className="mt-0.5 text-lg font-bold">
                {month.workDays} gün · {month.hours} saat
              </div>
            </div>
          </div>
          <span className="text-sm font-semibold text-blue-600">Ətraflı ›</span>
        </button>
      )}

      {isBirthday && (
        <div className="rounded-3xl border border-pink-200 bg-gradient-to-r from-pink-50 to-amber-50 p-5 text-center shadow-sm">
          <div className="text-5xl">🎂</div>
          <div className="mt-1 text-xl font-extrabold text-pink-700">
            Ad günün mübarək, {firstName(profile?.fullName)}!
          </div>
          <div className="mt-1 text-sm text-slate-600">Bütün komanda səni təbrik edir 🎉</div>
        </div>
      )}

      {/* Second place the reminder can be switched on — self-hides once it is. Suppressed while the
          install nudge is up, so the home never stacks two asks at once. */}
      {!installShown && <PushEnablePrompt />}

      {/* Not while the night shift is still running: the "open day" the banner would complain about
          IS the shift they are standing in, and telling them they forgot to check out of it — above a
          card asking them to check out of it — is the screen arguing with itself. */}
      {!night && <MissedCheckoutBanner />}

      {/* Only for a worker who actually has field work today — renders nothing for everyone else. */}
      <FieldVisitCards visits={fieldVisits} onChanged={reloadFieldVisits} canFieldCheckIn={profile?.canFieldCheckIn} />

      <ScanHero today={today} night={night} shiftEnd={profile?.shiftEnd} qrless={profile?.qrlessCheckIn} onScan={() => navigate('/scan')} />

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
              <HistoryRow key={r.recordId === '00000000-0000-0000-0000-000000000000' ? r.attendanceDate : r.recordId} r={r} />
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

/** The day's primary action AND its status, in one hero — the scan lives here now that it's out of the
 *  bottom bar. Context-aware: green "Giriş et" before check-in, blue "Çıxış et" while at work (red and
 *  insistent once the shift is over — a forgotten check-out reads as zero hours), a calm summary once
 *  the day is done. The whole card is the tap target. */
function ScanHero({ today, night, shiftEnd, qrless, onScan }: {
  today: TodayState
  night: { kind: 'night'; checkIn: string } | null
  shiftEnd?: string | null
  /** No poster at this branch — the same tap, but the words must not say "scan". */
  qrless?: boolean
  onScan: () => void
}) {
  const heroBtn = 'relative w-full overflow-hidden rounded-3xl p-6 text-left text-white shadow-lg transition active:scale-[.99]'
  // The call to action, in the branch's own terms: at a poster it is a scan; without one it is a
  // selfie and a position, and saying "skan" there sends somebody looking for a code that does not exist.
  const cta = (scanWord: string, qrlessWord: string) =>
    qrless ? <>📍 {qrlessWord}</> : <><IconQr className="h-5 w-5" /> {scanWord}</>

  // Checked first: for these ten hours the person IS at work, and the row that says so is yesterday's.
  // Only ever set when the server would in fact treat the next scan as that night's check-out.
  if (night && today.kind === 'none') {
    const over = isOverdue(night.checkIn, shiftEnd)
    return (
      <button
        onClick={onScan}
        className={`${heroBtn} ${over ? 'bg-gradient-to-br from-red-500 to-red-700 shadow-red-600/25' : 'bg-gradient-to-br from-indigo-600 to-violet-700 shadow-indigo-600/25'}`}
      >
        <IconQr className="pointer-events-none absolute -right-3 -top-3 h-24 w-24 opacity-15" />
        <div className="text-xs font-bold uppercase tracking-wider opacity-85">
          🌙 Gecə növbəsi{over ? ' · bitib' : ''}
        </div>
        <div className="mt-1 text-3xl font-extrabold">Çıxış et</div>
        <div className="mt-1 text-sm opacity-90">
          Dünən axşam {fmtTime(night.checkIn)}-də giriş etmisiniz
          {over ? ' · çıxışı unutmayın!' : shiftEnd ? ` · Növbə bitir ${shiftEnd}` : ''}
        </div>
        <span className="mt-4 inline-flex items-center gap-2 rounded-xl bg-white/20 px-4 py-2.5 text-base font-bold">
          {cta('Çıxış üçün skan et', 'Çıxış üçün toxunun')}
        </span>
      </button>
    )
  }

  if (today.kind === 'none') {
    return (
      <button onClick={onScan} className={`${heroBtn} bg-gradient-to-br from-green-500 to-emerald-600 shadow-green-600/25`}>
        <IconQr className="pointer-events-none absolute -right-3 -top-3 h-24 w-24 opacity-15" />
        <div className="text-xs font-bold uppercase tracking-wider opacity-85">Bu gün</div>
        <div className="mt-1 text-3xl font-extrabold">Giriş et</div>
        <div className="mt-1 text-sm opacity-90">Hələ giriş etməmisiniz</div>
        <span className="mt-4 inline-flex items-center gap-2 rounded-xl bg-white/20 px-4 py-2.5 text-base font-bold">
          {cta('Skan üçün toxunun', 'Üz və GPS ilə giriş')}
        </span>
      </button>
    )
  }

  if (today.kind === 'in') {
    const overdue = isOverdue(today.checkIn, shiftEnd)
    return (
      <button
        onClick={onScan}
        className={`${heroBtn} ${overdue ? 'bg-gradient-to-br from-red-500 to-red-700 shadow-red-600/25' : 'bg-gradient-to-br from-blue-600 to-indigo-600 shadow-blue-600/25'}`}
      >
        <IconQr className="pointer-events-none absolute -right-3 -top-3 h-24 w-24 opacity-15" />
        <div className="text-xs font-bold uppercase tracking-wider opacity-85">{overdue ? 'Növbəniz bitib' : 'İşdəsiniz'}</div>
        <div className="mt-1 text-3xl font-extrabold">Çıxış et</div>
        <div className="mt-1 text-sm opacity-90">
          Giriş {fmtTime(today.checkIn)}
          {overdue ? ' · çıxışı unutmayın!' : shiftEnd ? ` · Növbə bitir ${shiftEnd}` : ''}
        </div>
        {/* Honest about where the record is. The card must not look identical to a synced day — the
            admin's board will not show this person yet, and the employee should not be surprised. */}
        {today.pending && (
          <div className="mt-2 inline-flex items-center gap-1.5 rounded-lg bg-white/20 px-2.5 py-1 text-xs font-bold">
            📴 Göndərilməyi gözləyir
          </div>
        )}
        <span className="mt-4 inline-flex items-center gap-2 rounded-xl bg-white/20 px-4 py-2.5 text-base font-bold">
          {cta('Çıxış üçün skan et', 'Çıxış üçün toxunun')}
        </span>
      </button>
    )
  }

  return (
    <div className="rounded-3xl border border-blue-200 bg-blue-50 p-6">
      <div className="text-xs font-bold uppercase tracking-wider text-slate-500">Bu gün · tamamlandı ✓</div>
      <div className="mt-1 text-2xl font-extrabold">
        {fmtTime(today.checkIn)} – {fmtTime(today.checkOut)}
      </div>
      <div className="mt-1 text-sm text-slate-600">{fmtDuration(today.checkIn, today.checkOut)} işlədiniz.</div>
      {today.pending && (
        <div className="mt-2 inline-flex items-center gap-1.5 rounded-lg bg-slate-200 px-2.5 py-1 text-xs font-bold text-slate-600">
          📴 Göndərilməyi gözləyir
        </div>
      )}
    </div>
  )
}
