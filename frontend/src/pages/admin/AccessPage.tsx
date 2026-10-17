import { useCallback, useEffect, useMemo, useState } from 'react'
import {
  accessErrorText, archiveAccessRole, archiveStructure, createAccessRole, createStructure, getAccessCatalogue,
  getAccessRoles, getAssignments, getStructures, isAccessError, moveBranches, renameStructure, restoreAccessRole,
  restoreStructure, revokeAssignment, updateAccessRole,
  type AccessRoleRow, type AssignmentRow, type PermissionInfo, type RoleTemplateInfo, type StructureBranch,
  type StructureRow,
} from '../../api/access'
import { AssignRoleDrawer } from '../../components/AssignRoleDrawer'
import { EmployeeLink } from '../../components/EmployeeLink'
import { IconX } from '../../components/icons'
import { groupPermissions, TEMPLATE_HINT } from '../../lib/accessGroups'
import { fmtDateOfInstant } from '../../lib/format'

type Tab = 'assignments' | 'roles' | 'structures'
type Catalogue = { permissions: PermissionInfo[]; templates: RoleTemplateInfo[] }
type Feedback = { ok: boolean; text: string } | null

/**
 * Səlahiyyətlər — who holds which role where (Phase 3 of docs/strukturlar-ve-selahiyyetler.md).
 *
 * Three screens behind one tab bar. «Təyinatlar» is the daily one: give somebody a role, take one away.
 * «Rollar» is where a company shapes its own roles. «Strukturlar» exists here because a structure operator
 * cannot be appointed over a structure that has never been made.
 *
 * Every change counts from the person's next request — the permission model reads the database every time,
 * so nobody is signed out. Today's admins and managers keep working exactly as before while the company is
 * in Legacy or Shadow mode; these roles start deciding in Phase 5.
 */
export function AccessPage() {
  const [tab, setTab] = useState<Tab>('assignments')
  const [catalogue, setCatalogue] = useState<Catalogue | null>(null)

  useEffect(() => {
    void getAccessCatalogue().then(({ status, data }) => {
      if (status === 200) setCatalogue(data)
    })
  }, [])

  const labels = useMemo(
    () => Object.fromEntries((catalogue?.permissions ?? []).map((p) => [p.key, p.label])),
    [catalogue],
  )

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
      <div className="acc-tabs" role="tablist">
        {([
          ['assignments', 'Təyinatlar'],
          ['roles', 'Rollar'],
          ['structures', 'Strukturlar'],
        ] as [Tab, string][]).map(([key, label]) => (
          <button key={key} type="button" role="tab" aria-selected={tab === key} className={`acc-tab${tab === key ? ' on' : ''}`} onClick={() => setTab(key)}>
            {label}
          </button>
        ))}
      </div>
      {tab === 'assignments' && <AssignmentsTab labels={labels} />}
      {tab === 'roles' && <RolesTab catalogue={catalogue} labels={labels} />}
      {tab === 'structures' && <StructuresTab />}
    </div>
  )
}

// ───────────────────────────── Təyinatlar ─────────────────────────────

