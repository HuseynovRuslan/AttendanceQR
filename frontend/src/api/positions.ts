import { apiRequest } from './client'

export interface JobPosition {
  /** Null for a title employees hold that the catalogue never registered (e.g. from a bulk import). */
  id: string | null
  name: string
  count: number
  inCatalogue: boolean
}

export function getPositions() {
  return apiRequest<JobPosition[]>('/api/admin/positions')
}

export function createPosition(name: string) {
  return apiRequest<JobPosition | { error: string }>('/api/admin/positions', {
    method: 'POST',
    body: { name },
  })
}

/** Renames a title — or merges it into another if that name already exists. */
export function renamePosition(id: string, name: string) {
  return apiRequest<{ name: string; merged: boolean; movedEmployees: number } | { error: string }>(
    `/api/admin/positions/${id}`,
    { method: 'PUT', body: { name } },
  )
}

/** Registers a title people already hold but the catalogue was missing. */
export function adoptPosition(name: string) {
  return apiRequest<{ id: string; name: string } | { error: string }>('/api/admin/positions/adopt', {
    method: 'POST',
    body: { name },
  })
}

/** Moves everyone from one title onto another and drops the first — how duplicates get cleaned up. */
export function mergePositions(from: string, into: string) {
  return apiRequest<{ movedEmployees: number; into: string } | { error: string }>(
    '/api/admin/positions/merge',
    { method: 'POST', body: { from, into } },
  )
}

export function deletePosition(id: string) {
  return apiRequest<{ deleted: string } | { error: string; employees?: number }>(
    `/api/admin/positions/${id}`,
    { method: 'DELETE' },
  )
}
