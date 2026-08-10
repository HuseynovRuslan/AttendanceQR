import { apiRequest } from './client'

export type FieldVisitStatus = 'Assigned' | 'CheckedIn' | 'Completed' | 'Cancelled'

/** One line of the work checklist the manager set when assigning. */
export interface ChecklistItem {
  id: string
  label: string
  sortOrder: number
  isDone: boolean
  doneAtUtc: string | null
}

/** The worker's view of one of their own visits. */
export interface MyFieldVisit {
  /** Always an array (never null) — the app renders nothing when it is empty. */
  checklist: ChecklistItem[]
  checklistDone: number
  checklistTotal: number
  hasWorkPhoto: boolean
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
  /** SELFIE present — unchanged meaning, and deliberately NOT where work-photo state lives. */
  hasCheckInPhoto: boolean
  hasCheckOutPhoto: boolean
  /** İŞ ŞƏKLİ present — a photo of the work, never of a face. */
  hasWorkPhoto: boolean
  checklistTotal: number
  checklistDone: number
  note: string | null
}

export interface GeoPhotoBody {
  /** Null when GPS could not be resolved — a check-out is recorded anyway and flagged, never refused. */
  latitude: number | null
  longitude: number | null
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
/** `doneItemIds` is the ABSOLUTE set of ticked lines — it reconciles ticks whose own POST never
 *  reached the server, so a whole afternoon of failed ticks still lands with the departure. */
export function checkOutFieldVisit(id: string, body: GeoPhotoBody & { doneItemIds?: string[] }) {
  return apiRequest<MyFieldVisit>(`/api/field-visits/${id}/check-out`, { method: 'POST', body })
}
/** Ticks one line. Fire-and-forget from the app's point of view — failure is never shown as an error. */
export function setChecklistItem(id: string, itemId: string, isDone: boolean) {
  return apiRequest<{ id: string; isDone: boolean; doneAtUtc: string | null }>(
    `/api/field-visits/${id}/checklist/${itemId}`, { method: 'POST', body: { isDone } })
}
/** İŞ ŞƏKLİ. Always answers 200; `stored` tells the truth so the app can offer a retry. */
export function uploadWorkPhoto(id: string, photoBase64: string) {
  return apiRequest<{ stored: boolean; error?: string }>(
    `/api/field-visits/${id}/work-photo`, { method: 'POST', body: { photoBase64 } })
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
  /** The lines the worker will tick. The server trims/de-dupes/caps — it never rejects the assign. */
  checklist?: string[] | null
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
// getFieldVisitPhotos is GONE with its endpoint: it minted presigned SELFIE urls for the field board.
// The only field photo an admin can now open is the work photo below, which resolves WorkPhotoKey and
// nothing else — there is no code path left here that can display a field selfie.

/** The checklist with per-item tick times — what the manager opens the visit detail for. */
export function getFieldVisitChecklist(id: string) {
  return apiRequest<{ items: ChecklistItem[] }>(`/api/field-visits/${id}/checklist`)
}
/** İŞ ŞƏKLİ — a short-lived url for the photo of the WORK. Never a face. */
export function getFieldVisitWorkPhoto(id: string) {
  return apiRequest<{ url: string | null; takenAtUtc: string | null }>(`/api/field-visits/${id}/work-photo`)
}