function AssignmentsTab({ labels }: { labels: Record<string, string> }) {
  const [rows, setRows] = useState<AssignmentRow[] | null>(null)
  const [roles, setRoles] = useState<AccessRoleRow[]>([])
  const [query, setQuery] = useState('')
  const [roleId, setRoleId] = useState('')
  const [source, setSource] = useState<'manual' | 'system' | 'all'>('manual')
  const [showRevoked, setShowRevoked] = useState(false)
  const [assigning, setAssigning] = useState(false)
  const [busyId, setBusyId] = useState<string | null>(null)
  const [feedback, setFeedback] = useState<Feedback>(null)

  const load = useCallback(async () => {
    const [a, r] = await Promise.all([getAssignments({ includeRevoked: showRevoked }), getAccessRoles()])
    setRows(a.status === 200 && Array.isArray(a.data) ? a.data : [])
    if (r.status === 200 && Array.isArray(r.data)) setRoles(r.data)
  }, [showRevoked])

  useEffect(() => {
    void load()
  }, [load])

  const shown = useMemo(() => {
    const q = query.trim().toLocaleLowerCase('az')
    return (rows ?? []).filter((a) =>
      (source === 'all' || (source === 'manual' ? !a.system : a.system))
      && (!roleId || a.roleId === roleId)
      && (!q || (a.employeeName ?? '').toLocaleLowerCase('az').includes(q) || a.area.toLocaleLowerCase('az').includes(q)))
  }, [rows, query, roleId, source])

  async function revoke(a: AssignmentRow) {
    if (!window.confirm(`${a.employeeName}: «${a.roleName}» (${a.area}) geri alınsın?\n\nNövbəti açılışdan qüvvədədir.`)) return
    setBusyId(a.id)
    setFeedback(null)
    const { status, data } = await revokeAssignment(a.id)
    setBusyId(null)
    if (status === 200) {
      setFeedback({ ok: true, text: `${a.employeeName}: «${a.roleName}» geri alındı.` })
      void load()
    } else {
      setFeedback({ ok: false, text: accessErrorText(data, labels) })
    }
  }

  return (
    <div className="card card-pad">
      <div className="acc-toolbar" style={{ marginTop: 0 }}>
        <input className="inp" placeholder="Ad və ya sahə axtar…" value={query} onChange={(e) => setQuery(e.target.value)} />
        <select className="inp" style={{ maxWidth: 220 }} value={roleId} onChange={(e) => setRoleId(e.target.value)}>
          <option value="">Bütün rollar</option>
          {roles.map((r) => <option key={r.id} value={r.id}>{r.name}</option>)}
        </select>
        <select className="inp" style={{ maxWidth: 230 }} value={source} onChange={(e) => setSource(e.target.value as typeof source)}>
          <option value="manual">Əl ilə verilənlər</option>
          <option value="system">İşçi formasından gələnlər</option>
          <option value="all">Hamısı</option>
        </select>
        <label className="acc-check" style={{ padding: 0 }}>
          <input type="checkbox" checked={showRevoked} onChange={(e) => setShowRevoked(e.target.checked)} />
          Geri alınanlar da
        </label>
        <span style={{ flex: 1 }} />
        <button type="button" className="btn btn-sm btn-primary" onClick={() => { setFeedback(null); setAssigning(true) }}>
          + Rol ver
        </button>
      </div>

      {feedback && <div className={`fb ${feedback.ok ? 'fb-ok' : 'fb-err'}`} style={{ marginBottom: 10 }}>{feedback.text}</div>}

      {rows === null ? (
        <p className="muted">Yüklənir…</p>
      ) : shown.length === 0 ? (
        <div className="acc-empty">
          {source === 'manual' && !query && !roleId
            ? 'Hələ heç kimə yeni rol verilməyib. Bugünkü admin və menecerlər işçi formasından gələn hüquqlarla işləyir — «Hamısı» seçin.'
            : 'Heç nə tapılmadı.'}
        </div>
      ) : (
        <div className="acc-list">
          {shown.map((a) => (
            <div key={a.id} className="acc-row" style={a.revokedAtUtc ? { opacity: 0.55 } : undefined}>
              <div className="acc-row-main">
                <span className="acc-row-name">
                  <EmployeeLink id={a.employeeId} name={a.employeeName ?? '—'} />
                  {a.employeeHome && <span className="muted" style={{ fontWeight: 400, fontSize: 12 }}> · {a.employeeHome}</span>}
                </span>
                <div className="acc-row-meta">
                  <span className="acc-badge role">{a.roleName}</span>
                  <span className="acc-badge area">{a.area}</span>
                  {a.system && <span className="acc-badge sys" title="İşçi formasındakı vəzifə və filiallardan gəlir — orada dəyişin">işçi formasından</span>}
                  {!a.system && a.createdByName && <span>verən: {a.createdByName} · {fmtDateOfInstant(a.createdAtUtc)}</span>}
                  {a.revokedAtUtc && <span>geri alan: {a.revokedByName ?? '—'} · {fmtDateOfInstant(a.revokedAtUtc)}</span>}
                </div>
              </div>
              {a.canRevoke && (
                <button type="button" className="btn btn-sm" disabled={busyId === a.id} onClick={() => void revoke(a)}>
                  Geri al
                </button>
              )}
            </div>
          ))}
        </div>
      )}

      {assigning && (
        <AssignRoleDrawer
          labels={labels}
          onClose={() => setAssigning(false)}
          onDone={(text) => {
            setAssigning(false)
            setFeedback({ ok: true, text })
            void load()
          }}
        />
      )}
    </div>
  )
}

