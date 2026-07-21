import { apiRequest } from './client'

export interface VoteCandidate {
  employeeId: string
  fullName: string
  position: string | null
}

export interface VoteStatus {
  /** False when no ballot was created for this month — the award simply isn't being run. */
  enabled: boolean
  isOpen: boolean
  opensOn: string | null
  closesOn: string | null
  opensAt: string | null
  closesAt: string | null
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

// --- admin: the monthly ballot -----------------------------------------------

/** A ballot an admin created for one month. No campaign for a month = no vote that month. */
export interface VoteCampaign {
  id: string
  period: string
  startsOn: string
  endsOn: string
  /** Local time of day, "HH:mm". */
  startsAt: string
  endsAt: string
  minCandidates: number
  minVotesToDecide: number
  /** Positions barred from being nominated. Empty = everyone is eligible. */
  excludedPositions: string[]
  votesCast: number
  isOpen: boolean
  /** scheduled = created but not started yet, open = running, finished = window has passed. */
  state: 'scheduled' | 'open' | 'finished'
}

export interface VoteCampaignResponse {
  period: string
  campaign: VoteCampaign | null
}

export interface VoteCampaignInput {
  startsOn: string
  endsOn: string
  startsAt: string
  endsAt: string
  minCandidates: number
  minVotesToDecide: number
  excludedPositions: string[]
}

export function getVoteCampaign(period: string) {
  return apiRequest<VoteCampaignResponse>(`/api/admin/vote-campaigns?period=${period}`)
}

export function createVoteCampaign(input: VoteCampaignInput) {
  return apiRequest<VoteCampaignResponse | { error: string }>('/api/admin/vote-campaigns', {
    method: 'POST',
    body: input,
  })
}

export function updateVoteCampaign(id: string, input: VoteCampaignInput) {
  return apiRequest<VoteCampaignResponse | { error: string }>(`/api/admin/vote-campaigns/${id}`, {
    method: 'PUT',
    body: input,
  })
}

/** Removes the ballot and everything cast in it — the month goes back to having no vote. */
export function deleteVoteCampaign(id: string) {
  return apiRequest<{ deleted: string } | { error: string }>(`/api/admin/vote-campaigns/${id}`, {
    method: 'DELETE',
  })
}

/** Keeps the ballot, clears the votes — restarting a round after a trial run. */
export function resetVoteCampaignVotes(id: string) {
  return apiRequest<{ removedVotes: number } | { error: string }>(
    `/api/admin/vote-campaigns/${id}/reset-votes`,
    { method: 'POST' },
  )
}
