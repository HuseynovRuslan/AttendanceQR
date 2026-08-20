import { beforeEach, describe, expect, it, vi } from 'vitest'

/**
 * The drain rules, exercised against the REAL syncOfflineScans with the network and the queue
 * mocked. Each rule guards the same promise: a tap the employee saw turn green is either recorded
 * exactly once on the server, or its loss is loud — never silent, never doubled.
 */
const apiRequest = vi.fn()
vi.mock('../api/client', () => ({
  apiRequest: (...a: unknown[]) => apiRequest(...a),
  getToken: () => 'jwt',
}))
vi.mock('./jwt', () => ({ decodeJwt: () => ({ sub: 'emp-1' }) }))

const removeScan = vi.fn(async (_id: string) => {})
const queue: unknown[] = []
vi.mock('./offlineQueue', async (importOriginal) => ({
  ...(await importOriginal<typeof import('./offlineQueue')>()),
  allScans: async () => queue,
  scansFor: async () => queue,
  removeScan: (id: string) => removeScan(id),
}))

const reportFailure = vi.fn()
vi.mock('./scanFailures', () => ({
  reportFailure: (...a: unknown[]) => reportFailure(...a),
  flushFailures: async () => {},
}))
const addReject = vi.fn()
vi.mock('./offlineRejects', () => ({ addReject: (...a: unknown[]) => addReject(...a) }))

import { syncOfflineScans } from './offlineSync'

function item(id: string) {
  return {
    clientScanId: id,
    qrToken: 't',
    deviceFingerprint: 'fp',
    latitude: 40,
    longitude: 49,
    clientTimestampUtc: new Date().toISOString(),
    queuedAtMs: Date.now(),
    employeeId: 'emp-1',
  }
}

beforeEach(() => {
  vi.clearAllMocks()
  queue.length = 0
})

describe('draining the offline queue', () => {
  it('keeps everything and stops when the server is still down (502)', async () => {
    queue.push(item('a'), item('b'))
    apiRequest.mockResolvedValue({ status: 502, data: null })
    await syncOfflineScans()
    // One attempt, then stop — item b is not even tried, and NOTHING is removed or reported lost.
    expect(apiRequest).toHaveBeenCalledTimes(1)
    expect(removeScan).not.toHaveBeenCalled()
    expect(addReject).not.toHaveBeenCalled()
  })

  it('replays oldest-first and removes each item the server confirms', async () => {
    queue.push(item('first'), item('second'))
    apiRequest.mockResolvedValue({ status: 200, data: { action: 'CheckIn' } })
    await syncOfflineScans()
    expect(removeScan.mock.calls.map((c) => c[0])).toEqual(['first', 'second'])
  })

  it('treats "AlreadyRecorded" as success — the response-was-lost case must not double-record', async () => {
    // The server saw this clientScanId before (the original response never arrived). It answers 200
    // AlreadyRecorded; the item must leave the queue with no red banner anywhere.
    queue.push(item('lost-response'))
    apiRequest.mockResolvedValue({ status: 200, data: { action: 'AlreadyRecorded', alreadyProcessed: true } })
    await syncOfflineScans()
    expect(removeScan).toHaveBeenCalledWith('lost-response')
    expect(addReject).not.toHaveBeenCalled()
    expect(reportFailure).not.toHaveBeenCalled()
  })

  it('drops a definitive rejection LOUDLY — removed, but reported to the employee and the admin', async () => {
    queue.push(item('refused'))
    apiRequest.mockResolvedValue({ status: 409, data: { error: 'DeviceMismatch' } })
    await syncOfflineScans()
    expect(removeScan).toHaveBeenCalledWith('refused')
    expect(reportFailure).toHaveBeenCalled()
    expect(addReject).toHaveBeenCalledWith(expect.objectContaining({ code: 'DeviceMismatch' }))
  })

  it('drops an ordinary "already done today" answer without raising an alarm', async () => {
    queue.push(item('dup'))
    apiRequest.mockResolvedValue({ status: 409, data: { error: 'AlreadyCompleted' } })
    await syncOfflineScans()
    expect(removeScan).toHaveBeenCalledWith('dup')
    expect(addReject).not.toHaveBeenCalled()
  })

  it('keeps the queue when the network drops mid-drain', async () => {
    queue.push(item('a'), item('b'))
    apiRequest.mockRejectedValue(new TypeError('fetch failed'))
    await syncOfflineScans()
    expect(removeScan).not.toHaveBeenCalled()
  })
})
