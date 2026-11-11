import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { LONG_WAIT_HOURS, TYPE_LABEL, branchesInScope, hoursWaiting, isLongWait, visibleRequests } from './model'
import { RequestSheet } from './RequestSheet'
import { useProto, useScreenLoad } from './store'
import { EmptyState, ErrorState, Skeleton, employeeLabel, personName, waited, when } from './ui'

/**
 * An Operator's morning: what is waiting and for how long, how the branches stand, and what changed lately.
 * The queue is the point of the page — oldest first, its age drawn, the over-a-day ones marked.
 */
export function OperatorDashboard() {
  const { world: w, persona, now } = useProto()
  const load = useScreenLoad(`operator:${persona.id}`)
  const navigate = useNavigate()
  const [acting, setActing] = useState(false)
  const structure = w.structures.find((s) => s.id === persona.structureId)
  const branches = branchesInScope(w, persona)
  const requests = visibleRequests(w, persona)
  const pending = requests.filter((r) => r.status === 'pending').sort((a, b) => a.createdAt.localeCompare(b.createdAt))
  const long = pending.filter((r) => isLongWait(r, now))
  const today = new Date(now.getTime() + 4 * 3_600_000).toISOString().slice(0, 10)
  const decidedToday = requests.filter((r) => r.decidedAt && new Date(new Date(r.decidedAt).getTime() + 4 * 3_600_000).toISOString().slice(0, 10) === today)
  const keeperless = branches.filter((b) => !b.timekeeperId)
  const events = requests
    // A direct act is one line («birbaşa tətbiq etdi»), not «göndərdi» and then «tətbiq etdi».
    .flatMap((r) => r.events.filter((e) => e.kind !== 'applied' && !(r.direct && e.kind === 'created')).map((e) => ({ r, e })))
    .sort((a, b) => b.e.at.localeCompare(a.e.at))
    .slice(0, 8)

  if (load.state === 'error') return <div className="apx-panel"><ErrorState what="Operator paneli" onRetry={load.retry} /></div>

  return (
    <>
      <div className="apx-head" style={{ marginTop: -8 }}>
        <p style={{ margin: 0 }}>{structure?.name} strukturu: {branches.length} filial, {w.employees.filter((e) => branches.some((b) => b.id === e.branchId) && e.active).length} aktiv işçi.</p>
        <button type="button" className="apx-btn apx-btn-go" onClick={() => setActing(true)}>Yeni əməliyyat</button>
      </div>

      {load.state === 'loading' ? <div className="apx-panel"><Skeleton rows={3} /></div> : (
        <section className="apx-strip" aria-label="Qısa icmal">
          <div className="apx-fig apx-fig-wait"><span className="apx-fig-n">{pending.length}</span><span className="apx-fig-l">qərarınızı gözləyir</span></div>
          <div className="apx-fig apx-fig-long"><span className="apx-fig-n">{long.length}</span><span className="apx-fig-l">{LONG_WAIT_HOURS} saatdan çox gözləyir</span></div>
          <div className="apx-fig"><span className="apx-fig-n">{decidedToday.length}</span><span className="apx-fig-l">bu gün qərar verilib</span></div>
          <div className="apx-fig"><span className="apx-fig-n">{keeperless.length}</span><span className="apx-fig-l">filialda tabelçi yoxdur</span></div>
        </section>
      )}

      <div className="apx-dash">
        <section className="apx-panel" aria-label="Gözləyən müraciətlər">
          <div className="apx-panel-h">
            <h2>Növbə</h2>
            <Link to="../muracietler" style={{ fontSize: 13, fontWeight: 700, color: 'var(--leaf-d)' }}>Hamısı</Link>
          </div>
          {load.state === 'loading' ? <Skeleton /> : pending.length === 0 ? (
            <EmptyState title="Növbə boşdur" text="Tabelçilərin yeni müraciətləri burada, ən köhnəsi yuxarıda görünəcək." />
          ) : (
            <div className="apx-queue">
              {pending.slice(0, 7).map((r) => {
                const h = hoursWaiting(r, now)
                const isLong = isLongWait(r, now)
                return (
                  <button key={r.id} type="button" className="apx-q" onClick={() => navigate(`../muracietler?id=${r.id}`)}>
                    <span className="apx-q-t">{TYPE_LABEL[r.type]}</span>
                    <span className={`apx-q-age${isLong ? ' long' : ''}`}>{waited(r.createdAt, now)}</span>
                    <span className="apx-q-s">
                      {employeeLabel(w, r)}, {w.branches.find((b) => b.id === r.branchId)?.name}
                    </span>
                    <span className="apx-q-s" style={{ textAlign: 'right' }}>{personName(w, r.createdById)}</span>
                    <span className="apx-age" aria-hidden>
                      <i className={isLong ? 'long' : ''} style={{ width: `${Math.min(100, (h / (LONG_WAIT_HOURS * 1.5)) * 100)}%` }} />
                    </span>
                  </button>
                )
              })}
            </div>
          )}
        </section>

        <section className="apx-panel" aria-label="Son əməliyyatlar">
          <div className="apx-panel-h"><h2>Son əməliyyatlar</h2></div>
          {load.state === 'loading' ? <Skeleton /> : (
            <div className="apx-feed">
              {events.map(({ r, e }) => (
                <div key={`${r.id}-${e.kind}-${e.at}`} className="apx-feed-i">
                  <span className={`apx-feed-dot ${e.kind === 'approved' || e.kind === 'direct' ? 'ok' : e.kind === 'rejected' ? 'bad' : e.kind === 'created' ? 'wait' : ''}`} aria-hidden />
                  <span className="apx-feed-x">
                    <b>{personName(w, e.actorId)}</b>{' '}
                    {e.kind === 'created' ? 'göndərdi' : e.kind === 'approved' ? 'təsdiqlədi' : e.kind === 'rejected' ? 'rədd etdi'
                      : e.kind === 'direct' ? 'birbaşa tətbiq etdi' : 'ləğv etdi'}
                    {': '}{TYPE_LABEL[r.type].toLowerCase()}, {employeeLabel(w, r)}
                  </span>
                  <span className="apx-feed-t">{when(e.at)}</span>
                </div>
              ))}
            </div>
          )}
        </section>
      </div>

      <section className="apx-panel" aria-label="Filiallar">
        <div className="apx-panel-h"><h2>Filiallar</h2><span>{branches.length} filial</span></div>
        {load.state === 'loading' ? <Skeleton /> : (
          <div style={{ overflowX: 'auto' }}>
            <table className="apx-table">
              <thead>
                <tr>
                  <th scope="col">Filial</th><th scope="col" className="apx-num">İşçi</th><th scope="col" className="apx-num">Gözləyən</th>
                  <th scope="col">Tabelçi</th><th scope="col">Son müraciət</th>
                </tr>
              </thead>
              <tbody>
                {branches
                  .map((b) => ({ b, waiting: pending.filter((r) => r.branchId === b.id).length, last: requests.find((r) => r.branchId === b.id) }))
                  .sort((x, y) => y.waiting - x.waiting || x.b.name.localeCompare(y.b.name, 'az'))
                  .map(({ b, waiting, last }) => (
                    <tr key={b.id}>
                      <td>{b.name}</td>
                      <td className="apx-num">{w.employees.filter((e) => e.branchId === b.id && e.active).length}</td>
                      <td className="apx-num">{waiting ? <span className="apx-tag apx-tag-warn">{waiting}</span> : <span className="apx-quiet">0</span>}</td>
                      <td>{b.timekeeperId ? personName(w, b.timekeeperId) : <span style={{ color: 'var(--apx-wait)', fontWeight: 700 }}>Təyin edilməyib</span>}</td>
                      <td className="apx-quiet">{last ? when(last.createdAt) : '—'}</td>
                    </tr>
                  ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      {acting && <RequestSheet onClose={() => setActing(false)} onCreated={(id) => navigate(`../muracietler?id=${id}`)} />}
    </>
  )
}
