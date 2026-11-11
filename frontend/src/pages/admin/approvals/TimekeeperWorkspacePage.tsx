import { useState } from 'react'
import { Link, Navigate } from 'react-router-dom'
import { cancelRequest, isApiError, listRequests, type RequestItem } from '../../../api/approvals'
import { IconAlert } from '../../../components/icons'
import { EmptyState, Skeleton, when } from '../../approvals/shared'
import '../../approvals/approvals.css'
import { useApprovals } from './ApprovalsContext'
import { STATUS_LABEL, TYPE_LABEL, errorText } from './model'
import { NewEmployeeRequestSheet } from './NewEmployeeRequestSheet'
import { NotActive } from './NotActive'
import { ErrorState, Pips, SectionTabs, StatusMark, Toasts, call, useLoad, useToasts } from './parts'

const isList = (d: unknown): d is { items: RequestItem[] } => !!d && typeof d === 'object' && Array.isArray((d as { items?: unknown }).items)

/**
 * A Timekeeper's branch: «Yeni işçi müraciəti» in one press, and where each of their requests stands. Only the one
 * kind of request the server applies is offered; everything else at the branch is still done the old way.
 */
export function TimekeeperWorkspacePage() {
  const { view, state, reload: reloadMe } = useApprovals()
  const [branchId, setBranchId] = useState<string>('')
  const [asking, setAsking] = useState(false)
  const [busyId, setBusyId] = useState<string | null>(null)
  const { toasts, push, close } = useToasts()
  const mine = useLoad(() => call(listRequests('mine', { limit: 200 })), isList, [])

  if (state === 'loading') return <div className="apx-embed"><div className="apx-panel"><Skeleton rows={4} /></div></div>
  if (!view.enabled || !view.hasArea) return <NotActive />
  if (!view.roles.includes('timekeeper')) return <Navigate to="/admin/approvals" replace />

  const branch = view.requestBranches.find((b) => b.locationId === branchId) ?? view.requestBranches[0]
  const requests = (mine.data?.items ?? [])
    .filter((r) => r.locationId === branch.locationId)
    .sort((a, b) => b.createdAtUtc.localeCompare(a.createdAtUtc))
  const waiting = requests.filter((r) => r.status === 'Pending').length

  async function cancel(r: RequestItem) {
    if (busyId) return
    setBusyId(r.id)
    const res = await call(cancelRequest(r.id))
    setBusyId(null)
    if (res.status === 200 && res.data && !isApiError(res.data)) push(`R-${r.number} ləğv edildi.`)
    else push(errorText(res.status, res.data), true)
    void mine.reload()
    reloadMe()
  }

  return (
    <div className="apx-embed">
      <SectionTabs />
      <div className="apx-intro">
        <p>
          <b>{branch.name}</b>{branch.structureName ? `, ${branch.structureName} strukturu` : ''}. Yeni işçini siz
          müraciətlə göndərirsiniz, strukturun operatoru təsdiqləyəndən sonra işçi hesabı yaranır.
        </p>
        <button type="button" className="apx-btn apx-btn-go" onClick={() => setAsking(true)}>Yeni işçi müraciəti</button>
      </div>

      {view.requestBranches.length > 1 && (
        <label className="apx-field" style={{ maxWidth: 360 }}>
          <span>Filial</span>
          <select className="apx-select" value={branch.locationId} onChange={(e) => setBranchId(e.target.value)}>
            {view.requestBranches.map((b) => <option key={b.locationId} value={b.locationId}>{b.name}</option>)}
          </select>
        </label>
      )}

      <p className="apx-info" style={{ margin: 0 }}>
        <IconAlert aria-hidden />
        <span>
          Hələlik təsdiqlə yalnız yeni işçi əlavə olunur. Məlumat dəyişikliyi, deaktivasiya, məzuniyyət, icazə,
          xəstəlik, davamiyyət düzəlişi və növbə dəyişikliyi köhnə qaydada, birbaşa edilir.
        </span>
      </p>

      <section className="apx-panel" aria-label="Müraciətlərim">
        <div className="apx-panel-h"><h2>Müraciətlərim</h2><span>{waiting ? `${waiting} gözləyir` : `${requests.length} müraciət`}</span></div>
        {mine.error && !mine.data ? (
          <ErrorState what="Müraciətləriniz" message={mine.error} onRetry={() => void mine.reload()} />
        ) : mine.loading && !mine.data ? <Skeleton /> : requests.length === 0 ? (
          <EmptyState
            title="Hələ müraciət yoxdur"
            text="Filiala yeni işçi gələndə «Yeni işçi müraciəti» ilə göndərin. Statusu burada izləyəcəksiniz."
            action={<button type="button" className="apx-btn apx-btn-go apx-btn-sm" onClick={() => setAsking(true)}>Yeni işçi müraciəti</button>}
          />
        ) : (
          <div className="apx-queue">
            {requests.map((r) => (
              <div key={r.id} className="apx-q" style={{ cursor: 'default' }}>
                <Link to={`/admin/approvals?id=${r.id}`} className="apx-q-t" style={{ color: 'inherit', textDecoration: 'none' }}>
                  {r.subject}
                </Link>
                <span style={{ justifySelf: 'end' }}><StatusMark status={r.status} /></span>
                <span className="apx-q-s">{TYPE_LABEL[r.type]}{r.number ? `, R-${r.number}` : ''}, göndərilib {when(r.createdAtUtc)}</span>
                <span style={{ justifySelf: 'end' }}><Pips status={r.status} /></span>
                {r.status === 'Rejected' && r.decisionNote && (
                  <span className="apx-note apx-note-bad" style={{ gridColumn: '1 / -1', fontSize: 13, padding: '8px 10px' }}>
                    Rədd səbəbi: {r.decisionNote}
                  </span>
                )}
                {r.status !== 'Pending' && r.decidedAtUtc && (
                  <span className="apx-q-s" style={{ gridColumn: '1 / -1' }}>
                    {STATUS_LABEL[r.status]}{r.decidedBy ? `: ${r.decidedBy}` : ''}, {when(r.decidedAtUtc)}
                  </span>
                )}
                <span className="apx-q-s" style={{ gridColumn: '1 / -1', display: 'flex', gap: 10, alignItems: 'center', flexWrap: 'wrap' }}>
                  <Link to={`/admin/approvals?id=${r.id}`} style={{ fontWeight: 700, color: 'var(--leaf-d)' }}>Ətraflı</Link>
                  {r.canCancel && (
                    <button type="button" className="apx-btn apx-btn-sm apx-btn-ghost" disabled={busyId === r.id} onClick={() => void cancel(r)}>
                      {busyId === r.id ? 'Ləğv edilir…' : 'Ləğv et'}
                    </button>
                  )}
                </span>
              </div>
            ))}
          </div>
        )}
      </section>

      {asking && (
        <NewEmployeeRequestSheet
          branch={branch}
          onClose={() => setAsking(false)}
          onSent={(s) => {
            setAsking(false)
            push(s.replayed
              ? `Bu müraciət artıq göndərilmişdi (R-${s.number}).`
              : `Müraciət R-${s.number} göndərildi. Operator qərar verəndə statusu burada görəcəksiniz.`)
            void mine.reload()
            reloadMe()
          }}
        />
      )}
      <Toasts toasts={toasts} onClose={close} />
    </div>
  )
}
