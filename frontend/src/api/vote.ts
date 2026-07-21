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
