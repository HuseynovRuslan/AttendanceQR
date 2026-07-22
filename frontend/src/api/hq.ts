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
  }
  companies: GroupCompany[]
  trend: { date: string; present: number }[]
  feed: { fullName: string; company: string; location: string; atUtc: string; kind: 'in' | 'out' }[]
}

/** Every company at once. 403 for anyone outside the super-admin allowlist. */
export function getGroupOverview() {
  return apiRequest<GroupOverview>('/api/super/hq')
}