// ───────────────────────────── Rollar ─────────────────────────────

function RolesTab({ catalogue, labels }: { catalogue: Catalogue | null; labels: Record<string, string> }) {
  const [rows, setRows] = useState<AccessRoleRow[] | null>(null)
  const [showArchived, setShowArchived] = useState(false)
  const [open, setOpen] = useState<Set<string>>(new Set())
  const [editor, setEditor] = useState<{ role: AccessRoleRow | null } | null>(null)
  const [feedback, setFeedback] = useState<Feedback>(null)

  const load = useCallback(async () => {
    const { status, data } = await getAccessRoles(showArchived)
    setRows(status === 200 && Array.isArray(data) ? data : [])
  }, [showArchived])

  useEffect(() => {
    void load()
  }, [load])

  async function archive(r: AccessRoleRow) {
    if (!window.confirm(`«${r.name}» arxivə göndərilsin? Sonra bərpa etmək olar.`)) return
    const { status, data } = await archiveAccessRole(r.id)
    setFeedback(status === 200 ? { ok: true, text: `«${r.name}» arxivdədir.` } : { ok: false, text: accessErrorText(data, labels) })
    void load()
  }

  async function restore(r: AccessRoleRow) {
    const { status, data } = await restoreAccessRole(r.id)
    setFeedback(status === 200 ? { ok: true, text: `«${r.name}» bərpa olundu.` } : { ok: false, text: accessErrorText(data, labels) })
    void load()
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
      <div className="acc-toolbar" style={{ margin: 0 }}>
        <label className="acc-check" style={{ padding: 0 }}>
          <input type="checkbox" checked={showArchived} onChange={(e) => setShowArchived(e.target.checked)} />
          Arxivdəkilər də
        </label>
        <span style={{ flex: 1 }} />
        <button type="button" className="btn btn-sm btn-primary" disabled={!catalogue} onClick={() => { setFeedback(null); setEditor({ role: null }) }}>
          + Yeni rol
        </button>
      </div>
      {feedback && <div className={`fb ${feedback.ok ? 'fb-ok' : 'fb-err'}`}>{feedback.text}</div>}

      {rows === null ? (
        <p className="muted">Yüklənir…</p>
      ) : (
        <div className="acc-grid">
          {rows.map((r) => {
            const expanded = open.has(r.id)
            return (
              <div key={r.id} className="card acc-role-card" style={r.archived ? { opacity: 0.6 } : undefined}>
                <div style={{ display: 'flex', gap: 6, alignItems: 'center', flexWrap: 'wrap' }}>
                  <b style={{ fontSize: 15, color: 'var(--c900)' }}>{r.name}</b>
                  {r.isLocked && <span className="acc-badge lock" title="Kodla müəyyən olunur, dəyişdirilmir">kilidli</span>}
                  {r.isLegacy && <span className="acc-badge sys">keçid rolu</span>}
                  {r.archived && <span className="acc-badge">arxivdə</span>}
                </div>
                <p className="muted" style={{ fontSize: 13 }}>
                  {r.description ?? ((r.templateKey && TEMPLATE_HINT[r.templateKey]) || '')}
                </p>
                <span className="acc-row-meta">
                  {r.permissions.length} hüquq · {r.activeAssignments > 0 ? `${r.activeAssignments} təyinat` : 'heç kimə verilməyib'}
                </span>
                <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
                  <button type="button" className="btn btn-sm" onClick={() => {
                    const next = new Set(open)
                    if (expanded) next.delete(r.id)
                    else next.add(r.id)
                    setOpen(next)
                  }}>
                    {expanded ? 'Gizlət' : 'Hüquqlar'}
                  </button>
                  {!r.isLocked && !r.archived && (
                    <>
                      <button type="button" className="btn btn-sm" onClick={() => { setFeedback(null); setEditor({ role: r }) }}>Redaktə</button>
                      <button type="button" className="btn btn-sm btn-ghost" onClick={() => void archive(r)}>Arxivlə</button>
                    </>
                  )}
                  {r.archived && <button type="button" className="btn btn-sm" onClick={() => void restore(r)}>Bərpa et</button>}
                </div>
                {expanded && (
                  <div>
                    {groupPermissions(r.permissions).map((g) => (
                      <div key={g.title} className="acc-perm-group">
                        <div className="acc-perm-title">{g.title}</div>
                        {g.keys.map((k) => <span key={k} style={{ fontSize: 13, color: 'var(--c700)' }}>· {labels[k] ?? k}</span>)}
                      </div>
                    ))}
                  </div>
                )}
              </div>
            )
          })}
        </div>
      )}

      {editor && catalogue && (
        <RoleEditor
          catalogue={catalogue}
          role={editor.role}
          labels={labels}
          onClose={() => setEditor(null)}
          onSaved={(text) => {
            setEditor(null)
            setFeedback({ ok: true, text })
            void load()
          }}
        />
      )}
    </div>
  )
}

