import { describe, expect, it } from 'vitest'
import { isEarlyCheckOut } from './earlyCheckOut'

const IN = '2026-09-16T03:38:00.000Z'   // 07:38 Baku
const at = (min: number) => new Date(Date.parse(IN) + min * 60_000).toISOString()

describe('asking «are you leaving?» before a scan is saved offline', () => {
  it('asks for the tap six minutes after arriving — the one that closed Babayev Mustafa\'s day', () => {
    expect(isEarlyCheckOut(IN, at(6))).toBe(true)
  })

  it('asks up to two hours in, the same window the server uses', () => {
    expect(isEarlyCheckOut(IN, at(119))).toBe(true)
    expect(isEarlyCheckOut(IN, at(120))).toBe(false)
  })

  it('does not ask inside the double-tap window — the server refuses that tap whatever the answer', () => {
    expect(isEarlyCheckOut(IN, at(2))).toBe(false)
  })
})
