import { useEffect, useState } from 'react'
import { SubPageHeader } from '../components/SubPageHeader'
import { castVote, getVoteStatus, type VoteStatus } from '../api/vote'
import { initials } from '../lib/att'

const MONTHS = [
  'Yanvar', 'Fevral', 'Mart', 'Aprel', 'May', 'İyun',
  'İyul', 'Avqust', 'Sentyabr', 'Oktyabr', 'Noyabr', 'Dekabr',
]

/** "Ayın işçisi" — one tap, one vote, secret. Deliberately a single screen: employees open this app
 *  to scan and little else, so anything longer than pick-a-name would simply not get used. */
export function VotePage() {
  const [status, setStatus] = useState<VoteStatus | null>(null)
  const [search, setSearch] = useState('')
  const [picked, setPicked] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [done, setDone] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function load() {
    const { status: s, data } = await getVoteStatus()
    if (s === 200 && data && 'candidates' in data) setStatus(data)
  }

  useEffect(() => {
    void load()
  }, [])

  async function submit() {
    if (!picked) return
    setBusy(true)
    setError(null)
    const { status: s, data } = await castVote(picked)
    setBusy(false)
    if (s === 200) {
      setDone(true)
      return
    }
    const code = data && 'error' in data ? data.error : ''
    setError(
      code === 'AlreadyVoted' ? 'Bu ay artıq səs vermisiniz.'
        : code === 'VotingClosed' ? 'Səsvermə bağlıdır.'
        : code === 'ManagersDoNotVote' ? 'Rəhbərlər səs vermir.'
        : 'Səs qeydə alınmadı, yenidən yoxlayın.',
    )
  }

  const monthName = status ? MONTHS[Number(status.period.slice(5, 7)) - 1] : ''

  return (
    <div className="min-h-screen bg-slate-50">
      <SubPageHeader title="Ayın işçisi" />
      <main className="mx-auto max-w-md p-4">
        {!status ? (
          <div className="h-40" aria-busy="true" />
        ) : done || status.hasVoted ? (
          <div className="rounded-3xl border border-green-200 bg-green-50 p-6 text-center">
            <div className="text-5xl">🏆</div>
            <div className="mt-2 text-lg font-bold text-green-900">Səsiniz qeydə alındı</div>
            <p className="mt-1 text-sm text-green-800">
              Təşəkkür edirik. Nəticə ayın sonunda elan olunacaq. Səsiniz gizlidir — kimə səs
              verdiyinizi heç kim görmür.
            </p>
          </div>
        ) : !status.enabled ? (
          // The company switched the ballot off from the admin panel.
          <div className="rounded-3xl border border-slate-200 bg-white p-6 text-center text-slate-600">
            <div className="text-4xl">🗳️</div>
            <div className="mt-2 font-bold text-slate-800">Səsvermə keçirilmir</div>
            <p className="mt-1 text-sm">Hazırda bu bölmə bağlıdır.</p>
          </div>
        ) : status.tooFewColleagues ? (
          <div className="rounded-3xl border border-slate-200 bg-white p-6 text-center text-slate-600">
            Bu filialda səsvermə keçirilmir — komanda kiçik olduğu üçün səsin gizli qalması mümkün deyil.
          </div>
        ) : !status.isOpen ? (
          <div className="rounded-3xl border border-slate-200 bg-white p-6 text-center text-slate-600">
            <div className="text-4xl">🗳️</div>
            <div className="mt-2 font-bold text-slate-800">Səsvermə hələ başlamayıb</div>
            <p className="mt-1 text-sm">
              {monthName} ayının son günlərində açılacaq. Başlayanda bildiriş göndərəcəyik.
            </p>
          </div>
        ) : (
          <>
            <div className="rounded-3xl border border-blue-100 bg-blue-50 p-4">
              <div className="font-bold text-blue-900">{monthName} ayının işçisini seçin</div>
              <p className="mt-1 text-sm text-blue-800">
                {status.locationName} üzrə bir nəfər seçin. Səsiniz <b>gizlidir</b> — kimə səs
                verdiyinizi heç kim, rəhbər də görmür. Bir dəfə səs verilir.
              </p>
            </div>

            {/* A branch can be 50+ people — scrolling to find one name is the difference between
                voting and giving up. The picked person stays in the list even while filtering. */}
            <input
              type="search"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Ad üzrə axtar…"
              className="mt-4 w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 text-base outline-none focus:border-blue-500"
            />

            <div className="mt-3 flex flex-col gap-2">
              {status.candidates
                .filter((c) => {
                  const q = search.trim().toLowerCase()
                  return !q || c.employeeId === picked || c.fullName.toLowerCase().includes(q)
                })
                .map((c) => {
                const on = picked === c.employeeId
                return (
                  <button
                    key={c.employeeId}
                    onClick={() => setPicked(c.employeeId)}
                    className={`flex items-center gap-3 rounded-2xl border-2 p-3 text-left transition ${
                      on ? 'border-blue-600 bg-blue-50' : 'border-slate-100 bg-white'
                    }`}
                  >
                    <span className="flex h-11 w-11 shrink-0 items-center justify-center rounded-full bg-slate-200 font-bold text-slate-700">
                      {initials(c.fullName)}
                    </span>
                    <span className="min-w-0 flex-1">
                      <span className="block truncate font-bold text-slate-900">{c.fullName}</span>
                      <span className="block truncate text-sm text-slate-500">
                        {c.position ? `${c.position} · ` : ''}bu ay {c.daysPresent} gün işləyib
                      </span>
                    </span>
                    <span
                      className={`h-5 w-5 shrink-0 rounded-full border-2 ${
                        on ? 'border-blue-600 bg-blue-600' : 'border-slate-300'
                      }`}
                    />
                  </button>
                )
              })}
            </div>

            {error && <div className="mt-3 rounded-xl bg-red-50 p-3 text-sm text-red-700">{error}</div>}

            <button
              onClick={() => void submit()}
              disabled={!picked || busy}
              className="mt-4 w-full rounded-2xl bg-blue-600 py-4 text-base font-bold text-white disabled:opacity-50"
            >
              {busy ? 'Göndərilir…' : 'Səs ver'}
            </button>
            <p className="mt-2 text-center text-xs text-slate-400">Səs verdikdən sonra dəyişmək olmur.</p>
          </>
        )}
      </main>
    </div>
  )
}
