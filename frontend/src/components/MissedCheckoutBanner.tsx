import { useEffect, useState } from 'react'
import {
  getMissedCheckoutStatus,
  submitMissedCheckout,
  type MissedCheckoutStatusResp,
} from '../api/attendance'

// Preset reasons so a non-technical employee taps instead of typing. "Başqa" opens a short free text.
const REASONS = ['Yadımdan çıxdı', 'Tələsirdim', 'Telefon/internet problemi', 'Skan işləmədi', 'Başqa']
// Tap-only time: an hour + a minute button, no native clock-face picker (which older staff found
// confusing). The hour choices are built from the check-in hour to end of day, so an early leaver and
// a late one are both covered and pre-check-in (invalid) hours never appear.
const MINUTES = [0, 15, 30, 45]
const pad = (n: number) => String(n).padStart(2, '0')

const AZ_MONTHS = [
  'yanvar', 'fevral', 'mart', 'aprel', 'may', 'iyun',
  'iyul', 'avqust', 'sentyabr', 'oktyabr', 'noyabr', 'dekabr',
]

function fmtDate(dateOnly: string): string {
  const d = new Date(`${dateOnly}T00:00:00`)
  return `${d.getDate()} ${AZ_MONTHS[d.getMonth()] ?? ''}`
}

/**
 * Home-screen nudge for a day the employee forgot to scan out. They report the time they actually
 * left (preset reason + time picker); it becomes a request an admin/manager approves. The count of
 * this month's reports is shown back to them on purpose — this path is a safety net, not a habit.
 */
