import { apiRequest } from './client'

/**
 * GET /api/me/access — what the signed-in person may do in the permission model, and where. Read from the
 * database on every call, so a role given or taken away shows on the next one without signing anybody out.
 * The panel draws its menu from it; the server still decides every request on its own.
 */
export interface MyAccess {
  mode: 'Legacy' | 'Shadow' | 'Enforced'
  /** The role today's gates read — until the switch (Phase 5) it, not the model, opens the pages. */
  legacyRole: 'Admin' | 'Manager' | 'Employee'
  /** Today's two panel roles, or any role in the new model. */
  panel: boolean
  permissions: { key: string; label: string; company: boolean; locationIds: string[] }[]
  assignments: {
    roleId: string
    roleName: string | null
    template: string | null
    scope: 'Company' | 'Structure' | 'Branch'
    structureId: string | null
    structureName: string | null
    locationIds: string[]
  }[]
  /** Every branch this person's assignments reach — all of them for company-wide. */
  branches: { id: string; name: string; structureId: string | null }[]
}

export function getMyAccess() {
  return apiRequest<MyAccess>('/api/me/access')
}