function RoleEditor({
  catalogue, role, labels, onClose, onSaved,
}: {
  catalogue: Catalogue
  role: AccessRoleRow | null
  labels: Record<string, string>
  onClose: () => void
  onSaved: (text: string) => void
}) {
  const [name, setName] = useState(role?.name ?? '')
  const [description, setDescription] = useState(role?.description ?? '')
  const [picked, setPicked] = useState<Set<string>>(new Set(role?.permissions ?? []))
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const byKey = useMemo(() => new Map(catalogue.permissions.map((p) => [p.key, p])), [catalogue])
  const groups = useMemo(() => groupPermissions(catalogue.permissions.map((p) => p.key)), [catalogue])

  // A new role usually starts as a copy of a template — «Tabelçi, plus attendance correction».
  function startFrom(templateKey: string) {
    const template = catalogue.templates.find((t) => t.key === templateKey)
    if (!template) return
    void getAccessRoles().then(({ data }) => {
      const source = Array.isArray(data) ? data.find((r) => r.templateKey === templateKey) : undefined
      if (source) setPicked(new Set(source.permissions))
      if (!name.trim()) setName(`${template.name} (öz)`)
    })
  }

  async function save() {
    setBusy(true)
    setError(null)
    const body = { name: name.trim(), description: description.trim() || null, permissions: [...picked] }
    const { status, data } = role ? await updateAccessRole(role.id, body) : await createAccessRole(body)
    setBusy(false)
    if (status === 200 && !isAccessError(data)) onSaved(role ? `«${body.name}» yadda saxlandı.` : `«${body.name}» yaradıldı.`)
    else setError(accessErrorText(data, labels))
  }

  return (
    <>
      <div className="acc-backdrop" onClick={onClose} />
      <aside className="acc-drawer" role="dialog" aria-label={role ? 'Rolu redaktə et' : 'Yeni rol'}>
        <div className="acc-drawer-head">
          <div>
            <h3 className="acc-drawer-title">{role ? 'Rolu redaktə et' : 'Yeni rol'}</h3>
            <p className="acc-drawer-sub">
              {role && role.activeAssignments > 0
                ? `Dəyişiklik bu rolu daşıyan ${role.activeAssignments} təyinata dərhal aid olur.`
                : 'Rolu sonra istənilən adama, istənilən sahədə vermək olar.'}
            </p>
          </div>
          <button type="button" className="acc-close" onClick={onClose} aria-label="Bağla"><IconX /></button>
        </div>
        <div className="acc-drawer-body">
          {!role && (
            <div className="acc-step">
              <div className="acc-step-title">Şablondan başla</div>
              <select className="inp" defaultValue="" onChange={(e) => e.target.value && startFrom(e.target.value)}>
                <option value="">— boş —</option>
                {catalogue.templates.filter((t) => !t.isLegacy).map((t) => <option key={t.key} value={t.key}>{t.name}</option>)}
              </select>
            </div>
          )}
          <div className="acc-step">
            <div className="acc-step-title">Ad</div>
            <input className="inp" maxLength={120} value={name} onChange={(e) => setName(e.target.value)} placeholder="Məs. Gecə növbəsi nəzarətçisi" />
            <textarea className="inp" rows={2} maxLength={500} value={description} onChange={(e) => setDescription(e.target.value)} placeholder="Qısa təsvir (istəyə bağlı)" />
          </div>
          <div className="acc-step">
            <div className="acc-step-title">Hüquqlar · {picked.size}</div>
            {groups.map((g) => (
              <div key={g.title} className="acc-perm-group">
                <div className="acc-perm-title">{g.title}</div>
                {g.keys.map((k) => {
                  const info = byKey.get(k)!
                  return (
                    <label key={k} className="acc-check">
                      <input
                        type="checkbox"
                        checked={picked.has(k)}
                        onChange={(e) => {
                          const next = new Set(picked)
                          if (e.target.checked) next.add(k)
                          else next.delete(k)
                          setPicked(next)
                        }}
                      />
                      {info.label}
                      {info.companyOnly && <small>yalnız bütün şirkət üzrə</small>}
                    </label>
                  )
                })}
              </div>
            ))}
          </div>
          {error && <div className="fb fb-err">{error}</div>}
        </div>
        <div className="acc-drawer-foot">
          <button type="button" className="btn btn-sm" onClick={onClose}>Ləğv et</button>
          <button type="button" className="btn btn-sm btn-primary" disabled={busy || name.trim().length < 2 || picked.size === 0} onClick={() => void save()}>
            {busy ? 'Saxlanılır…' : 'Yadda saxla'}
          </button>
        </div>
      </aside>
    </>
  )
}

