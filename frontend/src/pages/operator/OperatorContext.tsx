import { createContext, useContext } from 'react'

/** The current operator's role + the mutating powers it holds (keys match the backend OperatorPermission
 *  enum). The operator shell fetches this once (GET /api/super/me) and provides it; pages read it to hide
 *  actions the operator can't perform. The SERVER still enforces every action — this is only UX. */
export interface OperatorInfo {
  role: string | null
  permissions: string[]
}

export const OperatorContext = createContext<OperatorInfo>({ role: null, permissions: [] })

// eslint-disable-next-line react-refresh/only-export-components
export function useOperator(): OperatorInfo {
  return useContext(OperatorContext)
}

/** Does the current operator hold this permission? (ManageTenants / Billing / ManageUsers / Impersonate /
 *  Announce / ManageTeam). */
// eslint-disable-next-line react-refresh/only-export-components
export function useCan(permission: string): boolean {
  return useContext(OperatorContext).permissions.includes(permission)
}
