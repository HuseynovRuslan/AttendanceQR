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

describe('what it refuses to conclude', () => {
  it('two queued scans still only say "at work"', () => {
    // Whether the second becomes a check-out depends on a server rule about how long after the first
    // it was. Guessing "done" would tell somebody their day is closed when it may not be; guessing
    // "at work" costs at worst one scan the server declines.
    const out = withPendingScans({ kind: 'none' }, [at('08:50'), at('09:05')], TODAY)

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
