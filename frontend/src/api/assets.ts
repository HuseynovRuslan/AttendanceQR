import { apiRequest } from './client'

/** Equipment categories. Names match the backend enum — the labels below are what an admin reads. */
export type AssetType =
  | 'Laptop'
  | 'Desktop'
  | 'Monitor'
  | 'Printer'
  | 'Phone'
  | 'Tablet'
  | 'Server'
  | 'Network'
  | 'Peripheral'
  | 'Other'

export type AssetStatus = 'InStock' | 'Assigned' | 'InRepair' | 'WrittenOff'

export const ASSET_TYPE_LABEL: Record<AssetType, string> = {
  Laptop: 'Noutbuk',
  Desktop: 'Stasionar kompüter',
  Monitor: 'Monitor',
  Printer: 'Printer / MFU',
  Phone: 'Telefon',
  Tablet: 'Planşet',
  Server: 'Server',
  Network: 'Şəbəkə avadanlığı',
  Peripheral: 'Periferiya',
  Other: 'Digər',
}

export const ASSET_STATUS_LABEL: Record<AssetStatus, string> = {
  InStock: 'Anbarda',
  Assigned: 'Təhkim olunub',
  InRepair: 'Təmirdə',
  WrittenOff: 'Silinib',
}

export const ASSET_TYPES = Object.keys(ASSET_TYPE_LABEL) as AssetType[]

export interface Asset {
  id: string
  /** The number on the sticker. Unique within the company. */
  inventoryNumber: string
  type: AssetType
  name: string
  brand: string | null
  model: string | null
  serialNumber: string | null
  /** "yyyy-MM-dd" or null. */
  purchaseDate: string | null
  purchasePrice: number | null
  status: AssetStatus
  locationId: string | null
  locationName: string | null
  /** Null unless status is "Assigned" — the two always move together. */
  assignedEmployeeId: string | null
  assignedEmployeeName: string | null
  assignedAtUtc: string | null
  notes: string | null
  createdAtUtc: string
}

export interface AssetSummary {
  total: number
  inStock: number
  assigned: number
  inRepair: number
  writtenOff: number
}

/** The card's editable fields. Assignment is not one of them — see assignAsset. */
export interface AssetInput {
  inventoryNumber: string
  type: AssetType
  name: string
  brand: string | null
  model: string | null
  serialNumber: string | null
  purchaseDate: string | null
  purchasePrice: number | null
  status: AssetStatus
  locationId: string | null
  notes: string | null
}

export interface AssetFilters {
  /** Matches inventory number, name, serial number, brand or model. */
  q?: string
  type?: AssetType | ''
  status?: AssetStatus | ''
  employeeId?: string
}

export function getAssets(filters: AssetFilters = {}) {
  const q = new URLSearchParams()
  if (filters.q) q.set('q', filters.q)
  if (filters.type) q.set('type', filters.type)
  if (filters.status) q.set('status', filters.status)
  if (filters.employeeId) q.set('employeeId', filters.employeeId)
  const suffix = q.toString() ? `?${q}` : ''
  return apiRequest<Asset[]>(`/api/admin/assets${suffix}`)
}

export function getAssetSummary() {
  return apiRequest<AssetSummary>('/api/admin/assets/summary')
}

export function createAsset(input: AssetInput) {
  return apiRequest<Asset | { error: string }>('/api/admin/assets', { method: 'POST', body: input })
}

export function updateAsset(id: string, input: AssetInput) {
  return apiRequest<Asset | { error: string }>(`/api/admin/assets/${id}`, { method: 'PUT', body: input })
}

/** Hands the equipment over. Works straight from one holder to another. */
export function assignAsset(id: string, employeeId: string, notes?: string | null) {
  return apiRequest<Asset | { error: string }>(`/api/admin/assets/${id}/assign`, {
    method: 'POST',
    body: { employeeId, notes: notes ?? null },
  })
}

/** Takes it back into stock. */
export function returnAsset(id: string) {
  return apiRequest<Asset | { error: string }>(`/api/admin/assets/${id}/return`, { method: 'POST' })
}

export function deleteAsset(id: string) {
  return apiRequest<{ deleted: string } | { error: string }>(`/api/admin/assets/${id}`, {
    method: 'DELETE',
  })
}
