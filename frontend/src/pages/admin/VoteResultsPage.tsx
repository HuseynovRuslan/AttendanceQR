import { useEffect, useState } from 'react'
import { EmployeeLink } from '../../components/EmployeeLink'
import { getVoteResults, type VoteResults } from '../../api/vote'
import { VoteSettingsCard } from './VoteSettingsCard'

const MONTHS = [
  'Yanvar', 'Fevral', 'Mart', 'Aprel', 'May', 'İyun',
  'İyul', 'Avqust', 'Sentyabr', 'Oktyabr', 'Noyabr', 'Dekabr',
]

function periodOf(offsetMonths: number): string {
  const d = new Date()
  d.setDate(1)
  d.setMonth(d.getMonth() + offsetMonths)
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-01`
}

/** Branch-by-branch ballot results. Admins see them live (they have to chase turnout); employees
 *  never do until the month closes. */
export function VoteResultsPage() {
  const [offset, setOffset] = useState(0)
  const [data, setData] = useState<VoteResults | null>(null)
  const [loaded, setLoaded] = useState(false)

  useEffect(() => {
    setLoaded(false)
    void getVoteResults(periodOf(offset)).then(({ status, data }) => {
      if (status === 200 && data && 'branches' in data) setData(data)
      setLoaded(true)
    })
  }, [offset])

  const d = new Date()
  d.setDate(1)
  d.setMonth(d.getMonth() + offset)
  const label = `${MONTHS[d.getMonth()]} ${d.getFullYear()}`
  const winnerFor = (locationId: string) => data?.winners?.find((w) => w.locationId === locationId)

  return (
    <div>
      <VoteSettingsCard />

      <div className="chip-row">
        <span className={`chip${offset === 0 ? ' active' : ''}`} onClick={() => setOffset(0)}>Bu ay</span>
        <span className={`chip${offset === -1 ? ' active' : ''}`} onClick={() => setOffset(-1)}>Keçən ay</span>
        <span className={`chip${offset === -2 ? ' active' : ''}`} onClick={() => setOffset(-2)}>2 ay əvvəl</span>
      </div>

      <div className="card card-pad" style={{ marginBottom: 16 }}>
        <div className="card-title">{label} — səsvermə</div>
        <div className="muted" style={{ fontSize: 13, marginTop: -10 }}>
          {data?.open
            ? `Səsvermə davam edir · indiyə qədər ${data.votesCast} səs verilib`
            : `Səsvermə bağlıdır · cəmi ${data?.votesCast ?? 0} səs`}
        </div>
        {data?.open && (
          <div className="fb fb-info" style={{ marginTop: 12 }}>
            <span>
              Bu nəticələri yalnız siz görürsünüz — işçilər səsvermə bağlanana qədər heç bir sıralama
              görmür (yoxsa son gün sürü effekti yaranır).
            </span>
          </div>
        )}
      </div>

      {loaded && (!data || data.branches.length === 0) && (
        <div className="card card-pad muted" style={{ textAlign: 'center' }}>
          Bu ay üçün hələ səs verilməyib.
        </div>
      )}

      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(320px, 1fr))', gap: 16 }}>
        {data?.branches.map((b) => {
          const winner = winnerFor(b.locationId)
          const total = b.results.reduce((s, r) => s + r.votes, 0) || 1
          return (
            <div className="card card-pad" key={b.locationId}>
              <div className="card-title">{b.locationName}</div>
              <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
                {b.results.map((r, i) => {
                  const isWinner = winner ? winner.employeeId === r.employeeId : i === 0 && !data.open
                  const pct = Math.round((r.votes / total) * 100)
                  return (
                    <div key={r.employeeId}>
                      <div style={{ display: 'flex', alignItems: 'baseline', justifyContent: 'space-between', marginBottom: 5 }}>
                        <div style={{ fontWeight: 700, fontSize: 13, color: 'var(--c900)' }}>
                          {isWinner && '🏆 '}
                          <EmployeeLink id={r.employeeId} name={r.fullName} />
                        </div>
                        <div style={{ fontSize: 13, fontWeight: 800, color: isWinner ? 'var(--leaf-d)' : 'var(--c600)' }}>
                          {r.votes} səs
                        </div>
                      </div>
                      <div className="dash-bar">
                        <i style={{ width: `${pct}%`, background: isWinner ? 'var(--leaf)' : 'var(--c300)' }} />
                      </div>
                    </div>
                  )
                })}
              </div>
            </div>
          )
        })}
      </div>
    </div>
  )
}
