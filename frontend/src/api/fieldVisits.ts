import { apiRequest } from './client'

export type FieldVisitStatus = 'Assigned' | 'CheckedIn' | 'Completed' | 'Cancelled'

/** The worker's view of one of their own visits. */
export interface MyFieldVisit {
  id: string
  status: FieldVisitStatus
  selfReported: boolean
  assignedByName: string | null
  targetLabel: string | null
  targetLatitude: number | null
  targetLongitude: number | null
  targetRadiusMeters: number | null
  checkInAtUtc: string | null
  checkInDistanceMeters: number | null
  checkOutAtUtc: string | null
  note: string | null
}

/** The manager's board row — everything above plus who/where and photo flags. */
export interface BoardFieldVisit {
  id: string
  employeeId: string
  employeeName: string
  phone: string | null
  assignedByName: string | null
  selfReported: boolean
  status: FieldVisitStatus
  targetLabel: string | null
  targetLatitude: number | null
  targetLongitude: number | null
  targetRadiusMeters: number | null
  checkInAtUtc: string | null
  checkInLatitude: number | null
  checkInLongitude: number | null
  checkInDistanceMeters: number | null
  checkOutAtUtc: string | null
  checkOutLatitude: number | null
  checkOutLongitude: number | null
  durationMinutes: number | null
  hasCheckInPhoto: boolean
  hasCheckOutPhoto: boolean
  note: string | null
}

export interface GeoPhotoBody {
  latitude: number
  longitude: number
  photoBase64?: string | null
}

// ---- worker ----
export function getMyFieldVisits() {
  return apiRequest<MyFieldVisit[]>('/api/field-visits/mine')
}
export function startFieldVisit(body: GeoPhotoBody & { targetLabel?: string | null }) {
  return apiRequest<MyFieldVisit>('/api/field-visits/start', { method: 'POST', body })
}
export function checkInFieldVisit(id: string, body: GeoPhotoBody) {
  return apiRequest<MyFieldVisit>(`/api/field-visits/${id}/check-in`, { method: 'POST', body })
}
export function checkOutFieldVisit(id: string, body: GeoPhotoBody) {
  return apiRequest<MyFieldVisit>(`/api/field-visits/${id}/check-out`, { method: 'POST', body })
}

// ---- manager / admin ----
export interface AssignBody {
  employeeId: string
  targetLabel?: string | null
  targetLatitude?: number | null
  targetLongitude?: number | null
  targetRadiusMeters?: number | null
  visitDate?: string | null
  note?: string | null
}
export interface AssignablePerson {
  id: string
  fullName: string
}
export function getAssignablePeople() {
  return apiRequest<AssignablePerson[]>('/api/field-visits/assignable')
}
export function assignFieldVisit(body: AssignBody) {
  return apiRequest<{ id: string }>('/api/field-visits', { method: 'POST', body })
}
export function getFieldVisitBoard(date?: string) {
  return apiRequest<BoardFieldVisit[]>(`/api/field-visits${date ? `?date=${date}` : ''}`)
}
export function cancelFieldVisit(id: string) {
  return apiRequest<{ id: string; status: string }>(`/api/field-visits/${id}/cancel`, { method: 'POST' })
}
/** Admin closes a visit the worker never checked out of (stuck CheckedIn). Checkout is stamped now. */
export function forceCheckOutFieldVisit(id: string) {
  return apiRequest<{ id: string; status: string }>(`/api/field-visits/${id}/force-checkout`, { method: 'POST' })
}
export function getFieldVisitPhotos(id: string) {
  return apiRequest<{ checkInUrl: string | null; checkOutUrl: string | null }>(`/api/field-visits/${id}/photos`)
}
