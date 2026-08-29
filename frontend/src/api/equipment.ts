import { API_BASE_URL, apiRequest, getToken } from './client'

/**
 * The IT equipment register — one line per person, the same columns as the spreadsheet it is
 * imported from ("İT AVADANLIQLARININ SİYAHISI").
 */
export interface EquipmentRecord {
  id: string
  /** "Sıra №" — the line number in the register; what a re-import matches on. */
  rowNo: number
  fullName: string
  position: string | null
  /** "İşlədiyi ərazi" — free text: offices, sites, "Ümumi ərazilər". */
  area: string | null
  equipment: string | null
  systemUnit: string | null
  monitor: string | null
  otherEquipment: string | null
  /** Set when the name matched someone in the staff list; null is normal. */
  employeeId: string | null
  updatedAtUtc: string
}

/** rowNo is optional on create — null appends to the end of the list. */
export interface EquipmentInput {
  rowNo: number | null
  fullName: string
  position: string | null
  area: string | null
  equipment: string | null
  systemUnit: string | null
  monitor: string | null
  otherEquipment: string | null
  employeeId: string | null
}

export interface ImportResult {
  added: number
  updated: number
  linked: number
  /** Names the file carries that the staff list does not have — the register's usual staleness. */
  unmatched: string[]
}

export function getEquipment(q?: string) {
  const suffix = q?.trim() ? `?q=${encodeURIComponent(q.trim())}` : ''
  return apiRequest<EquipmentRecord[]>(`/api/admin/equipment${suffix}`)
}

/* There is no getEquipmentSummary here any more. The screen counts the register itself — the same
   parser that draws the chips on the cards — so a headline number and the card under it can never
   disagree. The server's GET /api/admin/equipment/summary is now called by nobody; it counts
   different things (linked/unlinked rows, "has a system unit") and should either be deleted or be
   given a caller, but a live endpoint whose numbers nothing shows is the kind of thing somebody
   later "fixes" for an afternoon. */

export function getEquipmentByEmployee(employeeId: string) {
  return apiRequest<EquipmentRecord[]>(`/api/admin/equipment/by-employee/${employeeId}`)
}

export function createEquipment(input: EquipmentInput) {
  return apiRequest<EquipmentRecord | { error: string }>('/api/admin/equipment', {
    method: 'POST',
    body: input,
  })
}

export function updateEquipment(id: string, input: EquipmentInput) {
  return apiRequest<EquipmentRecord | { error: string }>(`/api/admin/equipment/${id}`, {
    method: 'PUT',
    body: input,
  })
}

export function deleteEquipment(id: string) {
  return apiRequest<{ deleted: string } | { error: string }>(`/api/admin/equipment/${id}`, {
    method: 'DELETE',
  })
}

/** Uploads the register spreadsheet. Multipart, so it can't ride the JSON apiRequest helper. */
export async function importEquipment(file: File): Promise<{ status: number; data: ImportResult | { error: string } | null }> {
  const form = new FormData()
  form.append('file', file)
  const token = getToken()
  try {
    const res = await fetch(`${API_BASE_URL}/api/admin/equipment/import`, {
      method: 'POST',
      headers: token ? { Authorization: `Bearer ${token}` } : {},
      body: form,
    })
    const text = await res.text()
    return { status: res.status, data: text ? JSON.parse(text) : null }
  } catch {
    return { status: 0, data: null }
  }
}
