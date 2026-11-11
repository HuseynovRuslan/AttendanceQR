import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { buildWorld, NOW, PERSONA_IDS } from './data'
import { reduce, type Action, type Draft, type Persona, type World } from './model'

/**
 * The prototype's in-memory «server». Everything lives in this tab and is gone on reload — on purpose: the
 * prototype must never look as if it saved anything. Each call waits a moment, as a real request would, so the
 * screens show their loading state; «Xəta rejimi» makes every call fail, so their error state can be judged too.
 */

export type Toast = { id: number; tone: 'ok' | 'bad'; text: string }

interface Store {
  world: World
  persona: Persona
  now: Date
  /** True while a simulated request is in flight — the screens show skeletons or disable buttons. */
  loading: boolean
  failing: boolean
  slow: boolean
  /** How long a screen's first load takes (0 in tests). */
  loadMs: number
  toasts: Toast[]
  setPersona: (id: string) => void
  setFailing: (on: boolean) => void
  setSlow: (on: boolean) => void
  reset: () => void
  /** Reload the current screen's data — the error state's «Yenidən yüklə». */
  reload: () => Promise<void>
  create: (draft: Draft) => Promise<{ ok: boolean; error?: string; id?: string }>
  approve: (id: string) => Promise<{ ok: boolean; error?: string }>
  reject: (id: string, reason: string) => Promise<{ ok: boolean; error?: string }>
  cancel: (id: string) => Promise<{ ok: boolean; error?: string }>
  dismissToast: (id: number) => void
}

const Ctx = createContext<Store | null>(null)

export const PERSONA_SLUGS: Record<string, string> = {
  admin: PERSONA_IDS.admin,
  'op-aero': PERSONA_IDS.opAero,
  'op-park': PERSONA_IDS.opPark,
  'vw-aero': PERSONA_IDS.viewerAero,
  'vw-park': PERSONA_IDS.viewerPark,
  'vw-branch': PERSONA_IDS.viewerBranch,
}

export function slugFor(world: World, personaId: string): string {
  const named = Object.entries(PERSONA_SLUGS).find(([, id]) => id === personaId)?.[0]
  if (named) return named
  const p = world.personas.find((x) => x.id === personaId)
  return p?.kind === 'timekeeper' ? `tk-${p.branchId}` : 'admin'
}

export function personaFromSlug(world: World, slug: string | null): Persona {
  if (slug?.startsWith('tk-')) {
    const p = world.personas.find((x) => x.kind === 'timekeeper' && x.branchId === slug.slice(3))
    if (p) return p
  }
  const id = (slug && PERSONA_SLUGS[slug]) || PERSONA_IDS.opAero
  return world.personas.find((x) => x.id === id)!
}

const wait = (ms: number) => new Promise((r) => setTimeout(r, ms))

export function ProtoStoreProvider({ children, initialSlug, latencyMs = 380, loadMs = 320 }: {
  children: ReactNode
  initialSlug: string | null
  latencyMs?: number
  loadMs?: number
}) {
  const openedAt = useRef(Date.now())
  const [world, setWorld] = useState<World>(() => buildWorld())
  const [personaId, setPersonaId] = useState(() => personaFromSlug(world, initialSlug).id)
  const [loading, setLoading] = useState(false)
  const [failing, setFailing] = useState(false)
  const [slow, setSlow] = useState(false)
  const [toasts, setToasts] = useState<Toast[]>([])
  const seq = useRef(0)
  const worldRef = useRef(world)
  worldRef.current = world

  const persona = world.personas.find((p) => p.id === personaId) ?? world.personas[0]

  const toast = useCallback((tone: Toast['tone'], text: string) => {
    const id = ++seq.current
    setToasts((t) => [...t, { id, tone, text }])
    setTimeout(() => setToasts((t) => t.filter((x) => x.id !== id)), 5200)
  }, [])

  const round = useCallback(async () => {
    setLoading(true)
    await wait(slow ? latencyMs * 5 : latencyMs)
    setLoading(false)
    if (failing) throw new Error('fail')
  }, [failing, slow, latencyMs])

  const run = useCallback(async (action: (at: string) => Action, done: string) => {
    try {
      await round()
    } catch {
      toast('bad', 'Əlaqə alınmadı — demo xəta rejimi açıqdır. Heç nə dəyişmədi.')
      return { ok: false, error: 'network' }
    }
    // The prototype's clock: NOW plus however long this tab has been open, so new events sort after the seed.
    const at = new Date(NOW.getTime() + (Date.now() - openedAt.current)).toISOString()
    const out = reduce(worldRef.current, action(at))
    if (out.error) {
      toast('bad', out.error)
      return { ok: false, error: out.error }
    }
    worldRef.current = out.world
    setWorld(out.world)
    toast('ok', `${done}. Demo: serverə heç nə göndərilmədi.`)
    return { ok: true }
  }, [round, toast])

  const value = useMemo<Store>(() => ({
    world, persona, now: NOW, loading, failing, slow, loadMs, toasts,
    setPersona: setPersonaId,
    setFailing, setSlow,
    reset: () => {
      const fresh = buildWorld()
      worldRef.current = fresh
      setWorld(fresh)
      setFailing(false)
      setSlow(false)
      toast('ok', 'Demo məlumat ilkin vəziyyətə qaytarıldı')
    },
    reload: async () => {
      try { await round() } catch { /* the screen keeps showing its error */ }
    },
    create: async (draft) => {
      const id = `R-${2000 + ++seq.current}`
      const r = await run((at) => ({ type: 'create', draft, by: persona, id, at }),
        persona.kind === 'operator' ? 'Birbaşa tətbiq olundu' : 'Müraciət göndərildi')
      return { ...r, id: r.ok ? id : undefined }
    },
    approve: (id) => run((at) => ({ type: 'approve', id, by: persona, at }), 'Təsdiqləndi'),
    reject: (id, reason) => run((at) => ({ type: 'reject', id, by: persona, at, reason }), 'Rədd edildi'),
    cancel: (id) => run((at) => ({ type: 'cancel', id, by: persona, at }), 'Müraciət ləğv edildi'),
    dismissToast: (id) => setToasts((t) => t.filter((x) => x.id !== id)),
  }), [world, persona, loading, failing, slow, loadMs, toasts, round, run, toast])

  return <Ctx.Provider value={value}>{children}</Ctx.Provider>
}

export function useProto(): Store {
  const s = useContext(Ctx)
  if (!s) throw new Error('useProto outside ProtoStoreProvider')
  return s
}

/** A screen's first load: a moment of skeleton, then the data — or the error state while «Xəta rejimi» is on. */
export function useScreenLoad(key: string): { state: 'loading' | 'ready' | 'error'; retry: () => void } {
  const { failing, slow, loadMs } = useProto()
  const [state, setState] = useState<'loading' | 'ready' | 'error'>(loadMs === 0 && !failing ? 'ready' : 'loading')
  const [tick, setTick] = useState(0)
  useEffect(() => {
    if (loadMs === 0) {
      setState(failing ? 'error' : 'ready')
      return
    }
    setState('loading')
    const t = setTimeout(() => setState(failing ? 'error' : 'ready'), slow ? loadMs * 5 : loadMs)
    return () => clearTimeout(t)
  }, [key, failing, slow, loadMs, tick])
  return { state, retry: () => setTick((t) => t + 1) }
}
