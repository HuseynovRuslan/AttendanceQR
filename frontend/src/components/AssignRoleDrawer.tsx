import { useEffect, useMemo, useRef, useState } from 'react'
import {
  accessErrorText, assignRole, getAssignable, isAccessError, searchAccessPeople,
  type AccessScope, type Assignable, type AssignableRole, type PersonHit,
} from '../api/access'
import { SCOPE_LABEL, TEMPLATE_HINT } from '../lib/accessGroups'
import { IconX } from './icons'
import { matchesSearch } from '../lib/searchFold'

/**
 * Giving somebody a role, in three steps: who, which role, where. Only what the server says it would allow
 * is ever offered (GET /assignable) — the daily «make Aynur tabelçi of two branches» needs no permissions
 * expert, and no click ends in a refusal. The server checks everything again when it is saved.
 */
export function AssignRoleDrawer({
  person, labels = {}, onClose, onDone,
}: {
  person?: { id: string; fullName: string } | null
  /** Permission key → name, for the rare refusal that names one. */
  labels?: Record<string, string>
  onClose: () => void
  onDone: (message: string) => void
}) {
  const [who, setWho] = useState<{ id: string; fullName: string } | null>(person ?? null)
  const [query, setQuery] = useState('')
  const [hits, setHits] = useState<PersonHit[] | null>(null)
  const [offer, setOffer] = useState<Assignable | null>(null)
  const [roleId, setRoleId] = useState<string | null>(null)
  const [scope, setScope] = useState<AccessScope | null>(null)
  const [structureId, setStructureId] = useState<string>('')
  const [branchIds, setBranchIds] = useState<Set<string>>(new Set())
  const [branchFilter, setBranchFilter] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const closeRef = useRef(onClose)
  closeRef.current = onClose
  // Read through a ref: a parent passing a fresh object each render must not re-ask the server each render.
  const labelsRef = useRef(labels)
  labelsRef.current = labels

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') closeRef.current()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [])

  // Who: a small search of its own, so this drawer does not depend on the roster's paging.
  useEffect(() => {
    if (who) return
    const text = query.trim()
    if (text.length < 2) {
      setHits(null)
      return
    }
    const t = window.setTimeout(async () => {
      const { status, data } = await searchAccessPeople(text)
      setHits(status === 200 && Array.isArray(data) ? data : [])
    }, 250)
    return () => window.clearTimeout(t)
  }, [query, who])

  // What and where: asked once a person is chosen.
  useEffect(() => {
    if (!who) {
      setOffer(null)
      return
    }
    let live = true
    setOffer(null)
    setRoleId(null)
    setScope(null)
    setError(null)
    void getAssignable(who.id).then(({ status, data }) => {
      if (!live) return
      if (status === 200 && !isAccessError(data)) setOffer(data)
      else setError(accessErrorText(data, labelsRef.current))
    })
    return () => {
      live = false
    }
  }, [who])

  const role: AssignableRole | undefined = offer?.roles.find((r) => r.roleId === roleId)
  const scopes: AccessScope[] = role
    ? ([
        role.structureIds.length > 0 ? 'Structure' : null,
        role.locationIds.length > 0 ? 'Branch' : null,
        role.company ? 'Company' : null,
      ].filter(Boolean) as AccessScope[])
    : []

  function pickRole(r: AssignableRole) {
    setRoleId(r.roleId)
    setError(null)
    setBranchIds(new Set())
    setStructureId(r.structureIds.length === 1 ? r.structureIds[0] : '')
    // The usual shape of each template: an operator over a structure, a timekeeper over branches.
    const preferred: AccessScope =
      r.templateKey === 'timekeeper' && r.locationIds.length > 0 ? 'Branch'
      : r.structureIds.length > 0 ? 'Structure'
      : r.locationIds.length > 0 ? 'Branch'
      : 'Company'
    setScope(preferred)
  }

  const branchesOffered = useMemo(() => {
    if (!offer || !role) return []
    const allowed = new Set(role.locationIds)
    return offer.branches.filter((b) => allowed.has(b.id) && matchesSearch(branchFilter, b.name))
  }, [offer, role, branchFilter])

  const ready =
    !!who && !!role && !!scope && !busy
    && (scope === 'Company' || (scope === 'Structure' && !!structureId) || (scope === 'Branch' && branchIds.size > 0))

  async function save() {
    if (!who || !role || !scope) return
    setBusy(true)
    setError(null)
    const { status, data } = await assignRole({
      employeeId: who.id,
      roleId: role.roleId,
      scope,
      structureId: scope === 'Structure' ? structureId : null,
      locationIds: scope === 'Branch' ? [...branchIds] : null,
    })
    setBusy(false)
    if (status === 200 && !isAccessError(data)) {
      const n = data.created.length
      onDone(
        n === 0
          ? `${who.fullName}: «${role.name}» bu sahədə artıq var idi.`
          : `${who.fullName}: «${role.name}» verildi${n > 1 ? ` (${n} filial)` : ''}. Növbəti açılışdan qüvvədədir.`,
      )
    } else {
      setError(accessErrorText(data, labels))
    }
  }

  const blocked =
    offer && offer.roles.length === 0
      ? offer.reason === 'SelfGrant' ? 'Özünüzə rol verə bilməzsiniz.'
      : offer.reason === 'CannotManageOperator' ? 'Platforma operatorunun hüquqları buradan dəyişmir.'
      : 'Bu şəxsə verə biləcəyiniz rol yoxdur.'
      : null

  return (
    <>
      <div className="acc-backdrop" onClick={onClose} />
      <aside className="acc-drawer" role="dialog" aria-label="Rol ver">
        <div className="acc-drawer-head">
          <div>
            <h3 className="acc-drawer-title">Rol ver</h3>
            <p className="acc-drawer-sub">Kim, hansı rol, harada — verilən hüquq növbəti açılışdan qüvvədədir.</p>
          </div>
          <button type="button" className="acc-close" onClick={onClose} aria-label="Bağla">
            <IconX />
          </button>
        </div>

        <div className="acc-drawer-body">
          <div className="acc-step">
            <div className="acc-step-title">1 · Kim</div>
            {who ? (
              <div className="acc-row" style={{ padding: 0, borderBottom: 'none' }}>
                <div className="acc-row-main">
                  <span className="acc-row-name">{who.fullName}</span>
                </div>
                {!person && (
                  <button type="button" className="btn btn-sm" onClick={() => { setWho(null); setQuery('') }}>
                    Dəyiş
                  </button>
                )}
              </div>
            ) : (
              <>
                <input
                  className="inp"
                  autoFocus
                  placeholder="Ad və ya telefon (ən azı 2 hərf)"
                  value={query}
                  onChange={(e) => setQuery(e.target.value)}
                />
                {hits && (
                  <div className="acc-hits">
                    {hits.length === 0 && <div className="acc-empty" style={{ padding: 14 }}>Tapılmadı</div>}
                    {hits.map((h) => (
                      <button
                        key={h.id}
                        type="button"
                        className="acc-hit"
                        disabled={h.isOperator}
                        onClick={() => setWho({ id: h.id, fullName: h.fullName })}
                      >
                        <b>{h.fullName}</b>
                        <span>
                          {[h.position, h.home].filter(Boolean).join(' · ') || '—'}
                          {h.isOperator ? ' · platforma operatoru' : ''}
                        </span>
                      </button>
                    ))}
                  </div>
                )}
              </>
            )}
          </div>

          {who && !offer && !error && <p className="muted">Yüklənir…</p>}
          {blocked && <div className="fb fb-err">{blocked}</div>}

          {offer && offer.roles.length > 0 && (
            <div className="acc-step">
              <div className="acc-step-title">2 · Hansı rol</div>
              {offer.roles.map((r) => (
                <button
                  key={r.roleId}
                  type="button"
                  className={`acc-option${r.roleId === roleId ? ' on' : ''}`}
                  onClick={() => pickRole(r)}
                >
                  <b>{r.name}</b>
                  <span>{(r.templateKey && TEMPLATE_HINT[r.templateKey]) || 'Şirkətin öz rolu'}</span>
                </button>
              ))}
            </div>
          )}

          {role && offer && (
            <div className="acc-step">
              <div className="acc-step-title">3 · Harada</div>
              {scopes.length > 1 && (
                <div className="acc-tabs">
                  {scopes.map((s) => (
                    <button key={s} type="button" className={`acc-tab${scope === s ? ' on' : ''}`} onClick={() => setScope(s)}>
                      {s === 'Branch' ? 'Filial(lar)' : SCOPE_LABEL[s]}
                    </button>
                  ))}
                </div>
              )}
              {scope === 'Company' && <p className="muted" style={{ margin: 0 }}>Bütün şirkət — bütün filiallar, gələcəkdə açılacaqlar da.</p>}
              {scope === 'Structure' && (
                <select className="inp" value={structureId} onChange={(e) => setStructureId(e.target.value)}>
                  <option value="">Struktur seçin…</option>
                  {offer.structures.filter((s) => role.structureIds.includes(s.id)).map((s) => (
                    <option key={s.id} value={s.id}>{s.name}</option>
                  ))}
                </select>
              )}
              {scope === 'Branch' && (
                <>
                  <input className="inp" placeholder="Filial axtar…" value={branchFilter} onChange={(e) => setBranchFilter(e.target.value)} />
                  <div className="acc-pick">
                    {branchesOffered.map((b) => (
                      <label key={b.id} className="acc-check">
                        <input
                          type="checkbox"
                          checked={branchIds.has(b.id)}
                          onChange={(e) => {
                            const next = new Set(branchIds)
                            if (e.target.checked) next.add(b.id)
                            else next.delete(b.id)
                            setBranchIds(next)
                          }}
                        />
                        {b.name}
                      </label>
                    ))}
                    {branchesOffered.length === 0 && <div className="acc-empty" style={{ padding: 14 }}>Filial yoxdur</div>}
                  </div>
                  {branchIds.size > 0 && <span className="muted">{branchIds.size} filial seçilib</span>}
                </>
              )}
            </div>
          )}

          {error && <div className="fb fb-err">{error}</div>}
        </div>

        <div className="acc-drawer-foot">
          <button type="button" className="btn btn-sm" onClick={onClose}>Ləğv et</button>
          <button type="button" className="btn btn-sm btn-primary" disabled={!ready} onClick={() => void save()}>
            {busy ? 'Verilir…' : 'Ver'}
          </button>
        </div>
      </aside>
    </>
  )
}
