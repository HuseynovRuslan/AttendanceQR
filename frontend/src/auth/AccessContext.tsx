import { createContext, useCallback, useContext, useEffect, useRef, useState, type ReactNode } from 'react'
import { getMyAccess, type MyAccess } from '../api/me'
import { useAuth } from './AuthContext'
import type { Role } from '../lib/jwt'

/**
 * The panel's copy of GET /api/me/access, read once when the panel opens and again whenever the tab comes
 * back into view — so a role somebody was just given shows up without signing them out (the owner's rule),
 * at the cost of one small request per return to the tab. Panel only: the employee app and the scan
 * screen never wait on it.
 */
interface AccessState {
  access: MyAccess | null
  /** True until the first answer (or failure) — the router waits on it only for somebody whose role is not
   *  a panel role today. */
  loading: boolean
  reload: () => void
}

const AccessCtx = createContext<AccessState>({ access: null, loading: false, reload: () => {} })

export function AccessProvider({ children }: { children: ReactNode }) {
  const { employeeId } = useAuth()
  const [access, setAccess] = useState<MyAccess | null>(null)
  const [loading, setLoading] = useState(true)
  const seq = useRef(0)

  const reload = useCallback(() => {
    const mine = ++seq.current
    void getMyAccess()
      .then((r) => {
        if (mine !== seq.current) return
        // A failure leaves the last good answer in place: a dropped request must not empty the menu.
        if (r.status === 200 && r.data && 'panel' in r.data) setAccess(r.data)
      })
      .catch(() => {})
      .finally(() => {
        if (mine === seq.current) setLoading(false)
      })
  }, [])

  useEffect(() => {
    setLoading(true)
    setAccess(null)
    reload()
  }, [employeeId, reload])

  useEffect(() => {
    const onVisible = () => {
      if (document.visibilityState === 'visible') reload()
    }
    document.addEventListener('visibilitychange', onVisible)
    return () => document.removeEventListener('visibilitychange', onVisible)
  }, [reload])

  return <AccessCtx.Provider value={{ access, loading, reload }}>{children}</AccessCtx.Provider>
}

export function useAccess(): AccessState {
  return useContext(AccessCtx)
}

/**
 * The role a page should take to pick its endpoints, for one permission (Phase 5).
 *
 * Every screen grew up asking «Admin or Manager?» to choose between the company-wide endpoints and the
 * manager's narrowed twins. In a company switched to Enforced that question is the permission model's, and
 * the answer is the server's own (AccessProjection): the Company administrator → the admin endpoints;
 * anybody else holding the right → the narrowed ones, which the server narrows to where they hold it (a
 * company-wide observer gets every branch, still without salaries, roles or numbers); not held → null.
 * Anywhere else it is the token's role, as before.
 */
export function useScopedRole(permission: string): Role | null {
  const { role } = useAuth()
  const { access } = useAccess()
  return scopedRole(role, access, permission)
}

/** The rule behind useScopedRole — the server's AccessProjection, read on the panel's side. */
export function scopedRole(role: Role | null, access: MyAccess | null, permission: string): Role | null {
  if (access?.mode !== 'Enforced') return role
  if (access.companyAdmin) return 'Admin'
  return access.permissions.some((p) => p.key === permission) ? 'Manager' : null
}

/** Whether a button's own right is held — the permission model's answer in Enforced, the given role rule elsewhere. */
export function useHolds(permission: string, legacy: boolean): boolean {
  const { access } = useAccess()
  if (access?.mode !== 'Enforced') return legacy
  return access.permissions.some((p) => p.key === permission)
}
