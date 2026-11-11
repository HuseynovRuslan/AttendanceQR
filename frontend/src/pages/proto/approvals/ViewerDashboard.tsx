import { useNavigate } from 'react-router-dom'
import { IconEye } from '../../../components/icons'
import { STATUS_LABEL, TYPE_LABEL, branchesInScope, visibleRequests } from './model'
import { useProto, useScreenLoad } from './store'
import { EmptyState, ErrorState, Skeleton, StatusMark, employeeLabel, when } from './ui'

/**
 * A Viewer's page: the area they were given — a structure or a single branch — to read, with nothing to press.
 * Rows open the request read-only; no control on this page changes anything.
 */
export function ViewerDashboard() {
  const { world: w, persona } = useProto()
  const load = useScreenLoad(`viewer:${persona.id}`)
  const navigate = useNavigate()
  const branches = branchesInScope(w, persona)
  const people = w.employees.filter((e) => branches.some((b) => b.id === e.branchId) && e.active)
  const requests = visibleRequests(w, persona)
  const decided = requests.filter((r) => r.status === 'approved' || r.status === 'rejected')
    .sort((a, b) => (b.decidedAt ?? '').localeCompare(a.decidedAt ?? '')).slice(0, 8)
  const byBranch = branches.map((b) => ({ b, n: people.filter((e) => e.branchId === b.id).length })).sort((x, y) => y.n - x.n)
  const max = Math.max(1, ...byBranch.map((x) => x.n))
  const area = persona.structureId ? w.structures.find((s) => s.id === persona.structureId)?.name : branches[0]?.name

  if (load.state === 'error') return <div className="apx-panel"><ErrorState what="Hesabat" onRetry={load.retry} /></div>

  return (
    <>
      <div className="apx-head" style={{ marginTop: -8 }}>
        <p style={{ margin: 0 }}>
          {persona.structureId ? `${area} strukturu, ${branches.length} filial.` : `${area} filialı.`} Bu səhifədə heç nə dəyişdirilmir.
        </p>
        <span className="apx-readonly"><IconEye aria-hidden /> Yalnız baxış</span>
      </div>

      {load.state === 'loading' ? <div className="apx-panel"><Skeleton rows={3} /></div> : (
        <section className="apx-strip" aria-label="Qısa icmal">
          <div className="apx-fig"><span className="apx-fig-n">{branches.length}</span><span className="apx-fig-l">filial</span></div>
          <div className="apx-fig"><span className="apx-fig-n">{people.length}</span><span className="apx-fig-l">aktiv işçi</span></div>
          <div className="apx-fig apx-fig-wait"><span className="apx-fig-n">{requests.filter((r) => r.status === 'pending').length}</span><span className="apx-fig-l">müraciət qərar gözləyir</span></div>
          <div className="apx-fig"><span className="apx-fig-n">{requests.filter((r) => r.status === 'approved').length}</span><span className="apx-fig-l">təsdiqlənib</span></div>
        </section>
      )}

      <div className="apx-dash">
        <section className="apx-panel" aria-label="Son qərarlar">
          <div className="apx-panel-h"><h2>Son qərarlar</h2></div>
          {load.state === 'loading' ? <Skeleton /> : decided.length === 0 ? (
            <EmptyState title="Hələ qərar yoxdur" text="Operator müraciətlərə qərar verdikcə burada görünəcək." />
          ) : (
            <div style={{ overflowX: 'auto' }}>
              <table className="apx-table">
                <thead><tr><th scope="col">Müraciət</th><th scope="col">İşçi</th><th scope="col">Status</th><th scope="col">Qərar</th></tr></thead>
                <tbody>
                  {decided.map((r) => (
                    <tr key={r.id}>
                      <td>
                        <button type="button" onClick={() => navigate(`../muracietler?id=${r.id}`)}
                          style={{ background: 'none', border: 0, padding: 0, font: 'inherit', fontWeight: 700, color: 'inherit', cursor: 'pointer', textAlign: 'left' }}
                          aria-label={`Aç: ${TYPE_LABEL[r.type]}, ${STATUS_LABEL[r.status].toLowerCase()}`}>
                          {TYPE_LABEL[r.type]}
                        </button>
                      </td>
                      <td>{employeeLabel(w, r)}</td>
                      <td><StatusMark status={r.status} /></td>
                      <td className="apx-quiet">{r.decidedAt ? when(r.decidedAt) : '—'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </section>

        <section className="apx-panel" aria-label="Filiallar üzrə işçi sayı">
          <div className="apx-panel-h"><h2>Filiallar üzrə işçi</h2><span>aktiv</span></div>
          {load.state === 'loading' ? <Skeleton /> : (
            <div className="apx-bars">
              {byBranch.slice(0, 12).map(({ b, n }) => (
                <div key={b.id} className="apx-bar">
                  <span className="apx-bar-t">{b.name}</span>
                  <span className="apx-bar-n">{n}</span>
                  <span className="apx-bar-track" aria-hidden><i style={{ width: `${(n / max) * 100}%` }} /></span>
                </div>
              ))}
              {byBranch.length > 12 && <span className="apx-quiet">və daha {byBranch.length - 12} filial</span>}
            </div>
          )}
        </section>
      </div>
    </>
  )
}
