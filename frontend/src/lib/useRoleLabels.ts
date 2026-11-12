import { useEffect, useState } from 'react'
import { getRoleLabels } from '../api/me'
import { displayRoleName, type DisplayRole } from './roleNames'

export interface RoleLabelsView {
  /** The company has a Structure Manager: role pickers say Timekeeper. */
  hierarchy: boolean
  /** A person's role as shown: the server's label when it sent one, today's role otherwise. */
  nameOf: (employeeId: string, role: string | null | undefined) => string
}

/**
 * The names the company's people are shown under (GET /api/me/role-labels), for the staff list and the profile.
 * Plain staff are not in the answer and read as Employee. If the call fails the screen still works: everybody
 * reads under today's role — a missing label is a cosmetic gap, never a wrong right.
 */
export function useRoleLabels(): RoleLabelsView {
  const [state, setState] = useState<{ hierarchy: boolean; byId: Map<string, DisplayRole> }>({
    hierarchy: false,
    byId: new Map(),
  })
  useEffect(() => {
    let alive = true
    getRoleLabels()
      .then(({ status, data }) => {
        if (!alive || status !== 200 || !data || !Array.isArray(data.labels)) return
        setState({ hierarchy: !!data.hierarchy, byId: new Map(data.labels.map((l) => [l.id, l.role])) })
      })
      .catch(() => { /* names stay on today's roles */ })
    return () => { alive = false }
  }, [])
  return {
    hierarchy: state.hierarchy,
    nameOf: (employeeId, role) => displayRoleName(state.byId.get(employeeId), role),
  }
}