// ───────────────────────────── Strukturlar ─────────────────────────────

const STRUCTURE_ERROR: Record<string, string> = {
  NameTaken: 'Bu adda struktur artıq var.',
  NameRequired: 'Strukturun adını yazın.',
  StructureNotEmpty: 'Strukturda filial var — əvvəl filialları çıxarın.',
  StructureArchived: 'Struktur arxivdədir.',
  StructureNotFound: 'Struktur tapılmadı — səhifəni yeniləyin.',
  LocationNotFound: 'Filial tapılmadı.',
  NoLocations: 'Filial seçin.',
  TooManyLocations: 'Bir dəfədə çox filial seçilib.',
}

function structureError(data: unknown): string {
  const code = data && typeof data === 'object' && 'error' in data ? String((data as { error: unknown }).error) : ''
  return STRUCTURE_ERROR[code] ?? 'Əməliyyat alınmadı.'
}

function StructuresTab() {
  const [data, setData] = useState<{ structures: StructureRow[]; unassigned: StructureBranch[] } | null>(null)
  const [newName, setNewName] = useState('')
  const [picking, setPicking] = useState<StructureRow | null>(null)
  const [feedback, setFeedback] = useState<Feedback>(null)

  const load = useCallback(async () => {
    const { status, data: d } = await getStructures()
    if (status === 200) setData(d)
  }, [])

  useEffect(() => {
    void load()
  }, [load])

  async function create() {
    const name = newName.trim()
    if (!name) return
    const { status, data: d } = await createStructure(name)
    if (status === 200 || status === 201) {
      setNewName('')
      setFeedback({ ok: true, text: `«${name}» yaradıldı — indi filiallarını seçin.` })
      void load()
    } else setFeedback({ ok: false, text: structureError(d) })
  }

  async function rename(s: StructureRow) {
    const name = window.prompt('Strukturun yeni adı', s.name)?.trim()
    if (!name || name === s.name) return
    const { status, data: d } = await renameStructure(s.id, name)
    setFeedback(status === 200 ? { ok: true, text: `«${name}» adlandırıldı.` } : { ok: false, text: structureError(d) })
    void load()
  }

  async function archive(s: StructureRow) {
    if (!window.confirm(`«${s.name}» arxivə göndərilsin?`)) return
    const { status, data: d } = await archiveStructure(s.id)
    setFeedback(status === 200 ? { ok: true, text: `«${s.name}» arxivdədir.` } : { ok: false, text: structureError(d) })
    void load()
  }

  async function restore(s: StructureRow) {
    const { status, data: d } = await restoreStructure(s.id)
    setFeedback(status === 200 ? { ok: true, text: `«${s.name}» bərpa olundu.` } : { ok: false, text: structureError(d) })
    void load()
  }

  const allBranches = useMemo(() => {
    if (!data) return []
    const rows = data.structures.flatMap((s) => s.locations.map((l) => ({ ...l, structure: s.name, structureId: s.id as string | null })))
    return [...rows, ...data.unassigned.map((l) => ({ ...l, structure: null as string | null, structureId: null as string | null }))]
      .sort((a, b) => a.name.localeCompare(b.name, 'az'))
  }, [data])

  return (
    <div className="card card-pad">
      <p className="muted" style={{ marginTop: 0, fontSize: 13 }}>
        Struktur — bir neçə filialın birlikdə idarəsi. Struktur operatoru strukturun bütün filiallarında işləyir;
        filial strukturdan çıxanda hüquq da onunla gedir. Strukturlardan istifadə etmək məcburi deyil.
      </p>
      <div className="acc-toolbar">
        <input className="inp" placeholder="Yeni struktur adı" value={newName} maxLength={120}
          onChange={(e) => setNewName(e.target.value)} onKeyDown={(e) => { if (e.key === 'Enter') void create() }} />
        <button type="button" className="btn btn-sm btn-primary" disabled={!newName.trim()} onClick={() => void create()}>Yarat</button>
        {data && <span className="muted" style={{ fontSize: 12 }}>Struktursuz filial: {data.unassigned.length}</span>}
      </div>
      {feedback && <div className={`fb ${feedback.ok ? 'fb-ok' : 'fb-err'}`} style={{ marginBottom: 10 }}>{feedback.text}</div>}
      {!data ? (
        <p className="muted">Yüklənir…</p>
      ) : data.structures.length === 0 ? (
        <div className="acc-empty">Hələ struktur yoxdur.</div>
      ) : (
        <div className="acc-list">
          {data.structures.map((s) => (
            <div key={s.id} className="acc-row" style={s.archived ? { opacity: 0.55 } : undefined}>
              <div className="acc-row-main">
                <span className="acc-row-name">{s.name}{s.archived && <span className="acc-badge" style={{ marginLeft: 6 }}>arxivdə</span>}</span>
                <div className="acc-row-meta">
                  {s.locations.length === 0 ? <span>filial yoxdur</span>
                    : s.locations.slice(0, 6).map((l) => <span key={l.id} className="acc-badge area">{l.name}</span>)}
                  {s.locations.length > 6 && <span>+{s.locations.length - 6}</span>}
                </div>
              </div>
              <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
                {!s.archived && (
                  <>
                    <button type="button" className="btn btn-sm" onClick={() => setPicking(s)}>Filiallar</button>
                    <button type="button" className="btn btn-sm" onClick={() => void rename(s)}>Ad</button>
                    <button type="button" className="btn btn-sm btn-ghost" onClick={() => void archive(s)}>Arxivlə</button>
                  </>
                )}
                {s.archived && <button type="button" className="btn btn-sm" onClick={() => void restore(s)}>Bərpa et</button>}
              </div>
            </div>
          ))}
        </div>
      )}

      {picking && (
        <BranchPicker
          structure={picking}
          branches={allBranches}
          onClose={() => setPicking(null)}
          onSaved={(text, ok) => {
            setPicking(null)
            setFeedback({ ok, text })
            void load()
          }}
        />
      )}
    </div>
  )
}