export function MissedCheckoutBanner({ onReported }: { onReported?: () => void } = {}) {
  const [status, setStatus] = useState<MissedCheckoutStatusResp | null>(null)
  const [open, setOpen] = useState(false)
  const [hour, setHour] = useState(18)
  const [minute, setMinute] = useState(0)
  const [reason, setReason] = useState('')
  const [customReason, setCustomReason] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [sent, setSent] = useState(false)

  useEffect(() => {
    void getMissedCheckoutStatus().then((r) => {
      if (r.status === 200 && r.data && 'openDay' in r.data) setStatus(r.data)
    })
  }, [])

  if (!status?.openDay || sent) return null
  const { openDay, monthlyCount, limit, pending } = status

  // Already awaiting approval — inform, no action.
  if (pending) {
    return (
      <div className="rounded-3xl border border-blue-100 bg-blue-50 p-4">
        <div className="font-semibold text-blue-800">Çıxış tələbiniz göndərilib ⏳</div>
        <div className="mt-1 text-sm text-blue-700">
          {fmtDate(openDay.attendanceDate)} üçün admin təsdiqini gözləyir.
        </div>
      </div>
    )
  }

  // Cap reached — the self-report path is closed for this month.
  if (monthlyCount >= limit) {
    return (
      <div className="rounded-3xl border border-red-100 bg-red-50 p-4">
        <div className="font-semibold text-red-800">Çox tez-tez çıxış unudulur</div>
        <div className="mt-1 text-sm text-red-700">
          Bu ay {monthlyCount} dəfə. {fmtDate(openDay.attendanceDate)} günü üçün admin ilə əlaqə saxlayın.
        </div>
      </div>
    )
  }

  // Valid checkout hours: from the (local) check-in hour to 23. selHour keeps the readout/submit inside
  // that range even if the default (18) is earlier than a late check-in.
  const checkInHour = new Date(openDay.checkInAtUtc).getHours()
  const hours = Array.from({ length: 24 - checkInHour }, (_, i) => checkInHour + i)
  const selHour = hours.includes(hour) ? hour : hours[0]

  async function submit() {
    const finalReason = (reason === 'Başqa' ? customReason : reason).trim()
    if (!finalReason) {
      setError('Səbəb seçin')
      return
    }
    // The employee picks a wall-clock time; combine it with the day and let the phone's timezone give
    // the UTC instant the server stores.
    const dt = new Date(`${openDay.attendanceDate}T${pad(selHour)}:${pad(minute)}:00`)
    if (Number.isNaN(dt.getTime())) {
      setError('Saat düzgün deyil')
      return
    }
    setBusy(true)
    setError(null)
    const r = await submitMissedCheckout(openDay.recordId, dt.toISOString(), finalReason)
    setBusy(false)
    if (r.status === 201) {
      setSent(true)
      setOpen(false)
      onReported?.()
      return
    }
    const code = r.data && 'error' in r.data ? r.data.error : ''
    setError(
      code === 'MonthlyLimitReached'
        ? 'Bu ay limit dolub — admin ilə əlaqə saxlayın'
        : code === 'CheckOutBeforeCheckIn'
          ? 'Çıxış vaxtı girişdən sonra olmalıdır'
          : code === 'CheckOutInFuture'
            ? 'Çıxış vaxtı gələcəkdə ola bilməz'
            : code === 'AlreadyRequested'
              ? 'Bu gün üçün tələb artıq göndərilib'
              : 'Göndərilmədi, yenidən cəhd edin',
    )
  }

  return (
    <>
      <div className="rounded-3xl border border-amber-200 bg-amber-50 p-4">
        <div className="flex items-center gap-3">
          <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-amber-100 text-lg">⏰</span>
          <div className="min-w-0 flex-1">
            <div className="font-semibold text-amber-900">Dünənki çıxış qeydə alınmayıb</div>
            <div className="text-sm text-amber-800">
              {fmtDate(openDay.attendanceDate)} — çıxış skan etməmisiniz
              {monthlyCount > 0 && ` · bu ay ${monthlyCount}/${limit} dəfə`}
            </div>
          </div>
        </div>
        <button
          onClick={() => setOpen(true)}
          className="mt-3 w-full rounded-2xl bg-amber-500 py-3 font-bold text-white transition active:scale-[0.99]"
        >
          Çıxış vaxtını bildir
        </button>
      </div>

      {open && (
        <div className="fixed inset-0 z-50 flex items-end justify-center bg-black/40 p-0 sm:items-center sm:p-4">
          <div className="max-h-[92vh] w-full max-w-sm overflow-y-auto rounded-t-3xl bg-white p-5 shadow-xl sm:rounded-3xl">
            <h2 className="text-center text-lg font-bold">Çıxış vaxtını bildir</h2>
            <p className="mt-1 text-center text-sm text-slate-500">
              {fmtDate(openDay.attendanceDate)} günü neçədə getdiniz?
            </p>

            {error && (
              <div className="mt-3 rounded-lg bg-red-50 p-2.5 text-center text-sm font-medium text-red-700">{error}</div>
            )}

            <div className="mt-4 rounded-2xl bg-blue-50 py-3 text-center text-3xl font-extrabold tabular-nums text-blue-700">
              {pad(selHour)}:{pad(minute)}
            </div>

            <div className="mt-3 text-sm font-semibold text-slate-600">Saat</div>
            <div className="mt-1 grid grid-cols-6 gap-2">
              {hours.map((h) => (
                <button
                  key={h}
                  onClick={() => setHour(h)}
                  className={`rounded-lg py-2.5 text-base font-bold transition ${
                    selHour === h ? 'bg-blue-600 text-white' : 'bg-slate-100 text-slate-700'
                  }`}
                >
                  {h}
                </button>
              ))}
            </div>

            <div className="mt-3 text-sm font-semibold text-slate-600">Dəqiqə</div>
            <div className="mt-1 grid grid-cols-4 gap-2">
              {MINUTES.map((m) => (
                <button
                  key={m}
                  onClick={() => setMinute(m)}
                  className={`rounded-lg py-2.5 text-base font-bold transition ${
                    minute === m ? 'bg-blue-600 text-white' : 'bg-slate-100 text-slate-700'
                  }`}
                >
                  {pad(m)}
                </button>
              ))}
            </div>

            <label className="mt-4 block text-sm font-semibold text-slate-600">Səbəb</label>
            <div className="mt-1 flex flex-wrap gap-2">
              {REASONS.map((r) => (
                <button
                  key={r}
                  onClick={() => setReason(r)}
                  className={`rounded-full px-3 py-2 text-sm font-semibold transition ${
                    reason === r ? 'bg-blue-600 text-white' : 'bg-slate-100 text-slate-700'
                  }`}
                >
                  {r}
                </button>
              ))}
            </div>
            {reason === 'Başqa' && (
              <input
                type="text"
                value={customReason}
                maxLength={200}
                placeholder="Səbəbi yazın"
                onChange={(e) => setCustomReason(e.target.value)}
                className="mt-2 w-full rounded-xl border border-slate-200 bg-white px-3 py-2.5 focus:border-blue-500 focus:outline-none focus:ring-2 focus:ring-blue-200"
              />
            )}

            <div className="mt-5 flex gap-2">
              <button
                onClick={() => setOpen(false)}
                className="flex-1 rounded-2xl bg-slate-100 py-3 font-semibold text-slate-600"
              >
                Ləğv et
              </button>
              <button
                onClick={submit}
                disabled={busy}
                className="flex-1 rounded-2xl bg-blue-600 py-3 font-bold text-white transition disabled:opacity-50"
              >
                {busy ? 'Göndərilir…' : 'Göndər'}
              </button>
            </div>
          </div>
        </div>
      )}
    </>
  )
}
