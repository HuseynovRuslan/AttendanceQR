import { describe, expect, it } from 'vitest'
import { isServerUnavailable, isTooOldToReplay, mayReplay, MAX_QUEUED_AGE_MS, type QueuedScan } from './offlineQueue'

/**
 * The two rules that decide what happens to a scan the employee already believes is saved. Both are
 * about the same failure: the person tapped, saw a green card, and the record must not quietly become
 * somebody else's — or somebody else's day.
 */
const ALI = 'aaaaaaaa-0000-0000-0000-000000000001'
const NIGAR = 'bbbbbbbb-0000-0000-0000-000000000002'

function scan(over: Partial<QueuedScan> = {}): QueuedScan {
  return {
    clientScanId: 'c1',
    qrToken: 't',
    deviceFingerprint: 'fp',
    latitude: 40.4093,
    longitude: 49.8671,
    clientTimestampUtc: '2026-08-10T05:00:00.000Z',
    queuedAtMs: 1_000_000,
    ...over,
  }
}

describe('who may replay a queued scan', () => {
  it('lets an employee replay their own', () => {
    expect(mayReplay(scan({ employeeId: ALI }), ALI)).toBe(true)
  })

  it('refuses to replay someone else’s scan under this session', () => {
    // The whole point: a shared site phone. Ali scans with no signal, hands the phone to Nigar, she
    // signs in — without this her session posts HIS scan as HER check-in, with his selfie filed under
    // her name and face-matched against her reference.
    expect(mayReplay(scan({ employeeId: ALI }), NIGAR)).toBe(false)
  })

  it('keeps someone else’s scan queued rather than discarding it', () => {
    // mayReplay only decides "not now". Ali's scan waits for Ali to sign back in; it is never dropped
    // on Nigar's behalf, because dropping it would cost Ali the day.
    const alis = scan({ employeeId: ALI })
    expect(mayReplay(alis, NIGAR)).toBe(false)
    expect(mayReplay(alis, ALI)).toBe(true)
  })

  it('still replays a legacy item that predates the owner stamp', () => {
    // One deploy's worth of items have no employeeId. The common case is the same person on their own
    // phone, and refusing them would lose a real attendance record to fix a rare one.
    expect(mayReplay(scan({ employeeId: undefined }), ALI)).toBe(true)
    expect(mayReplay(scan({ employeeId: undefined }), null)).toBe(true)
  })

  it('does not treat a signed-out session as matching an owned scan', () => {
    expect(mayReplay(scan({ employeeId: ALI }), null)).toBe(false)
  })
})

describe('when a queued scan is too old to replay', () => {
  const queuedAtMs = 1_000_000_000

  it('replays anything inside the trust window', () => {
    const item = scan({ queuedAtMs })
    expect(isTooOldToReplay(item, queuedAtMs)).toBe(false)
    expect(isTooOldToReplay(item, queuedAtMs + 60_000)).toBe(false)
    // Right up to the boundary.
    expect(isTooOldToReplay(item, queuedAtMs + MAX_QUEUED_AGE_MS)).toBe(false)
  })

  it('drops one past it', () => {
    // Past 18h the server stops trusting the phone's clock and stamps SERVER time — so a Thursday
    // scan syncing on Monday is not recorded late, it is recorded on MONDAY. If they already checked
    // in that morning it reads as their check-out and closes a live shift.
    const item = scan({ queuedAtMs })
    expect(isTooOldToReplay(item, queuedAtMs + MAX_QUEUED_AGE_MS + 1)).toBe(true)
    expect(isTooOldToReplay(item, queuedAtMs + 4 * 24 * 60 * 60 * 1000)).toBe(true)
  })

  it('matches the server’s 18-hour window', () => {
    // If AttendanceController's window ever moves, this is the tripwire — the two must agree or the
    // client either drops scans the server would have accepted, or sends ones it will misdate.
    expect(MAX_QUEUED_AGE_MS).toBe(18 * 60 * 60 * 1000)
  })

  it('is not confused by a clock that jumped backwards', () => {
    // A phone whose clock is behind produces a negative age; that must read as "fresh", not "expired".
    const item = scan({ queuedAtMs })
    expect(isTooOldToReplay(item, queuedAtMs - 60 * 60 * 1000)).toBe(false)
  })
})

describe('which HTTP answers mean "the server was not there to judge this scan"', () => {
  it('queues on the gateway statuses a deploy window produces', () => {
    // Caddy answers 502 while the backend container is being swapped; 503/504 are the same story
    // from an overloaded or wedged upstream. These taps used to be rejected outright — the one way
    // a real clock-in could still be lost.
    expect(isServerUnavailable(502)).toBe(true)
    expect(isServerUnavailable(503)).toBe(true)
    expect(isServerUnavailable(504)).toBe(true)
  })

  it('never queues a real answer', () => {
    // 4xx means the scan was judged and refused — replaying it would re-send what the server has
    // already said no to (wrong device, outside the fence, expired session).
    for (const s of [200, 400, 401, 403, 404, 409, 422, 429]) expect(isServerUnavailable(s)).toBe(false)
  })

  it('does not queue a 500 — a deterministic bug would retry for ever behind a green card', () => {
    expect(isServerUnavailable(500)).toBe(false)
  })
})
