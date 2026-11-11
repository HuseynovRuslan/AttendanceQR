import { useMemo, useState } from 'react'
import { IconArrowLeft, IconChevronRight, IconSearch } from '../../../components/icons'
import { branchesInScope, canCreate, type Branch, type Employee } from './model'
import { EmployeeSheet } from './EmployeeSheet'
import { useProto, useScreenLoad } from './store'
import { EmptyState, ErrorState, RoleMark, Skeleton, initials, personName } from './ui'

const FREE = '__free__'

/**
 * Company → Structure → Branch → People, one level per column. Each level shows its own appointments: Operator
 * and Viewer belong to a structure, Timekeeper and Viewer to a branch — so where a responsibility sits is read off
 * the column it appears in. Branches whose structure is not decided yet stay in their own group, unassigned.
 */
export function StructureExplorer() {
  const { world: w, persona } = useProto()
  const load = useScreenLoad(`explorer:${persona.id}`)
  const scope = useMemo(() => branchesInScope(w, persona), [w, persona])
  const scopeIds = useMemo(() => new Set(scope.map((b) => b.id)), [scope])
  const structures = w.structures.filter((s) => scope.some((b) => b.structureId === s.id))
  const free = scope.filter((b) => !b.structureId)

  const [structureId, setStructureId] = useState<string | null>(structures[0]?.id ?? (free.length ? FREE : null))
  // The first branch open from the start, so the three levels read together on a wide screen.
  const [branchId, setBranchId] = useState<string | null>(
    () => scope.find((b) => (structures[0] ? b.structureId === structures[0].id : !b.structureId))?.id ?? null,
  )
  const [depth, setDepth] = useState(0)
  const [query, setQuery] = useState('')
  const [person, setPerson] = useState<Employee | null>(null)

  const staffOf = (b: Branch) => w.employees.filter((e) => e.branchId === b.id)
  const pendingOf = (b: Branch) => w.requests.filter((r) => r.branchId === b.id && r.status === 'pending').length
  const branches = structureId === FREE ? free : scope.filter((b) => b.structureId === structureId)
  const structure = w.structures.find((s) => s.id === structureId)
  const branch = branchId ? w.branches.find((b) => b.id === branchId && scopeIds.has(b.id)) : undefined
  const people = branch
    ? staffOf(branch)
      .filter((e) => !query.trim() || e.name.toLowerCase().includes(query.trim().toLowerCase()))
      .sort((a, b) => Number(b.active) - Number(a.active) || a.name.localeCompare(b.name, 'az'))
    : []

  const totalPeople = scope.reduce((n, b) => n + staffOf(b).length, 0)

  if (load.state === 'error') return <div className="apx-panel"><ErrorState what="Struktur xəritəsi" onRetry={load.retry} /></div>

  return (
    <>
      <section className="apx-panel apx-co" aria-label="Şirkət">
        <div className="apx-co-name">{w.companyName}</div>
        <div className="apx-co-fig"><b>{structures.length}</b><span>struktur</span></div>
        <div className="apx-co-fig"><b>{scope.length}</b><span>filial</span></div>
        <div className="apx-co-fig"><b>{totalPeople}</b><span>işçi</span></div>
        {persona.kind === 'companyAdmin' && (
          <div className="apx-co-fig"><b>{free.length}</b><span>struktura bağlanmamış filial</span></div>
        )}
      </section>

      <div className="apx-cols" data-depth={depth}>
        {/* Level 1: structures */}
        <section className="apx-panel apx-col" aria-label="Strukturlar">
          <header className="apx-col-h"><h2>Strukturlar</h2></header>
          {load.state === 'loading' ? <Skeleton rows={5} /> : (
            <div className="apx-col-list">
              {structures.map((s) => {
                const bs = scope.filter((b) => b.structureId === s.id)
                return (
                  <button
                    key={s.id} type="button" className="apx-row" aria-current={structureId === s.id}
                    onClick={() => { setStructureId(s.id); setBranchId(null); setDepth(1) }}
                  >
                    <span className="apx-row-t">{s.name}</span>
                    <span className="apx-row-n">{bs.length}</span>
                    <span className="apx-row-s">
                      <span>{bs.reduce((n, b) => n + staffOf(b).length, 0)} işçi</span>
                      <span>Operator: {personName(w, s.operatorId)}</span>
                    </span>
                  </button>
                )
              })}
              {free.length > 0 && (
                <>
                  <div className="apx-group-l">Bölgüsü hələ təsdiqlənməyib</div>
                  <button
                    type="button" className="apx-row apx-row-free" aria-current={structureId === FREE}
                    onClick={() => { setStructureId(FREE); setBranchId(null); setDepth(1) }}
                  >
                    <span className="apx-row-t">Struktursuz filiallar</span>
                    <span className="apx-row-n">{free.length}</span>
                    <span className="apx-row-s"><span>Müraciətləri şirkət administratoru həll edir</span></span>
                  </button>
                </>
              )}
            </div>
          )}
        </section>

        {/* Level 2: branches of the structure */}
        <section className="apx-panel apx-col" aria-label="Filiallar">
          <header className="apx-col-h">
            <button type="button" className="apx-btn apx-btn-ghost apx-btn-sm apx-back" onClick={() => setDepth(0)}>
              <IconArrowLeft /> Strukturlar
            </button>
            <h2>{structureId === FREE ? 'Struktursuz filiallar' : structure?.name ?? 'Filiallar'} <small>{branches.length} filial</small></h2>
            {structure && (
              <div className="apx-roles">
                <div className="apx-roles-line"><RoleMark kind="operator" /> {personName(w, structure.operatorId)}</div>
                <div className="apx-roles-line">
                  <RoleMark kind="viewer" />
                  {structure.viewerIds.length ? structure.viewerIds.map((id) => personName(w, id)).join(', ') : <span className="apx-none">təyin edilməyib</span>}
                </div>
              </div>
            )}
            {structureId === FREE && (
              <p className="apx-quiet" style={{ margin: 0 }}>
                Bu filialların strukturu hələ təsdiqlənməyib. Təxminlə heç bir struktura yazılmayıb.
              </p>
            )}
          </header>
          {load.state === 'loading' ? <Skeleton /> : branches.length === 0 ? (
            <EmptyState title="Bu strukturda filial yoxdur" text="Filiallar struktura şirkət administratoru tərəfindən bağlanır." />
          ) : (
            <div className="apx-col-list">
              {branches.map((b) => {
                const pending = pendingOf(b)
                return (
                  <button
                    key={b.id} type="button" className="apx-row" aria-current={branchId === b.id}
                    onClick={() => { setBranchId(b.id); setQuery(''); setDepth(2) }}
                  >
                    <span className="apx-row-t">{b.name}</span>
                    <span className="apx-row-n">{staffOf(b).length}</span>
                    <span className="apx-row-s">
                      <span>{b.timekeeperId ? `Tabelçi: ${personName(w, b.timekeeperId)}` : 'Tabelçi təyin edilməyib'}</span>
                      {pending > 0 && <span className="apx-tag apx-tag-warn">{pending} gözləyir</span>}
                    </span>
                  </button>
                )
              })}
            </div>
          )}
        </section>

        {/* Level 3: one branch, its appointments and its people */}
        <section className="apx-panel apx-col" aria-label="Filial">
          {!branch ? (
            <EmptyState title="Filial seçin" text="Filialın tabelçisi, müşahidəçisi və işçiləri burada açılacaq." />
          ) : (
            <>
              <header className="apx-col-h">
                <button type="button" className="apx-btn apx-btn-ghost apx-btn-sm apx-back" onClick={() => setDepth(1)}>
                  <IconArrowLeft /> Filiallar
                </button>
                <nav className="apx-lineage" aria-label="Yer">
                  <span>{w.companyName}</span>
                  <IconChevronRight aria-hidden />
                  <button type="button" onClick={() => setDepth(1)}>{structure?.name ?? 'Struktursuz'}</button>
                  <IconChevronRight aria-hidden />
                  <span>{branch.name}</span>
                </nav>
                <h2>{branch.name} <small>{staffOf(branch).length} işçi</small></h2>
                <div className="apx-roles">
                  <div className="apx-roles-line">
                    <RoleMark kind="timekeeper" />
                    {branch.timekeeperId ? personName(w, branch.timekeeperId) : <span className="apx-none">təyin edilməyib</span>}
                  </div>
                  <div className="apx-roles-line">
                    <RoleMark kind="viewer">Filial müşahidəçisi</RoleMark>
                    {branch.viewerIds.length ? branch.viewerIds.map((id) => personName(w, id)).join(', ') : <span className="apx-quiet">yoxdur</span>}
                  </div>
                </div>
                <label className="apx-search">
                  <IconSearch aria-hidden />
                  <input
                    className="apx-input" placeholder="İşçi axtar" value={query} onChange={(e) => setQuery(e.target.value)}
                    aria-label="Bu filialda işçi axtar"
                  />
                </label>
              </header>
              {load.state === 'loading' ? <Skeleton /> : people.length === 0 ? (
                <EmptyState title="Heç kim tapılmadı" text={query ? `«${query}» adına uyğun işçi yoxdur.` : 'Bu filialda işçi yoxdur.'} />
              ) : (
                <ul className="apx-col-list" aria-label="İşçilər">
                  {people.map((e) => (
                    <li key={e.id}>
                    <button
                      type="button" className={`apx-emp apx-row${e.active ? '' : ' apx-emp-off'}`}
                      style={{ gridTemplateColumns: '32px minmax(0,1fr) auto' }} onClick={() => setPerson(e)}
                    >
                      <span className="apx-av" aria-hidden>{initials(e.name)}</span>
                      <span style={{ minWidth: 0 }}>
                        <span className="apx-emp-n" style={{ display: 'block' }}>{e.name}</span>
                        <span className="apx-emp-p">{e.position}, növbə {e.shift}</span>
                      </span>
                      {!e.active && <span className="apx-tag">Deaktiv</span>}
                    </button>
                    </li>
                  ))}
                </ul>
              )}
            </>
          )}
        </section>
      </div>

      {person && (
        <EmployeeSheet
          employee={w.employees.find((e) => e.id === person.id) ?? person}
          canAct={canCreate(w, persona, person.branchId)}
          onClose={() => setPerson(null)}
        />
      )}
    </>
  )
}
