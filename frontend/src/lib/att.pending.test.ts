import { describe, expect, it } from 'vitest'
import { withPendingScans, type TodayState } from './att'

/**
 * The home screen after a scan taken with no signal.
 *
 * Reported from the field: the card still read "Giriş et · Hələ giriş etməmisiniz" after an offline
 * check-in, because it was built only from what the SERVER knows. People concluded the scan had
 * failed and scanned again — and that second tap is a separate scan with its own id, so once the two
 * are far enough apart the second is not a duplicate, it is a CHECK-OUT. A day that opened at 08:50
 * and was scanned again at 09:05 closes at nine in the morning.
 *
 * So these tests are mostly about the two directions of being wrong. Claiming too little makes people
 * scan again, which is the bug. Claiming too much — saying a day is finished when the server may yet
 * decline the second tap — tells someone they can go home. The rule below only ever moves the day
 * forward one step.
 */

const TODAY = '2026-08-31'
const at = (t: string) => ({ clientTimestampUtc: `${TODAY}T${t}:00.000Z` })

describe('a queued scan counts on the employee\'s own screen', () => {
  it('turns "not checked in" into "at work"', () => {
    const out = withPendingScans({ kind: 'none' }, [at('08:50')], TODAY)

    expect(out).toEqual({ kind: 'in', checkIn: `${TODAY}T08:50:00.000Z`, pending: true })
  })

  it('marks it as still waiting to be sent', () => {
    // The screen has to be honest that the server has not confirmed it — the alternative is a card
    // that looks identical to a synced day and quietly disagrees with the admin's board.
    const out = withPendingScans({ kind: 'none' }, [at('08:50')], TODAY)

    expect(out.kind === 'in' && out.pending).toBe(true)
  })

  it('closes the day when the check-out is the queued one', () => {
    // The other half: the check-in synced, the way out did not. Left alone the card says "Çıxış et"
    // and invites a third scan.
    const server: TodayState = { kind: 'in', checkIn: `${TODAY}T08:50:00.000Z` }

    const out = withPendingScans(server, [at('18:05')], TODAY)

    expect(out).toEqual({
      kind: 'done', checkIn: `${TODAY}T08:50:00.000Z`, checkOut: `${TODAY}T18:05:00.000Z`, pending: true,
    })
  })
})

describe('a check-in AND a check-out both waiting on the phone', () => {
  // Staging, 2026-09-18: in at 14:25 and out at 14:35, both with no signal — and the card still said
  // «İşdəsiniz · Çıxış et». Whoever had just checked out was asked to do it again. The phone knows the
  // server's rules, so it can say what the server will do.

  it('a confirmed early exit closes the day on the screen', () => {
    const out = withPendingScans(
      { kind: 'none' },
      [at('14:25'), { ...at('14:35'), confirmEarlyCheckOut: true }],
      TODAY)

    expect(out).toEqual({
      kind: 'done', checkIn: `${TODAY}T14:25:00.000Z`, checkOut: `${TODAY}T14:35:00.000Z`, pending: true,
    })
  })

  it('an exit hours later needs no «bəli» to count', () => {
    const out = withPendingScans({ kind: 'none' }, [at('08:50'), at('18:05')], TODAY)

    expect(out.kind).toBe('done')
  })

  it("an unconfirmed early tap after the server's check-in is ignored, as the server ignores it", () => {
    const server: TodayState = { kind: 'in', checkIn: `${TODAY}T08:50:00.000Z` }

    expect(withPendingScans(server, [at('09:05')], TODAY)).toEqual(server)
  })
})

describe('what it refuses to conclude', () => {
  it('a nervous second tap nobody confirmed stays "at work"', () => {
    // Fifteen minutes after arriving, with no «bəli, çıxıram»: the server keeps the check-in and
    // ignores the tap (EarlyCheckOutRules), so the screen must not announce a check-out either.
    const out = withPendingScans({ kind: 'none' }, [at('08:50'), at('09:05')], TODAY)

    expect(out.kind).toBe('in')
  })

  it('a tap inside the double-tap window is no way out, even confirmed', () => {
    const out = withPendingScans({ kind: 'none' }, [at('08:50'), { ...at('08:52'), confirmEarlyCheckOut: true }], TODAY)

    expect(out.kind).toBe('in')
  })

  it('leaves a finished day alone', () => {
    const done: TodayState = {
      kind: 'done', checkIn: `${TODAY}T08:50:00.000Z`, checkOut: `${TODAY}T18:00:00.000Z`,
    }

    expect(withPendingScans(done, [at('18:30')], TODAY)).toEqual(done)
  })

  it('ignores a queued tap from before the server\'s check-in', () => {
    // The check-in reached the server some other way — a replay from another tab, a second phone —
    // and the queued copy is now just a leftover. It is not a way out.
    const server: TodayState = { kind: 'in', checkIn: `${TODAY}T08:50:00.000Z` }

    expect(withPendingScans(server, [at('08:49')], TODAY)).toEqual(server)
  })

  it('ignores yesterday\'s leftovers', () => {
    // The queue holds scans for up to 18 hours, so an overnight shift's tap is still in there in the
    // morning. It is not today's check-in.
    const stale = { clientTimestampUtc: '2026-08-30T22:10:00.000Z' }

    expect(withPendingScans({ kind: 'none' }, [stale], TODAY)).toEqual({ kind: 'none' })
  })

  it('changes nothing when the queue is empty', () => {
    expect(withPendingScans({ kind: 'none' }, [], TODAY)).toEqual({ kind: 'none' })
  })
})
