import { describe, expect, it } from 'vitest'
import { applyBackspace, applyDigit, applyPaste } from './pinDigits'

/**
 * The PIN box is the last thing between an employee and their shift, so what is pinned here is what
 * would strand somebody: a pasted temporary PIN landing in a single box, backspace refusing to walk
 * back, or a letter from a stray keyboard ending up in the value.
 */
describe('applyDigit', () => {
  it('places a digit and moves on', () => {
    expect(applyDigit('', 4, 0, '7')).toEqual({ next: '7', focus: 1 })
  })

  it('keeps the caret where it is when a box is cleared', () => {
    expect(applyDigit('12', 4, 1, '')).toEqual({ next: '1', focus: 1 })
  })

  it('drops anything that is not a digit', () => {
    expect(applyDigit('', 4, 0, 'a').next).toBe('')
  })

  it('takes the last character when a keyboard sends two', () => {
    // Some Android keyboards deliver the existing value plus the new key.
    expect(applyDigit('1', 4, 0, '19')).toEqual({ next: '9', focus: 1 })
  })

  it('does not run past the last box', () => {
    expect(applyDigit('123', 4, 3, '4')).toEqual({ next: '1234', focus: 3 })
  })
})

describe('applyBackspace', () => {
  it('clears the previous box and steps back when this one is empty', () => {
    expect(applyBackspace('12', 4, 2)).toEqual({ next: '1', focus: 1, handled: true })
  })

  it('leaves a filled box to the browser', () => {
    expect(applyBackspace('12', 4, 1).handled).toBe(false)
  })

  it('does nothing at the first box', () => {
    expect(applyBackspace('', 4, 0).handled).toBe(false)
  })
})

describe('applyPaste', () => {
  it('spreads a pasted PIN across the row', () => {
    expect(applyPaste('5273', 4)).toEqual({ next: '5273', focus: 3 })
  })

  it('keeps only the digits', () => {
    expect(applyPaste('PIN: 12-34', 4)).toEqual({ next: '1234', focus: 3 })
  })

  it('ignores a paste with no digits in it', () => {
    expect(applyPaste('salam', 4)).toBeNull()
  })

  it('takes only as many digits as there are boxes', () => {
    expect(applyPaste('123456', 4)?.next).toBe('1234')
  })
})