function BranchPicker({
  structure, branches, onClose, onSaved,
}: {
  structure: StructureRow
  branches: (StructureBranch & { structure: string | null; structureId: string | null })[]
  onClose: () => void
  onSaved: (text: string, ok: boolean) => void
}) {
  const [picked, setPicked] = useState<Set<string>>(new Set(structure.locations.map((l) => l.id)))
  const [filter, setFilter] = useState('')
  const [busy, setBusy] = useState(false)
  const shown = branches.filter((b) => !filter.trim() || b.name.toLocaleLowerCase('az').includes(filter.trim().toLocaleLowerCase('az')))

  async function save() {
    const before = new Set(structure.locations.map((l) => l.id))
    const added = [...picked].filter((id) => !before.has(id))
    const removed = [...before].filter((id) => !picked.has(id))
    setBusy(true)
    const results = await Promise.all([
      added.length ? moveBranches(structure.id, added) : Promise.resolve({ status: 200, data: null }),
      removed.length ? moveBranches(null, removed) : Promise.resolve({ status: 200, data: null }),
    ])
    setBusy(false)
    const failed = results.find((r) => r.status !== 200)
    if (failed) onSaved(structureError(failed.data), false)
    else onSaved(`«${structure.name}»: ${added.length} filial əlavə olundu, ${removed.length} çıxarıldı.`, true)
  }

  return (
    <>
      <div className="acc-backdrop" onClick={onClose} />
      <aside className="acc-drawer" role="dialog" aria-label={`${structure.name} — filiallar`}>
        <div className="acc-drawer-head">
          <div>
            <h3 className="acc-drawer-title">{structure.name} — filiallar</h3>
            <p className="acc-drawer-sub">Başqa strukturdakı filialı seçsəniz, buraya köçür. Hüquqlar filialla birlikdə gedir.</p>
          </div>
          <button type="button" className="acc-close" onClick={onClose} aria-label="Bağla"><IconX /></button>
        </div>
        <div className="acc-drawer-body">
          <input className="inp" placeholder="Filial axtar…" value={filter} onChange={(e) => setFilter(e.target.value)} />
          <div className="acc-pick" style={{ maxHeight: 'none' }}>
            {shown.map((b) => (
              <label key={b.id} className="acc-check">
                <input
                  type="checkbox"
                  checked={picked.has(b.id)}
                  onChange={(e) => {
                    const next = new Set(picked)
                    if (e.target.checked) next.add(b.id)
                    else next.delete(b.id)
                    setPicked(next)
                  }}
                />
                {b.name}
                {b.structureId && b.structureId !== structure.id && <small>{b.structure}</small>}
              </label>
            ))}
          </div>
        </div>
        <div className="acc-drawer-foot">
          <span className="muted" style={{ marginRight: 'auto', fontSize: 12 }}>{picked.size} filial</span>
          <button type="button" className="btn btn-sm" onClick={onClose}>Ləğv et</button>
          <button type="button" className="btn btn-sm btn-primary" disabled={busy} onClick={() => void save()}>
            {busy ? 'Saxlanılır…' : 'Yadda saxla'}
          </button>
        </div>
      </aside>
    </>
  )
}
