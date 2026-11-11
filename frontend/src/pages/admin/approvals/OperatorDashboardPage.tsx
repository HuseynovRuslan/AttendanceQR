import { Link, Navigate, useNavigate } from 'react-router-dom'
import { listRequests, type RequestItem } from '../../../api/approvals'
import { bakuDay, EmptyState, Skeleton, waited, when } from '../../approvals/shared'
import '../../approvals/approvals.css'
import { useApprovals } from './ApprovalsContext'
import { LONG_WAIT_HOURS, TYPE_LABEL, isLongWait } from './model'
import { ErrorState, SectionTabs, call, useLoad } from './parts'
import { NotActive } from './NotActive'

const isList = (d: unknown): d is { items: RequestItem[] } => !!d && typeof d === 'object' && Array.isArray((d as { items?: unknown }).items)

/**
 * An Operator's morning: what waits for them and for how long, what was decided lately, and how their branches stand.
 * The queue is the point of the page: oldest first, its age drawn, the ones over a day marked.
 */
export function OperatorDashboardPage() {
  const { view, state } = useApprovals()
  const navigate = useNavigate()
  const queue = useLoad(() => call(listRequests('queue', { limit: 200 })), isList, [])
  const recent = useLoad(() => call(listRequests('all', { limit: 200 })), isList, [])

  if (state === 'loading') return <div className="apx-embed"><div className="apx-panel"><Skeleton rows={4} /></div></div>
  if (!view.enabled || !view.hasArea) return <NotActive />
  if (!view.roles.includes('operator')) return <Navigate to="/admin/approvals" replace />

  const now = new Date()
  const today = bakuDay(now)
  // Oldest first: what waited longest is decided first. The server sends it so, and the screen holds to it.
  const pending = [...(queue.data?.items ?? [])].sort((a, b) => a.createdAtUtc.localeCompare(b.createdAtUtc))
  const long = pending.filter((r) => isLongWait(r.createdAtUtc, now))
  const mine = new Set(view.approverBranches.map((b) => b.locationId))
  const decided = (recent.data?.items ?? [])
    .filter((r) => mine.has(r.locationId) && r.decidedAtUtc && r.status !== 'Cancelled')
    .sort((a, b) => (b.decidedAtUtc ?? '').localeCompare(a.decidedAtUtc ?? ''))
  const decidedToday = decided.filter((r) => r.decidedAtUtc && bakuDay(r.decidedAtUtc) === today)
  const structures = [...new Set(view.approverBranches.map((b) => b.structureName).filter(Boolean))]
  const standIn = view.approverBranches.filter((b) => b.standsIn)
  const loading = queue.loading && !queue.data

  return (
    <div className="apx-embed">
      <SectionTabs />
      <div className="apx-intro">
        <p>
          {structures.length ? <><b>{structures.join(', ')}</b> {structures.length > 1 ? 'strukturları' : 'strukturu'}: </> : null}
          {view.approverBranches.length} filial. Yeni işçi müraciətlərini siz təsdiqləyirsiniz.
          {standIn.length > 0 && <> Əvəzedici kimi: {[...new Set(standIn.map((b) => b.structureName))].join(', ')}.</>}
        </p>
      </div>

      {queue.error && !queue.data ? (
        <div className="apx-panel"><ErrorState what="Növbə" message={queue.error} onRetry={() => void queue.reload()} /></div>
      ) : (
        <>
          {loading ? <div className="apx-panel"><Skeleton rows={3} /></div> : (
            <section className="apx-strip" aria-label="Qısa icmal">
              <div className="apx-fig apx-fig-wait"><span className="apx-fig-n">{pending.length}</span><span className="apx-fig-l">qərarınızı gözləyir</span></div>
              <div className="apx-fig apx-fig-long"><span className="apx-fig-n">{long.length}</span><span className="apx-fig-l">{LONG_WAIT_HOURS} saatdan çox gözləyir</span></div>
              <div className="apx-fig"><span className="apx-fig-n">{decidedToday.filter((r) => r.status === 'Approved').length}</span><span className="apx-fig-l">bu gün təsdiqlənib</span></div>
              <div className="apx-fig"><span className="apx-fig-n">{decidedToday.filter((r) => r.status === 'Rejected').length}</span><span className="apx-fig-l">bu gün rədd edilib</span></div>
            </section>
          )}

          <div className="apx-dash">
            <section className="apx-panel" aria-label="Gözləyən müraciətlər">
              <div className="apx-panel-h">
                <h2>Növbə</h2>
                <Link to="/admin/approvals" style={{ fontSize: 13, fontWeight: 700, color: 'var(--leaf-d)' }}>Hamısı</Link>
              </div>
              {loading ? <Skeleton /> : pending.length === 0 ? (
                <EmptyState title="Növbə boşdur" text="Tabelçilərin yeni müraciətləri burada, ən uzun gözləyən yuxarıda görünəcək." />
              ) : (
                <div className="apx-queue">
                  {pending.slice(0, 8).map((r) => {
                    const hours = (now.getTime() - new Date(r.createdAtUtc).getTime()) / 3_600_000
                    const isLong = isLongWait(r.createdAtUtc, now)
                    return (
                      <button key={r.id} type="button" className="apx-q" onClick={() => navigate(`/admin/approvals?id=${r.id}`)}>
                        <span className="apx-q-t">{r.subject || TYPE_LABEL[r.type]}</span>
                        <span className={`apx-q-age${isLong ? ' long' : ''}`}>{waited(r.createdAtUtc, now)}</span>
                        <span className="apx-q-s">{TYPE_LABEL[r.type]}{r.number ? `, R-${r.number}` : ''}, {r.locationName}</span>
                        <span className="apx-q-s" style={{ textAlign: 'right' }}>{r.requestedBy}</span>
                        <span className="apx-age" aria-hidden>
                          <i className={isLong ? 'long' : ''} style={{ width: `${Math.min(100, (hours / (LONG_WAIT_HOURS * 1.5)) * 100)}%` }} />
                        </span>
                      </button>
                    )
                  })}
                </div>
              )}
            </section>

            <section className="apx-panel" aria-label="Son qərarlar">
              <div className="apx-panel-h"><h2>Son qərarlar</h2></div>
              {recent.loading && !recent.data ? <Skeleton /> : decided.length === 0 ? (
                <EmptyState title="Hələ qərar yoxdur" text="Təsdiqlədiyiniz və rədd etdiyiniz müraciətlər burada görünəcək." />
              ) : (
                <div className="apx-feed">
                  {decided.slice(0, 8).map((r) => (
                    <div key={r.id} className="apx-feed-i">
                      <span className={`apx-feed-dot ${r.status === 'Approved' ? 'ok' : 'bad'}`} aria-hidden />
                      <span className="apx-feed-x">
                        <b>{r.decidedBy ?? '—'}</b>{' '}
                        {r.direct ? 'birbaşa əlavə etdi' : r.status === 'Approved' ? 'təsdiqlədi' : 'rədd etdi'}: {r.subject}, {r.locationName}
                      </span>
                      <span className="apx-feed-t">{r.decidedAtUtc ? when(r.decidedAtUtc) : ''}</span>
                    </div>
                  ))}
                </div>
              )}
            </section>
          </div>

          <section className="apx-panel" aria-label="Filiallar">
            <div className="apx-panel-h"><h2>Filiallar</h2><span>{view.approverBranches.length} filial</span></div>
            <div style={{ overflowX: 'auto' }}>
              <table className="apx-table">
                <thead>
                  <tr>
                    <th scope="col">Filial</th><th scope="col">Struktur</th><th scope="col" className="apx-num">Gözləyən</th>
                    <th scope="col">Son müraciət</th>
                  </tr>
                </thead>
                <tbody>
                  {view.approverBranches
                    .map((b) => ({
                      b,
                      waiting: pending.filter((r) => r.locationId === b.locationId).length,
                      last: (recent.data?.items ?? []).filter((r) => r.locationId === b.locationId && !r.direct)
                        .sort((x, y) => y.createdAtUtc.localeCompare(x.createdAtUtc))[0],
                    }))
                    .sort((x, y) => y.waiting - x.waiting || x.b.name.localeCompare(y.b.name, 'az'))
                    .map(({ b, waiting, last }) => (
                      <tr key={b.locationId}>
                        <td>{b.name}</td>
                        <td className="apx-quiet">{b.structureName ?? '—'}</td>
                        <td className="apx-num">{waiting ? <span className="apx-tag apx-tag-warn">{waiting}</span> : <span className="apx-quiet">0</span>}</td>
                        <td className="apx-quiet">{last ? when(last.createdAtUtc) : '—'}</td>
                      </tr>
                    ))}
                </tbody>
              </table>
            </div>
          </section>
        </>
      )}
    </div>
  )
}
