import { useEffect, type ReactNode } from 'react'
import { NavLink, Navigate, Route, Routes, useLocation, useNavigate, useSearchParams } from 'react-router-dom'
import { IconBuilding, IconCheck, IconClipboard, IconEye, IconGrid, IconX } from '../../../components/icons'
import { ApprovalCenter } from './ApprovalCenter'
import { canDecide, visibleRequests, type Persona, type World } from './model'
import { OperatorDashboard } from './OperatorDashboard'
import { ProtoStoreProvider, slugFor, useProto } from './store'
import { StructureExplorer } from './StructureExplorer'
import { TimekeeperWorkspace } from './TimekeeperWorkspace'
import { personaRole } from './ui'
import { ViewerDashboard } from './ViewerDashboard'
import '../../approvals/approvals.css'

/**
 * Structure & approvals — a DESIGN PROTOTYPE on demo data. It is registered only in a development build (or one built
 * with VITE_PROTOTYPES=on, see App.tsx), never in production, and every screen says so: no request leaves this tab.
 * The real thing is planned in docs/tesdiq-sistemi-plan.md.
 */
export function ProtoApp({ latencyMs, loadMs }: { latencyMs?: number; loadMs?: number }) {
  const [params] = useSearchParams()
  return (
    <ProtoStoreProvider initialSlug={params.get('rol')} latencyMs={latencyMs} loadMs={loadMs}>
      <Shell />
    </ProtoStoreProvider>
  )
}

type Screen = { path: string; title: string; icon: ReactNode; lead: string }

const SCREENS: Record<string, Screen> = {
  operator: { path: 'operator', title: 'Operator paneli', icon: <IconGrid />, lead: '' },
  tabelci: { path: 'tabelci', title: 'Mənim filialım', icon: <IconClipboard />, lead: '' },
  musahideci: { path: 'musahideci', title: 'Müşahidəçi paneli', icon: <IconEye />, lead: '' },
  muracietler: {
    path: 'muracietler', title: 'Təsdiq mərkəzi', icon: <IconCheck />,
    lead: 'Tabelçilərin müraciətləri, qərarlar və hər birinin audit tarixçəsi.',
  },
  struktur: {
    path: 'struktur', title: 'Struktur xəritəsi', icon: <IconBuilding />,
    lead: 'Şirkət, strukturlar, filiallar və işçilər. Hər səviyyədə kimin məsul olduğu öz sütununda görünür.',
  },
}

function screensFor(p: Persona): Screen[] {
  switch (p.kind) {
    case 'companyAdmin': return [SCREENS.struktur, SCREENS.muracietler]
    case 'operator': return [SCREENS.operator, SCREENS.muracietler, SCREENS.struktur]
    case 'viewer': return [SCREENS.musahideci, SCREENS.struktur, SCREENS.muracietler]
    case 'timekeeper': return [SCREENS.tabelci, SCREENS.muracietler, SCREENS.struktur]
  }
}

/** The people the switcher offers: one of each role, on both approved structures. */
function switchable(w: World): { label: string; people: Persona[] }[] {
  const firstKeeper = (structureId: string) => {
    const withQueue = w.branches.find((b) => b.structureId === structureId && b.timekeeperId
      && w.requests.some((r) => r.branchId === b.id && r.status === 'pending'))
    return w.personas.find((p) => p.id === withQueue?.timekeeperId)
  }
  const ofStructure = (id: string) => [
    ...w.personas.filter((p) => p.structureId === id),
    ...w.personas.filter((p) => p.kind === 'viewer' && p.branchId && w.branches.find((b) => b.id === p.branchId)?.structureId === id),
    firstKeeper(id),
  ].filter((p): p is Persona => !!p)
  return [
    { label: 'Şirkət', people: w.personas.filter((p) => p.kind === 'companyAdmin') },
    ...w.structures.map((s) => ({ label: s.name, people: ofStructure(s.id) })),
  ]
}

