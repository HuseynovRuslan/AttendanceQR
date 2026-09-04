import { apiRequest } from './client'

export interface GroupCompany {
  id: string
  slug: string
  name: string
  employees: number
  /** Imported, never once used the app — kept OUT of attendancePct's denominator. */
  notStarted: number
  present: number
  onDuty: number
  locations: number
  attendancePct: number
  payroll: number
}

/** One site on the map: where it is, and how many people are working there right now. */
export interface GroupSite {
  id: string
  name: string
  /** Index into the company list, so the marker takes that company's accent. */
  companyIndex: number
  lat: number
  lng: number
  /** Geofence radius — the area a check-in must happen inside. */
  radiusMeters: number
  onDuty: number
  present: number
  staff: number
}

export interface GroupOverview {
  generatedAtUtc: string
  totals: {
    companies: number
    employees: number
  notStarted: number
    present: number
    onDuty: number
    locations: number
    payroll: number
    attendancePct: number
    totalScans: number
    /** Days since the first check-in ever — "running N days without a break". */
    daysLive: number
  }
  companies: GroupCompany[]
  sites: GroupSite[]
  trend: { date: string; present: number }[]
  // 'field-in' / 'field-out' — a «səyyar» visit to a site with no poster, GPS + selfie instead of a QR.
  feed: {
    employeeId: string
    fullName: string
    companyId: string
    company: string
    location: string
    atUtc: string
    kind: 'in' | 'out' | 'field-in' | 'field-out'
    /** The attendance record behind a poster CHECK-IN — the key to its selfie. Null for a check-out
     *  (the selfie belongs to the arrival) and for field visits (their photo is on FieldVisit). */
    recordId: string | null
    hasPhoto: boolean
    /** 'Match' | 'Mismatch' | 'NoFace' | 'NotChecked' — the face audit's verdict for the check-in. */
    faceMatchStatus: string
    faceMatchScore: number | null
  }[]
}

/** Every company at once. 403 for anyone outside the super-admin allowlist. */
export function getGroupOverview() {
  return apiRequest<GroupOverview>('/api/super/hq')
}

/** One person who has never once opened the app — see GET /api/super/hq/not-started. */
export interface NotStartedRow {
  id: string
  fullName: string
  company: string
  companyId: string
  location: string
  position: string | null
  /** No number on file at all: nothing technical will fix this one. */
  hasPhone: boolean
  /** Still on the import's temporary PIN — handed an account and never logged in with it. */
  neverLoggedIn: boolean
  /** Opened the app at least once. Opened-but-never-scanned is the poster/geofence case. */
  openedApp: boolean
  /** Days since they last opened the app, or null if they never have. */
  daysSince: number | null
}

/** The people behind the board's «aktivləşdirməyib» figure. Fetched on demand, not with the board:
 *  it changes about once a week and the board refreshes every twenty seconds. */
export function getNotStarted() {
  return apiRequest<{ total: number; rows: NotStartedRow[] }>('/api/super/hq/not-started')
}

/** One person's last two days — what a live-feed row opens onto. */
export interface PersonDay {
  id: string
  fullName: string
  position: string | null
  phone: string | null
  company: string
  branch: string
  branchLat: number | null
  branchLng: number | null
  branchRadius: number | null
  today: string
  records: {
    date: string
    checkInAtUtc: string | null
    checkOutAtUtc: string | null
    lat: number | null
    lng: number | null
    wasOffline: boolean
    manual: boolean
  }[]
  visits: {
    id: string
    date: string
    /** What the worker typed. A claim, not a location — see PersonDrawer. */
    label: string | null
    checkInAtUtc: string | null
    checkOutAtUtc: string | null
    lat: number | null
    lng: number | null
    targetLat: number | null
    targetLng: number | null
    distanceMeters: number | null
    note: string | null
    hasPhoto: boolean
    selfReported: boolean
  }[]
}

export function getPersonDay(employeeId: string) {
  return apiRequest<PersonDay>(`/api/super/hq/person/${employeeId}`)
}

/** The two photographs behind one check-in, presigned and short-lived. */
export interface HqPhoto {
  title: string
  referenceUrl: string | null
  checkInUrl: string | null
  checkInTakenAtUtc: string | null
  faceMatchStatus?: string
  faceMatchScore?: number | null
}

export function getHqPhoto(recordId: string) {
  return apiRequest<HqPhoto>(`/api/super/hq/records/${recordId}/photo-url`)
}
