import { describe, expect, it } from 'vitest'
import { isRefused, listFailureText } from './listFailure'

describe('a list that never came is not an empty list', () => {
  it('says a refusal is a refusal', () => {
    expect(isRefused(403)).toBe(true)
    expect(listFailureText(403)).toContain('icazəniz yoxdur')
  })

  it('does not call an ended session or a failure a permission', () => {
    // 401 is the login screen's question; a 500 or a dropped connection is a retry, not a «no».
    for (const status of [401, 429, 500, 0]) {
      expect(isRefused(status)).toBe(false)
      expect(listFailureText(status)).toContain('yüklənmədi')
    }
  })
})
