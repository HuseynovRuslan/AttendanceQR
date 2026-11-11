import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { getApprovalsMe, type ApprovalsMe } from '../../../api/approvals'
import { useAccess } from '../../../auth/AccessContext'
import { NO_APPROVALS, viewOf, type ApprovalsView } from './model'

interface ApprovalsState {
  view: ApprovalsView
  /** 'loading' until the first answer; 'error' when /api/approvals/me could not be read (the section then hides). */
  state: 'loading' | 'ready' | 'error'
  reload: () => void
}

const Ctx = createContext<ApprovalsState>({ view: NO_APPROVALS, state: 'loading', reload: () => {} })

/**
 * What the approval flow is for this person: read once when the panel opens, again when they come back to the tab,
 * and whenever a screen of the section asks (after a decision, so the queue's count is right). The menu and the
 * pages read the same answer. While the deployment's switch is off the server answers «enabled: false» and nothing
 * of the section is shown anywhere.
 */
export function ApprovalsProvider({ children }: { children: ReactNode }) {
  const { access } = useAccess()
  const [me, setMe] = useState<ApprovalsMe | null>(null)
  const [state, setState] = useState<ApprovalsState['state']>('loading')
  const seq = useRef(0)

  const reload = useCallback(() => {
    const mine = ++seq.current
    getApprovalsMe()
      .then((r) => {
        if (mine !== seq.current) return
        if (r.status === 200 && r.data && typeof r.data === 'object' && 'enabled' in r.data) {
          setMe(r.data)
          setState('ready')
        } else if (r.status === 404) {
          setMe({ enabled: false })
          setState('ready')
        } else {
          setState('error')
        }
      })
      .catch(() => {
        if (mine === seq.current) setState('error')
      })
  }, [])

  useEffect(() => {
    reload()
    const onVisible = () => {
      if (document.visibilityState === 'visible') reload()
    }
    document.addEventListener('visibilitychange', onVisible)
    return () => document.removeEventListener('visibilitychange', onVisible)
  }, [reload])

  const view = useMemo(() => viewOf(access, me), [access, me])
  const value = useMemo(() => ({ view, state, reload }), [view, state, reload])
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>
}

export function useApprovals(): ApprovalsState {
  return useContext(Ctx)
}