function Shell() {
  const { world: w, persona, setPersona, failing, setFailing, slow, setSlow, reset, toasts, dismissToast } = useProto()
  const navigate = useNavigate()
  const location = useLocation()
  const [params, setParams] = useSearchParams()
  const screens = screensFor(persona)
  const current = screens.find((s) => location.pathname.endsWith(`/${s.path}`)) ?? screens[0]
  const { role, area } = personaRole(w, persona)
  const waiting = visibleRequests(w, persona).filter((r) => canDecide(w, persona, r)).length

  useEffect(() => { document.title = `${current.title} (prototip) | QRLog` }, [current.title])

  function switchTo(id: string) {
    setPersona(id)
    const next = w.personas.find((p) => p.id === id)!
    const allowed = screensFor(next)
    const keep = allowed.find((s) => s.path === current.path)
    const q = new URLSearchParams(params)
    q.set('rol', slugFor(w, id))
    q.delete('id')
    navigate({ pathname: keep ? location.pathname : `/prototip/tesdiq/${allowed[0].path}`, search: q.toString() }, { replace: true })
  }

  const search = (() => { const q = new URLSearchParams(); q.set('rol', params.get('rol') ?? slugFor(w, persona.id)); return `?${q}` })()

  return (
    <div className="apx-app">
      <aside className="apx-rail" aria-label="Panel">
        <div className="apx-brand">
          <div>
            <div className="apx-brand-mark">QR<b>Log</b></div>
            <div className="apx-brand-co">{w.companyName}</div>
          </div>
        </div>
        <div className="apx-who">
          <label htmlFor="apx-persona">Kimin gözü ilə baxırsınız</label>
          <select id="apx-persona" value={persona.id} onChange={(e) => switchTo(e.target.value)}>
            {switchable(w).map((g) => (
              <optgroup key={g.label} label={g.label}>
                {g.people.map((p) => (
                  <option key={p.id} value={p.id}>
                    {p.name} ({p.kind === 'viewer' && p.branchId ? 'filial müşahidəçisi' : personaRole(w, p).role.toLowerCase()})
                  </option>
                ))}
              </optgroup>
            ))}
          </select>
          <div className="apx-who-role">{role}<br />{area}</div>
        </div>
        <nav className="apx-nav" aria-label="Bölmələr">
          {screens.map((s) => (
            <NavLink key={s.path} to={{ pathname: `/prototip/tesdiq/${s.path}`, search }} aria-current={current.path === s.path ? 'page' : undefined}>
              {s.icon}{s.title}
              {s.path === 'muracietler' && waiting > 0 && <span className="apx-nav-n" aria-label={`${waiting} qərar gözləyir`}>{waiting}</span>}
            </NavLink>
          ))}
        </nav>
        <p className="apx-rail-foot">Bu, dizayn prototipidir. Real panel olduğu kimi qalır.</p>
      </aside>

      <div className="apx-main">
        <div className="apx-proto" role="note">
          <span><strong>Prototip.</strong> Demo məlumatdır: adlar uydurmadır, heç nə serverə göndərilmir, səhifəni yeniləyəndə hər şey sıfırlanır.</span>
          <span className="apx-proto-tools">
            <label><input type="checkbox" checked={slow} onChange={(e) => setSlow(e.target.checked)} /> Yavaş şəbəkə</label>
            <label><input type="checkbox" checked={failing} onChange={(e) => setFailing(e.target.checked)} /> Xəta rejimi</label>
            <button type="button" onClick={() => { reset(); setParams(new URLSearchParams({ rol: slugFor(w, persona.id) }), { replace: true }) }}>Sıfırla</button>
          </span>
        </div>
        <main className="apx-page" key={persona.id}>
          <header className="apx-head">
            <div>
              <h1>{current.title}</h1>
              {current.lead && <p>{current.lead}</p>}
            </div>
          </header>
          <Routes>
            <Route index element={<Navigate to={{ pathname: screens[0].path, search }} replace />} />
            {screens.map((s) => <Route key={s.path} path={s.path} element={<ScreenFor path={s.path} />} />)}
            <Route path="*" element={<Navigate to={{ pathname: screens[0].path, search }} replace />} />
          </Routes>
        </main>
      </div>

      <div className="apx-toasts" aria-live="polite" aria-atomic="false">
        {toasts.map((t) => (
          <div key={t.id} className={`apx-toast ${t.tone}`} role={t.tone === 'bad' ? 'alert' : 'status'}>
            <span>{t.text}</span>
            <button type="button" onClick={() => dismissToast(t.id)} aria-label="Bildirişi bağla"><IconX width={14} height={14} /></button>
          </div>
        ))}
      </div>
    </div>
  )
}

function ScreenFor({ path }: { path: string }) {
  switch (path) {
    case 'operator': return <OperatorDashboard />
    case 'tabelci': return <TimekeeperWorkspace />
    case 'musahideci': return <ViewerDashboard />
    case 'muracietler': return <ApprovalCenter />
    default: return <StructureExplorer />
  }
}
