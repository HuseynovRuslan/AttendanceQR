import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { IconSearch } from '../../../components/icons'
import { TYPE_LABEL, approverIdFor, canCancel, type Employee } from './model'
import { RequestSheet } from './RequestSheet'
import { useProto, useScreenLoad } from './store'
import { EmptyState, ErrorState, Pips, Skeleton, StatusMark, employeeLabel, initials, periodLabel, personName, when } from './ui'

/** A Timekeeper's branch: its people, a new request in two taps, and where each request of theirs stands. */
export function TimekeeperWorkspace() {
  const { world: w, persona, cancel, loading } = useProto()
  const load = useScreenLoad(`timekeeper:${persona.id}`)
  const navigate = useNavigate()
  const [query, setQuery] = useState('')
  const [asking, setAsking] = useState<{ employeeId?: string } | null>(null)
  const branch = w.branches.find((b) => b.id === persona.branchId)
  if (!branch) return <EmptyState title="Filial təyin edilməyib" text="Tabelçi filiala təyin olunanda bu səhifə açılacaq." />

  const people: Employee[] = w.employees
    .filter((e) => e.branchId === branch.id)
    .filter((e) => !query.trim() || e.name.toLowerCase().includes(query.trim().toLowerCase()))
    .sort((a, b) => Number(b.active) - Number(a.active) || a.name.localeCompare(b.name, 'az'))
  const mine = w.requests.filter((r) => r.branchId === branch.id).sort((a, b) => b.createdAt.localeCompare(a.createdAt))

  if (load.state === 'error') return <div className="apx-panel"><ErrorState what="Filialınız" onRetry={load.retry} /></div>

  return (
    <>
      <div className="apx-head" style={{ marginTop: -8 }}>
        <p style={{ margin: 0 }}>
          {branch.name}: {people.filter((e) => e.active).length} aktiv işçi. Müraciətləriniz {personName(w, approverIdFor(w, branch.id))} tərəfindən həll olunur.
        </p>
        <button type="button" className="apx-btn apx-btn-go" onClick={() => setAsking({})}>Yeni müraciət</button>
      </div>

      <div className="apx-work">
        <section className="apx-panel" aria-label="Filialın işçiləri">
          <div className="apx-panel-h"><h2>İşçilər</h2><span>{people.length}</span></div>
          <div style={{ padding: '0 16px 10px' }}>
            <label className="apx-search">
              <IconSearch aria-hidden />
              <input className="apx-input" placeholder="İşçi axtar" value={query} onChange={(e) => setQuery(e.target.value)} aria-label="İşçi axtar" />
            </label>
          </div>
          {load.state === 'loading' ? <Skeleton /> : people.length === 0 ? (
            <EmptyState title="Heç kim tapılmadı" text={`«${query}» adına uyğun işçi yoxdur.`} />
          ) : (
            <div role="list" style={{ padding: '0 6px 8px' }}>
              {people.map((e) => (
                <div key={e.id} role="listitem" className={`apx-emp${e.active ? '' : ' apx-emp-off'}`}>
                  <span className="apx-av" aria-hidden>{initials(e.name)}</span>
                  <span style={{ minWidth: 0 }}>
                    <span className="apx-emp-n" style={{ display: 'block' }}>{e.name}</span>
                    <span className="apx-emp-p">{e.position}, növbə {e.shift}{e.active ? '' : ', deaktiv'}</span>
                  </span>
                  <button type="button" className="apx-btn apx-btn-sm" onClick={() => setAsking({ employeeId: e.id })} aria-label={`${e.name} üçün müraciət`}>
                    Müraciət
                  </button>
                </div>
              ))}
            </div>
          )}
        </section>

        <section className="apx-panel" aria-label="Müraciətlərim">
          <div className="apx-panel-h"><h2>Müraciətlər</h2><span>{mine.filter((r) => r.status === 'pending').length} gözləyir</span></div>
          {load.state === 'loading' ? <Skeleton /> : mine.length === 0 ? (
            <EmptyState title="Hələ müraciət yoxdur" text="«Yeni müraciət» ilə icazə, məzuniyyət və ya işçi dəyişikliyi istəyin." />
          ) : (
            <div className="apx-queue">
              {mine.map((r) => (
                <div key={r.id} className="apx-q" style={{ cursor: 'default' }}>
                  <button type="button" className="apx-q-t" style={{ background: 'none', border: 0, padding: 0, textAlign: 'left', cursor: 'pointer' }}
                    onClick={() => navigate(`../muracietler?id=${r.id}`)}>
                    {TYPE_LABEL[r.type]}
                  </button>
                  <span style={{ justifySelf: 'end' }}><StatusMark status={r.status} /></span>
                  <span className="apx-q-s">
                    {employeeLabel(w, r)}{periodLabel(r) ? `, ${periodLabel(r)}` : ''}
                  </span>
                  <span style={{ justifySelf: 'end' }}><Pips r={r} /></span>
                  {r.status === 'rejected' && r.decisionNote && (
                    <span className="apx-note apx-note-bad" style={{ gridColumn: '1 / -1', fontSize: 13, padding: '8px 10px' }}>
                      Rədd səbəbi: {r.decisionNote}
                    </span>
                  )}
                  <span className="apx-q-s" style={{ gridColumn: '1 / -1', display: 'flex', gap: 10, alignItems: 'center', flexWrap: 'wrap' }}>
                    <span>Göndərilib: {when(r.createdAt)}</span>
                    {canCancel(persona, r) && (
                      <button type="button" className="apx-btn apx-btn-sm apx-btn-ghost" disabled={loading} onClick={() => void cancel(r.id)}>
                        Ləğv et
                      </button>
                    )}
                  </span>
                </div>
              ))}
            </div>
          )}
        </section>
      </div>

      {asking && (
        <RequestSheet presetBranchId={branch.id} presetEmployeeId={asking.employeeId} onClose={() => setAsking(null)} />
      )}
    </>
  )
}
