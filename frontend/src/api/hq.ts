import { apiRequest } from './client'

export interface GroupCompany {
  id: string
  slug: string
  name: string
  employees: number
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
  feed: { fullName: string; company: string; location: string; atUtc: string; kind: 'in' | 'out' }[]
}

/** Every company at once. 403 for anyone outside the super-admin allowlist. */
export function getGroupOverview() {
  return apiRequest<GroupOverview>('/api/super/hq')
}
