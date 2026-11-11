import { useMemo, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import {
  approveRequest, cancelRequest, getFormOptions, getRequest, isApiError, listRequests, rejectRequest,
  type RequestDetail, type RequestItem, type RequestStatus,
} from '../../../api/approvals'
import { IconAlert, IconArrowLeft, IconEye, IconSearch } from '../../../components/icons'
import { searchFold } from '../../../lib/searchFold'
import { day, EmptyState, Skeleton, waited, when } from '../../approvals/shared'
import '../../approvals/approvals.css'
import { useApprovals } from './ApprovalsContext'
import { EVENT_TEXT, STATUS_LABEL, TYPE_LABEL, applyReason, errorText, isLongWait, phoneShown, routeOf } from './model'
import { ErrorState, PinReveal, Pips, RouteLine, SectionTabs, StatusMark, Toasts, call, useLoad, useToasts } from './parts'
import { NotActive } from './NotActive'

const STATUSES: RequestStatus[] = ['Pending', 'Approved', 'Rejected', 'Cancelled']

const isList = (d: unknown): d is { items: RequestItem[] } => !!d && typeof d === 'object' && Array.isArray((d as { items?: unknown }).items)
const isDetail = (d: unknown): d is RequestDetail => !!d && typeof d === 'object' && 'request' in d && 'history' in d

/**
 * Every request in the caller's area: the list on the left, one request in full on the right; on a phone, one at a
 * time with the decision fixed to the bottom of the screen. What a person may do with a request is the server's
 * answer for it (canDecide, canCancel), never worked out here.
 */
export function ApprovalCenterPage() {
  const { view, state, reload: reloadMe } = useApprovals()
  const [params, setParams] = useSearchParams()
  const selectedId = params.get('id')
  const [status, setStatus] = useState<RequestStatus>('Pending')
  const [query, setQuery] = useState('')
  const [branchId, setBranchId] = useState('')
  const { toasts, push, close } = useToasts()

  const list = useLoad(() => call(listRequests('all', { limit: 500 })), isList, [])
  const all = useMemo(() => list.data?.items ?? [], [list.data])
  const now = new Date()

  if (state === 'loading') return <div className="apx-embed"><div className="apx-panel"><Skeleton rows={4} /></div></div>
  if (!view.enabled || !view.hasArea) return <NotActive />

  const q = searchFold(query.trim())
  const rows = all
    .filter((r) => r.status === status)
    .filter((r) => !branchId || r.locationId === branchId)
    .filter((r) => !q || [r.subject, r.locationName ?? '', r.number ? `R-${r.number}` : '', r.requestedBy ?? '']
      .some((s) => searchFold(s).includes(q)))
    // The queue oldest first, so what waited longest is decided first; history newest first.
    .sort((a, b) => (status === 'Pending' ? a.createdAtUtc.localeCompare(b.createdAtUtc) : b.createdAtUtc.localeCompare(a.createdAtUtc)))
  const filtered = !!(q || branchId)
  const selected = all.find((r) => r.id === selectedId)

  const open = (id: string | null) => {
    const next = new URLSearchParams(params)
    if (id) next.set('id', id)
    else next.delete('id')
    setParams(next, { replace: true })
  }
  const refresh = () => { void list.reload(); reloadMe() }

  return (
    <div className="apx-embed">
      <SectionTabs />
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
          <input className="apx-input" placeholder="Ad, filial və ya R-nömrə" value={query} onChange={(e) => setQuery(e.target.value)} aria-label="Müraciət axtar" />
        </label>
        {view.branches.length > 1 && (
          <select className="apx-select" value={branchId} onChange={(e) => setBranchId(e.target.value)} aria-label="Filial" style={{ maxWidth: 240 }}>
            <option value="">Bütün filiallar</option>
            {view.branches.map((b) => <option key={b.locationId} value={b.locationId}>{b.name}</option>)}
          </select>
        )}
      </div>

      <div className="apx-split" data-open={selectedId ? 'true' : 'false'}>
        <section className="apx-panel" aria-label="Müraciətlər">
          {list.error && !list.data ? (
            <ErrorState what="Müraciətlər" message={list.error} onRetry={() => void list.reload()} />
          ) : list.loading && !list.data ? <Skeleton rows={8} /> : rows.length === 0 ? (
            filtered ? (
              <EmptyState
                title="Bu filtrə uyğun müraciət yoxdur" text="Axtarışı və ya filialı dəyişin."
                action={<button type="button" className="apx-btn apx-btn-sm" onClick={() => { setQuery(''); setBranchId('') }}>Filtrləri sıfırla</button>}
              />
            ) : status === 'Pending' ? (
              <EmptyState title="Gözləyən müraciət yoxdur" text="Yeni müraciət gələndə burada, ən uzun gözləyən yuxarıda görünəcək." />
            ) : (
              <EmptyState title={`«${STATUS_LABEL[status]}» müraciət yoxdur`} text="Qərar verilmiş müraciətlər burada saxlanır." />
            )
          ) : (
            <ul className="apx-list" aria-label="Müraciət siyahısı">
              {rows.map((r) => (
                <li key={r.id}>
                  <button type="button" className="apx-item" aria-current={selectedId === r.id} onClick={() => open(r.id)}>
                    <span className="apx-item-t">{r.subject || TYPE_LABEL[r.type]}</span>
                    <span className="apx-item-r">
                      {r.status === 'Pending'
                        ? <span className={`apx-q-age${isLongWait(r.createdAtUtc, now) ? ' long' : ''}`}>{waited(r.createdAtUtc, now)}</span>
                        : r.direct ? <span className="apx-tag apx-tag-direct">Birbaşa</span> : <StatusMark status={r.status} />}
                    </span>
                    <span className="apx-item-s">
                      {TYPE_LABEL[r.type]}{r.number ? `, R-${r.number}` : ''}, {r.locationName}
                    </span>
                    <Pips status={r.status} />
                  </button>
                </li>
              ))}
            </ul>
          )}
        </section>

        <section className="apx-panel" aria-label="Müraciətin detalları">
          {selectedId ? (
            <Detail
              key={selectedId} id={selectedId} onBack={() => open(null)} onChanged={refresh}
              toast={push}
            />
          ) : (
            <EmptyState
              title="Müraciət seçin"
              text={status === 'Pending' && rows.length ? 'Soldakı siyahıda ən uzun gözləyən müraciət yuxarıdadır.' : 'Müraciətin bütün yolu və tarixçəsi burada açılacaq.'}
            />
          )}
        </section>
      </div>
      <Toasts toasts={toasts} onClose={close} />
    </div>
  )
}

function Detail({ id, onBack, onChanged, toast }: {
  id: string; onBack: () => void; onChanged: () => void; toast: (text: string, bad?: boolean) => void
}) {
  const { view } = useApprovals()
  const detail = useLoad(() => call(getRequest(id)), isDetail, [id])
  const [mode, setMode] = useState<'idle' | 'rejecting'>('idle')
  const [reason, setReason] = useState('')
  const [tried, setTried] = useState(false)
  const [busy, setBusy] = useState(false)
  const [problem, setProblem] = useState<string | null>(null)
  const [pin, setPin] = useState<{ value: string; name: string } | null>(null)
  const scheduleId = detail.data?.payload?.scheduleId ?? null
  const mayReadOptions = !!detail.data?.request.canDecide || !!detail.data?.request.canCancel
  const options = useLoad(
    () => (scheduleId && mayReadOptions && detail.data
      ? call(getFormOptions(detail.data.request.locationId))
      : Promise.resolve({ status: 204, data: undefined })),
    (d): d is { schedules: { id: string; name: string; start: string; end: string }[] } =>
      !!d && typeof d === 'object' && 'schedules' in d,
    [scheduleId, mayReadOptions, detail.data?.request.locationId],
  )

  if (detail.error && !detail.data) {
    return (
      <div className="apx-detail">
        <button type="button" className="apx-btn apx-btn-ghost apx-btn-sm apx-back" style={{ justifySelf: 'start' }} onClick={onBack}>
          <IconArrowLeft /> Siyahıya qayıt
        </button>
        <ErrorState what="Müraciət" message={detail.error} onRetry={() => void detail.reload()} />
      </div>
    )
  }
  if (!detail.data) return <Skeleton rows={10} />

  const d = detail.data
  const r = d.request
  const p = d.payload
  const branch = view.branches.find((b) => b.locationId === r.locationId)
  const now = new Date()
  const schedule = options.data?.schedules.find((s) => s.id === p?.scheduleId)

  /** One press, one request: the buttons are off while it travels, and every answer ends in a fresh read. */
  async function act(kind: 'approve' | 'reject' | 'cancel') {
    if (busy) return
    if (kind === 'reject') {
      setTried(true)
      if (reason.trim().length < 3) return
    }
    setBusy(true)
    setProblem(null)
    const res = kind === 'approve' ? await call(approveRequest(r.id))
      : kind === 'reject' ? await call(rejectRequest(r.id, reason.trim()))
      : await call(cancelRequest(r.id))
    setBusy(false)
    if (res.status === 200 && res.data && !isApiError(res.data)) {
      if (kind === 'approve') {
        const data = res.data as { tempPin?: string }
        if (data.tempPin) setPin({ value: data.tempPin, name: p?.fullName || r.subject })
        toast(`${r.subject} təsdiqləndi, işçi hesabı yaradıldı.`)
      } else {
        toast(kind === 'reject' ? 'Müraciət rədd edildi. Tabelçi səbəbi görəcək.' : 'Müraciət ləğv edildi.')
      }
      setMode('idle')
      setReason('')
      setTried(false)
    } else {
      const text = errorText(res.status, res.data)
      setProblem(text)
      // A decision taken elsewhere, or a change that could not be applied: what the screen shows is now stale.
      if (res.status === 409) toast(text, true)
    }
    void detail.reload()
    onChanged()
  }

  const showHidden = d.contactsHidden
  const fact = (value: string | null | undefined, hidden = false) =>
    hidden && showHidden ? <span className="apx-hidden">Gizlədilib</span> : value ? value : <span className="apx-empty-cell">—</span>

  return (
    <article className="apx-detail">
      <button type="button" className="apx-btn apx-btn-ghost apx-btn-sm apx-back" style={{ justifySelf: 'start' }} onClick={onBack}>
        <IconArrowLeft /> Siyahıya qayıt
      </button>
      <header className="apx-detail-h">
        <div>
          <h2>{TYPE_LABEL[r.type]}: {r.subject}</h2>
          <p>{r.number ? `R-${r.number}, ` : ''}{r.locationName}{branch?.structureName ? `, ${branch.structureName}` : ''}</p>
        </div>
        <div style={{ display: 'flex', gap: 8, alignItems: 'center', flexWrap: 'wrap' }}>
          {r.direct && <span className="apx-tag apx-tag-direct">Birbaşa əlavə</span>}
          {r.status === 'Pending' && isLongWait(r.createdAtUtc, now) && <span className="apx-tag apx-tag-warn">{waited(r.createdAtUtc, now)} gözləyir</span>}
          <StatusMark status={r.status} />
        </div>
      </header>

      <RouteLine stops={routeOf(d)} />

      {problem && (
        <p className="apx-lock" role="alert" style={{ margin: 0, background: 'var(--clay-bg)', color: '#6E2A18' }}>
          <IconAlert aria-hidden /> {problem}
        </p>
      )}

      {p && (
        <div>
          <h3 className="apx-sec-t">Yeni işçinin məlumatları</h3>
          <dl className="apx-facts">
            <div><dt>Ad və soyad</dt><dd>{fact(`${p.firstName} ${p.lastName}`)}</dd></div>
            <div><dt>Ata adı</dt><dd>{fact(p.fatherName)}</dd></div>
            <div><dt>Telefon</dt><dd>{fact(phoneShown(p.phoneNumber), true)}</dd></div>
            <div><dt>E-poçt</dt><dd>{fact(p.email, true)}</dd></div>
            <div><dt>Doğum tarixi</dt><dd>{fact(p.birthDate ? day(p.birthDate) : null, true)}</dd></div>
            <div><dt>Vəzifə</dt><dd>{fact(p.position)}</dd></div>
            <div><dt>Növbə</dt><dd>{fact(schedule ? `${schedule.name} (${schedule.start}–${schedule.end})` : p.scheduleId ? 'Təyin olunub' : null)}</dd></div>
            <div><dt>Göndərilib</dt><dd>{when(r.createdAtUtc)}</dd></div>
          </dl>
          {showHidden && (
            <p className="apx-quiet" style={{ margin: '8px 0 0' }}>
              Telefon, e-poçt və doğum tarixi sizin rolunuz üçün gizlədilib.
            </p>
          )}
        </div>
      )}

      <div>
        <h3 className="apx-sec-t">Qeyd</h3>
        <p className="apx-note" style={{ margin: 0 }}>{d.note || '—'}</p>
      </div>

      {r.status === 'Rejected' && d.decisionNote && (
        <div>
          <h3 className="apx-sec-t">Rədd səbəbi</h3>
          <p className="apx-note apx-note-bad" style={{ margin: 0 }}>{d.decisionNote}</p>
        </div>
      )}

      <Decision
        r={r} busy={busy} mode={mode} setMode={setMode} reason={reason} setReason={setReason} tried={tried}
        viewerOnly={view.roles.length === 1 && view.roles[0] === 'viewer'} admin={view.roles.includes('companyAdmin')}
        onApprove={() => void act('approve')} onReject={() => void act('reject')} onCancel={() => void act('cancel')}
      />

      <div>
        <h3 className="apx-sec-t">Audit tarixçəsi</h3>
        <ol className="apx-trail">
          {d.history.map((h, i) => (
            <li key={i} className="apx-trail-i">
              <span
                className={`apx-trail-dot ${h.kind === 'Approved' || h.kind === 'Applied' || h.kind === 'Direct' ? 'ok' : h.kind === 'Rejected' || h.kind === 'ApplyRefused' ? 'bad' : h.kind === 'Cancelled' ? 'off' : ''}`}
                aria-hidden
              />
              <div className="apx-trail-x">
                <b>{h.actor ?? 'Sistem'}</b> {EVENT_TEXT[h.kind]}
                {h.note && h.kind !== 'Created' ? `: «${h.note}»` : ''}
                {h.kind === 'ApplyRefused' ? `: ${applyReason(typeof h.data?.error === 'string' ? h.data.error : undefined)}` : ''}
                <small>{when(h.atUtc)}</small>
              </div>
            </li>
          ))}
        </ol>
      </div>

      {pin && <PinReveal pin={pin.value} name={pin.name} onClose={() => setPin(null)} />}
    </article>
  )
}

function Decision(p: {
  r: RequestItem; busy: boolean; mode: 'idle' | 'rejecting'; setMode: (m: 'idle' | 'rejecting') => void
  reason: string; setReason: (s: string) => void; tried: boolean; viewerOnly: boolean; admin: boolean
  onApprove: () => void; onReject: () => void; onCancel: () => void
}) {
  if (p.r.status !== 'Pending' || p.r.direct) return null
  if (p.r.canDecide) {
    return (
      <section className="apx-decide apx-decide-bar" aria-label="Qərar">
        {p.mode === 'idle' ? (
          <div className="apx-decide-row">
            <button type="button" className="apx-btn apx-btn-go" disabled={p.busy} onClick={p.onApprove}>
              {p.busy ? 'Göndərilir…' : 'Təsdiqlə'}
            </button>
            <button type="button" className="apx-btn apx-btn-stop" disabled={p.busy} onClick={() => p.setMode('rejecting')}>Rədd et</button>
            <span className="apx-quiet">Təsdiqləyəndə işçi hesabı dərhal yaradılır.</span>
          </div>
        ) : (
          <>
            <label className="apx-field">
              <span>Rədd səbəbi (tabelçi bunu görəcək)</span>
              <textarea
                className="apx-textarea" value={p.reason} onChange={(e) => p.setReason(e.target.value)} autoFocus maxLength={500}
                aria-invalid={p.tried && p.reason.trim().length < 3}
                placeholder="Məsələn: şəxsiyyət vəsiqəsinin surəti yoxdur, telefon nömrəsi səhvdir"
              />
              {p.tried && p.reason.trim().length < 3 && <span className="apx-field-err" role="alert">Rədd səbəbini yazın (ən azı 3 hərf)</span>}
            </label>
            <div className="apx-decide-row">
              <button type="button" className="apx-btn apx-btn-stop" disabled={p.busy} onClick={p.onReject}>
                {p.busy ? 'Göndərilir…' : 'Rəddi təsdiqlə'}
              </button>
              <button type="button" className="apx-btn apx-btn-ghost" disabled={p.busy} onClick={() => p.setMode('idle')}>Geri</button>
            </div>
          </>
        )}
      </section>
    )
  }
  if (p.r.canCancel) {
    return (
      <section className="apx-decide" aria-label="Müraciətim">
        <div className="apx-decide-row">
          <button type="button" className="apx-btn" disabled={p.busy} onClick={p.onCancel}>{p.busy ? 'Göndərilir…' : 'Müraciəti ləğv et'}</button>
          <span className="apx-quiet">Qərarı strukturun operatoru verəcək. Öz müraciətinizi təsdiqləyə bilməzsiniz.</span>
        </div>
      </section>
    )
  }
  return (
    <p className="apx-lock" style={{ margin: 0 }}>
      <IconEye aria-hidden />
      {p.viewerOnly
        ? 'Siz Viewer rolundasınız: müraciətləri görürsünüz, qərar verə və dəyişə bilməzsiniz.'
        : p.r.requestedByMe
          ? 'Bu, sizin müraciətinizdir: qərarı strukturun başqa operatoru verəcək.'
          : p.admin
            ? 'Qərarı strukturun operatoru verir. Company Admin gündəlik müraciətlərin təsdiqçisi deyil.'
            : 'Qərarı strukturun operatoru verir.'}
    </p>
  )
}
