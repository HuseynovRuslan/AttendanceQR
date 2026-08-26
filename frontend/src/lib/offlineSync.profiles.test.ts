import { beforeEach, describe, expect, it, vi } from 'vitest'

/**
 * The CREW PHONE rules: one handset, several saved accounts, and a queue holding scans made by all of
 * them. What these guard is the promise that made the whole idea worth building — a worker's tap is
 * recorded as THAT WORKER, and one broken account cannot take the other twenty-nine down with it.
 */
const apiRequest = vi.fn()
vi.mock('../api/client', () => ({
  apiRequest: (...a: unknown[]) => apiRequest(...a),
  getToken: () => 'jwt-active',
  getImpersonation: () => null,
}))
vi.mock('./jwt', () => ({ decodeJwt: () => ({ sub: 'emp-active' }) }))

// Two other workers whose profiles live on this phone.
vi.mock('./profiles', () => ({
  listProfiles: () => [
    { employeeId: 'emp-active', name: 'Holder', token: 'jwt-active', addedAtMs: 1 },
    { employeeId: 'emp-b', name: 'B', token: 'jwt-b', addedAtMs: 2 },
    { employeeId: 'emp-c', name: 'C', token: 'jwt-c', addedAtMs: 3 },
  ],
}))

function item(id: string, employeeId: string) {
  return {
    clientScanId: id,
    qrToken: 't',
    deviceFingerprint: 'fp',
    latitude: 40,
    longitude: 49,
    clientTimestampUtc: new Date().toISOString(),
    queuedAtMs: Date.now(),
    employeeId,
  }
}

let queue: ReturnType<typeof item>[] = []
const removeScan = vi.fn(async (id: string) => {
  queue = queue.filter((q) => q.clientScanId !== id)
})
vi.mock('./offlineQueue', async (importOriginal) => ({
  ...(await importOriginal<typeof import('./offlineQueue')>()),
  allScans: async () => queue,
  scansFor: async (employeeId: string | null) => queue.filter((q) => q.employeeId === employeeId),
  removeScan: (id: string) => removeScan(id),
}))

vi.mock('./scanFailures', () => ({ reportFailure: vi.fn(), flushFailures: async () => {} }))
vi.mock('./offlineRejects', () => ({ addReject: vi.fn() }))

import { syncOfflineScans } from './offlineSync'

/** The `token` option a call was made with — undefined means "the active session". */
function tokensUsed() {
  return apiRequest.mock.calls.map((c) => (c[1] as { token?: string }).token)
}

beforeEach(() => {
  apiRequest.mockReset()
  removeScan.mockClear()
  queue = [item('a1', 'emp-active'), item('b1', 'emp-b'), item('c1', 'emp-c')]
})

describe('draining a crew phone', () => {
  it('sends every account queued scans, each as that account', async () => {
    // Without this the phone would come back from a day with no signal, drain whoever happened to be
    // signed in, and leave the other twenty-nine workers marked absent.
    apiRequest.mockResolvedValue({ status: 200, data: {} })
    await syncOfflineScans()

    expect(apiRequest).toHaveBeenCalledTimes(3)
    // The active session goes through the ordinary path (no explicit token) so that ITS 401 still
    // ends the session of the person holding the phone; the saved profiles carry their own.
    expect(tokensUsed()).toEqual([undefined, 'jwt-b', 'jwt-c'])
    expect(queue).toHaveLength(0)
  })

  it('keeps going when one account is refused', async () => {
    // B's PIN was reset this morning. That is B's problem; C was standing at the poster too.
    apiRequest.mockImplementation((_p: string, opts: { token?: string }) =>
      Promise.resolve(opts.token === 'jwt-b' ? { status: 401, data: {} } : { status: 200, data: {} }),
    )
    await syncOfflineScans()

    expect(tokensUsed()).toContain('jwt-c')
    // B's scan is kept, not thrown away — the refusal is a state of the account, not a verdict on
    // the scan, and it clears the moment the profile is re-added with the new PIN.
    expect(queue.map((q) => q.clientScanId)).toEqual(['b1'])
  })

  it('stops the whole pass when the server is unwell', async () => {
    // A deploy window or an overloaded gateway answers this way for everyone; walking the remaining
    // accounts would only burn battery on a phone that has just found signal.
    apiRequest.mockResolvedValue({ status: 503, data: {} })
    await syncOfflineScans()

    expect(apiRequest).toHaveBeenCalledTimes(1)
    expect(queue).toHaveLength(3)
  })

  it('stops the whole pass when the network drops mid-drain', async () => {
    apiRequest.mockRejectedValue(new Error('offline'))
    await syncOfflineScans()

    expect(apiRequest).toHaveBeenCalledTimes(1)
    expect(queue).toHaveLength(3)
  })

  it('leaves a scan belonging to a removed profile alone while it is still fresh', async () => {
    // Somebody was taken off this phone but their tap is only an hour old — it is not this pass's
    // business, and it must not be sent as anyone else.
    queue = [item('x1', 'emp-gone')]
    apiRequest.mockResolvedValue({ status: 200, data: {} })
    await syncOfflineScans()

    expect(apiRequest).not.toHaveBeenCalled()
    expect(queue).toHaveLength(1)
  })
})
