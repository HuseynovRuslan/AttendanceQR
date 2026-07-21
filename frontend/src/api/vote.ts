import { apiRequest } from './client'

export interface VoteCandidate {
  employeeId: string
  fullName: string
  position: string | null
  /** Days present this month — shown so the choice rests on work, not popularity alone. */
  daysPresent: number
}

export interface VoteStatus {
  isOpen: boolean
  opensOn: string
  closesOn: string
  hasVoted: boolean
  canVote: boolean
  tooFewColleagues: boolean
  locationName: string | null
  period: string
  candidates: VoteCandidate[]
}

export interface VoteBranchResult {
  locationId: string
  locationName: string
  results: { employeeId: string; fullName: string; votes: number }[]
}

export interface VoteResults {
  period: string
  open: boolean
  votesCast: number
  branches: VoteBranchResult[]
  /** Settled winners for the period (empty while it is still being voted on). */
  winners?: { locationId: string; employeeId: string; votes: number }[]
}

export interface MyAward {
  period: string
  votes: number
}

/** The caller's own wins — powers the 🏆 badge on their home screen. */
export function getMyAwards() {
  return apiRequest<MyAward[]>('/api/vote/my-awards')
}

export function getVoteStatus() {
  return apiRequest<VoteStatus>('/api/vote/status')
}

export function castVote(candidateEmployeeId: string) {
  return apiRequest<{ ok: boolean } | { error: string }>('/api/vote', {
    method: 'POST',
    body: { candidateEmployeeId },
  })
}

/** Results. While voting is open the server returns turnout only — no running scoreboard. */
export function getVoteResults(period?: string) {
  return apiRequest<VoteResults>(`/api/vote/results${period ? `?period=${period}` : ''}`)
}

// --- admin settings ---------------------------------------------------------

export interface VoteSettings {
  enabled: boolean
  openDaysBeforeEnd: number
  manualFrom: string | null
  manualTo: string | null
  minCandidates: number
  minVotesToDecide: number
  /** What the settings mean right now — the real dates, not just the number of days. */
  currentWindowFrom: string
  currentWindowTo: string
  isOpenNow: boolean
}

export function getVoteSettings() {
  return apiRequest<VoteSettings>('/api/admin/vote-settings')
}

export function saveVoteSettings(input: {
  enabled: boolean
  openDaysBeforeEnd: number
  manualFrom: string | null
  manualTo: string | null
  minCandidates: number
  minVotesToDecide: number
}) {
  return apiRequest<VoteSettings | { error: string }>('/api/admin/vote-settings', {
    method: 'PUT',
    body: input,
  })
}
