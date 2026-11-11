import { useMemo, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { IconAlert, IconArrowLeft, IconEye, IconSearch } from '../../../components/icons'
import {
  STATUS_LABEL, TYPE_LABEL, approverIdFor, branchesInScope, canCancel, canDecide, isLongWait, structureOf, visibleRequests,
  type ChangeRequest, type RequestStatus, type RequestType,
} from './model'
import { useProto, useScreenLoad } from './store'
import {
  EmptyState, ErrorState, Pips, Route, Skeleton, StatusMark, TypeIcon, employeeLabel, periodLabel, personName, waited, when,
} from './ui'

const STATUSES: RequestStatus[] = ['pending', 'approved', 'rejected', 'cancelled']

/** Every request in the caller's area: the queue on the left, one request in full on the right. */
export function ApprovalCenter() {
  const { world: w, persona, now } = useProto()
  const load = useScreenLoad(`approvals:${persona.id}`)
  const [params, setParams] = useSearchParams()
  const all = useMemo(() => visibleRequests(w, persona), [w, persona])
  const selectedId = params.get('id')
  const selected = all.find((r) => r.id === selectedId)
  const [status, setStatus] = useState<RequestStatus>(selected?.status ?? 'pending')
  const [query, setQuery] = useState('')
  const [type, setType] = useState<RequestType | ''>('')
  const [branchId, setBranchId] = useState('')
  const branches = branchesInScope(w, persona)

  const q = query.trim().toLowerCase()
  const list = all
    .filter((r) => r.status === status)
    .filter((r) => !type || r.type === type)
    .filter((r) => !branchId || r.branchId === branchId)
    .filter((r) => !q || [employeeLabel(w, r), w.branches.find((b) => b.id === r.branchId)?.name ?? '', r.id, r.note]
      .some((s) => s.toLowerCase().includes(q)))
    // The queue oldest first — what waited longest is decided first; history newest first.
    .sort((a, b) => (status === 'pending' ? a.createdAt.localeCompare(b.createdAt) : b.createdAt.localeCompare(a.createdAt)))
  const filtered = !!(q || type || branchId)

  const open = (id: string | null) => {
    const next = new URLSearchParams(params)
    if (id) next.set('id', id)
    else next.delete('id')
    setParams(next, { replace: true })
  }

  if (load.state === 'error') return <div className="apx-panel"><ErrorState what="Müraciətlər" onRetry={load.retry} /></div>

  return (
    <>
      <div className="apx-tools">
        <div className="apx-seg" role="tablist" aria-label="Status">
          {STATUSES.map((s) => (
            <button
              key={s} type="button" role="tab" aria-selected={status === s}
              onClick={() => { setStatus(s); if (selected && selected.status !== s) open(null) }}
            >
              {STATUS_LABEL[s]} <b>{all.filter((r) => r.status === s).length}</b>
            </button>
          ))}
        </div>
        <label className="apx-search">
          <IconSearch aria-hidden />
          <input className="apx-input" placeholder="İşçi, filial və ya nömrə" value={query} onChange={(e) => setQuery(e.target.value)} aria-label="Müraciət axtar" />
        </label>
        <select className="apx-select" value={type} onChange={(e) => setType(e.target.value as RequestType | '')} aria-label="Növ">
          <option value="">Bütün növlər</option>
          {(Object.keys(TYPE_LABEL) as RequestType[]).map((t) => <option key={t} value={t}>{TYPE_LABEL[t]}</option>)}
        </select>
        {branches.length > 1 && (
          <select className="apx-select" value={branchId} onChange={(e) => setBranchId(e.target.value)} aria-label="Filial" style={{ maxWidth: 220 }}>
            <option value="">Bütün filiallar</option>
            {branches.map((b) => <option key={b.id} value={b.id}>{b.name}</option>)}
          </select>
        )}
      </div>

      <div className="apx-split" data-open={selected ? 'true' : 'false'}>
        <section className="apx-panel" aria-label="Müraciətlər">
          {load.state === 'loading' ? <Skeleton rows={8} /> : list.length === 0 ? (
            filtered ? (
              <EmptyState
                title="Bu filtrə uyğun müraciət yoxdur" text="Axtarışı və ya filtrləri dəyişin."
                action={<button type="button" className="apx-btn apx-btn-sm" onClick={() => { setQuery(''); setType(''); setBranchId('') }}>Filtrləri sıfırla</button>}
              />
            ) : status === 'pending' ? (
              <EmptyState title="Gözləyən müraciət yoxdur" text="Yeni müraciət gələndə burada, ən köhnəsi yuxarıda görünəcək." />
            ) : (
              <EmptyState title={`«${STATUS_LABEL[status]}» müraciət yoxdur`} text="Qərar verilmiş müraciətlər burada saxlanır." />
            )
          ) : (
            <ul className="apx-list" aria-label="Müraciət siyahısı">
              {list.map((r) => (
                <li key={r.id}>
                <button
                  type="button" className="apx-item" aria-current={selected?.id === r.id}
                  onClick={() => open(r.id)}
                >
                  <span className="apx-item-t">{TYPE_LABEL[r.type]}</span>
                  <span className="apx-item-r">
                    {r.status === 'pending'
                      ? <span className={`apx-q-age${isLongWait(r, now) ? ' long' : ''}`}>{waited(r.createdAt, now)}</span>
                      : <StatusMark status={r.status} />}
                  </span>
                  <span className="apx-item-s">
                    {employeeLabel(w, r)}, {w.branches.find((b) => b.id === r.branchId)?.name}
                  </span>
                  <Pips r={r} />
                </button>
                </li>
              ))}
            </ul>
          )}
        </section>

        <section className="apx-panel" aria-label="Müraciətin detalları">
          {selected ? (
            <RequestDetail key={selected.id} r={selected} onBack={() => open(null)} />
          ) : (
            <EmptyState
              title="Müraciət seçin"
              text={status === 'pending' && list.length ? 'Soldakı siyahıda ən uzun gözləyən müraciət yuxarıdadır.' : 'Müraciətin bütün yolu və tarixçəsi burada açılacaq.'}
            />
          )}
        </section>
      </div>
    </>
  )
}

function RequestDetail({ r, onBack }: { r: ChangeRequest; onBack: () => void }) {
  const { world: w, persona, approve, reject, cancel, loading, now } = useProto()
  const [mode, setMode] = useState<'idle' | 'rejecting'>('idle')
  const [reason, setReason] = useState('')
  const [tried, setTried] = useState(false)
  const branch = w.branches.find((b) => b.id === r.branchId)
  const structure = structureOf(w, r.branchId)
  const approverId = approverIdFor(w, r.branchId)
  const emp = w.employees.find((e) => e.id === r.employeeId)
  const mayDecide = canDecide(w, persona, r)
  const mayCancel = canCancel(persona, r)
  const deleteBlocked = r.type === 'employee_delete' && emp?.hasHistory

  async function confirmReject() {
    setTried(true)
    if (reason.trim().length < 3) return
    const out = await reject(r.id, reason)
    if (out.ok) { setMode('idle'); setReason('') }
  }

  return (
    <article className="apx-detail">
      <button type="button" className="apx-btn apx-btn-ghost apx-btn-sm apx-back" style={{ justifySelf: 'start' }} onClick={onBack}>
        <IconArrowLeft /> Siyahıya qayıt
      </button>
      <header className="apx-detail-h">
        <div>
          <h2 style={{ display: 'flex', gap: 10, alignItems: 'center' }}>
            <span style={{ display: 'inline-flex', color: 'var(--c500)' }}><TypeIcon type={r.type} /></span>
            {TYPE_LABEL[r.type]}
          </h2>
          <p>{employeeLabel(w, r)}, {branch?.name}. Müraciət {r.id}.</p>
        </div>
        <div style={{ display: 'flex', gap: 8, alignItems: 'center', flexWrap: 'wrap' }}>
          {r.direct && <span className="apx-tag apx-tag-direct">Birbaşa əməliyyat</span>}
          {isLongWait(r, now) && <span className="apx-tag apx-tag-warn">{waited(r.createdAt, now)} gözləyir</span>}
          <StatusMark status={r.status} />
        </div>
      </header>

      <Route w={w} r={r} approverId={approverId} />

      <dl className="apx-facts">
        <div><dt>Filial</dt><dd>{branch?.name}</dd></div>
        <div><dt>Struktur</dt><dd>{structure?.name ?? 'Struktursuz'}</dd></div>
        {periodLabel(r) && <div><dt>{r.type === 'permission' ? 'Vaxt' : 'Dövr'}</dt><dd>{periodLabel(r)}</dd></div>}
        <div><dt>Yaradılıb</dt><dd>{when(r.createdAt)}</dd></div>
      </dl>

      {r.changes.length > 0 && (
        <div>
          <h3 className="apx-sec-t">Dəyişiklik</h3>
          <table className="apx-diff">
            <thead>
              <tr>
                <th scope="col">Sahə</th>
                {/* Once applied, «İndiki» would be wrong — the proposal IS the current value now. */}
                <th scope="col">{r.status === 'approved' ? 'Əvvəl' : 'İndiki'}</th>
                <th scope="col">{r.status === 'approved' ? 'Sonra' : r.status === 'pending' ? 'Təklif olunan' : 'Təklif olunmuşdu'}</th>
              </tr>
            </thead>
            <tbody>
              {r.changes.map((c) => (
                <tr key={c.field}>
                  <td>{c.label}</td>
                  <td>{c.before ? <span className="apx-was">{c.before}</span> : <span className="apx-empty-cell">—</span>}</td>
                  <td>{c.after ? <span className="apx-will">{c.after}</span> : <span className="apx-empty-cell">—</span>}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <div>
        <h3 className="apx-sec-t">Qeyd / Səbəb</h3>
        <p className="apx-note" style={{ margin: 0 }}>{r.note || '—'}</p>
      </div>

      {r.status === 'rejected' && r.decisionNote && (
        <div>
          <h3 className="apx-sec-t">Rədd səbəbi</h3>
          <p className="apx-note apx-note-bad" style={{ margin: 0 }}>{r.decisionNote}</p>
        </div>
      )}

      {deleteBlocked && r.status === 'pending' && (
        <p className="apx-lock" role="note" style={{ margin: 0, background: 'var(--clay-bg)', color: '#6E2A18' }}>
          <IconAlert aria-hidden />
          Bu işçinin davamiyyət tarixçəsi var — silinmə təsdiqlənə bilməz. Rədd edib tabelçidən deaktivasiya müraciəti istəyin.
        </p>
      )}

      <DecisionArea
        r={r} mayDecide={mayDecide} mayCancel={mayCancel} loading={loading} mode={mode} setMode={setMode}
        reason={reason} setReason={setReason} tried={tried} deleteBlocked={!!deleteBlocked}
        approverName={personName(w, approverId)} viewer={persona.kind === 'viewer'} timekeeper={persona.kind === 'timekeeper'}
        admin={persona.kind === 'companyAdmin'}
        onApprove={() => void approve(r.id)} onReject={() => void confirmReject()} onCancel={() => void cancel(r.id)}
      />

      <div>
        <h3 className="apx-sec-t">Audit tarixçəsi</h3>
        <ol className="apx-trail">
          {r.events.map((e, i) => (
            <li key={i} className="apx-trail-i">
              <span
                className={`apx-trail-dot ${e.kind === 'approved' || e.kind === 'applied' || e.kind === 'direct' ? 'ok' : e.kind === 'rejected' ? 'bad' : e.kind === 'cancelled' ? 'off' : ''}`}
                aria-hidden
              />
              <div className="apx-trail-x">
                <b>{personName(w, e.actorId)}</b> {EVENT_TEXT[e.kind]}
                {e.note && e.kind !== 'created' ? `: «${e.note}»` : ''}
                <small>{when(e.at)}</small>
              </div>
            </li>
          ))}
        </ol>
      </div>
    </article>
  )
}

const EVENT_TEXT: Record<ChangeRequest['events'][number]['kind'], string> = {
  created: 'müraciəti yaratdı',
  approved: 'təsdiqlədi',
  rejected: 'rədd etdi',
  cancelled: 'müraciəti ləğv etdi',
  applied: 'dəyişiklik tətbiq olundu',
  direct: 'öz strukturunda birbaşa başlatdı',
}

function DecisionArea(p: {
  r: ChangeRequest; mayDecide: boolean; mayCancel: boolean; loading: boolean; mode: 'idle' | 'rejecting'
  setMode: (m: 'idle' | 'rejecting') => void; reason: string; setReason: (s: string) => void; tried: boolean
  deleteBlocked: boolean; approverName: string; viewer: boolean; timekeeper: boolean; admin: boolean
  onApprove: () => void; onReject: () => void; onCancel: () => void
}) {
  if (p.r.status !== 'pending') return null
  if (p.mayDecide) {
    return (
      <section className="apx-decide" aria-label="Qərar">
        {p.mode === 'idle' ? (
          <div className="apx-decide-row">
            <button type="button" className="apx-btn apx-btn-go" disabled={p.loading || p.deleteBlocked} onClick={p.onApprove}>
              {p.loading ? 'Göndərilir…' : 'Təsdiqlə'}
            </button>
            <button type="button" className="apx-btn apx-btn-stop" disabled={p.loading} onClick={() => p.setMode('rejecting')}>Rədd et</button>
            <span className="apx-quiet">Təsdiqlənən dəyişiklik dərhal tətbiq olunur.</span>
          </div>
        ) : (
          <>
            <label className="apx-field">
              <span>Rədd səbəbi (tabelçi bunu görəcək)</span>
              <textarea
                className="apx-textarea" value={p.reason} onChange={(e) => p.setReason(e.target.value)} autoFocus
                aria-invalid={p.tried && p.reason.trim().length < 3}
                placeholder="Məsələn: arayış əlavə olunmayıb, tarixlər qrafiklə üst-üstə düşür"
              />
              {p.tried && p.reason.trim().length < 3 && <span className="apx-field-err" role="alert">Rədd səbəbini yazın (ən azı 3 hərf)</span>}
            </label>
            <div className="apx-decide-row">
              <button type="button" className="apx-btn apx-btn-stop" disabled={p.loading} onClick={p.onReject}>
                {p.loading ? 'Göndərilir…' : 'Rəddi təsdiqlə'}
              </button>
              <button type="button" className="apx-btn apx-btn-ghost" disabled={p.loading} onClick={() => p.setMode('idle')}>Geri</button>
            </div>
          </>
        )}
      </section>
    )
  }
  if (p.mayCancel) {
    return (
      <section className="apx-decide" aria-label="Müraciətim">
        <div className="apx-decide-row">
          <button type="button" className="apx-btn" disabled={p.loading} onClick={p.onCancel}>{p.loading ? 'Göndərilir…' : 'Müraciəti ləğv et'}</button>
          <span className="apx-quiet">Qərarı {p.approverName} verəcək. Öz müraciətinizi təsdiqləyə bilməzsiniz.</span>
        </div>
      </section>
    )
  }
  return (
    <p className="apx-lock" style={{ margin: 0 }}>
      <IconEye aria-hidden />
      {p.viewer
        ? 'Siz müşahidəçisiniz: müraciətləri görürsünüz, qərar verə və dəyişə bilməzsiniz.'
        : p.timekeeper
          ? `Qərarı strukturun operatoru (${p.approverName}) verir.`
          : p.admin
            ? `Bu müraciəti strukturun operatoru (${p.approverName}) həll edir. Şirkət administratoru gündəlik müraciətlərin təsdiqçisi deyil.`
            : `Bu müraciət sizin sahənizdə deyil — qərarı ${p.approverName} verir.`}
    </p>
  )
}
