import { describe, expect, it } from 'vitest'
import { isLimitError, limitErrorText } from './limitErrors'

describe('a value that does not fit its column is told with its limit', () => {
  it('names the field and the most it can hold', () => {
    expect(limitErrorText({ error: 'TextTooLong', field: 'description', max: 200 }, { description: 'Təsvir' }))
      .toBe('«Təsvir» çox uzundur — ən çox 200 hərf ola bilər.')
  })

  it('still gives the limit for a field the screen has no label for', () => {
    expect(limitErrorText({ error: 'TextTooLong', field: 'reason', max: 1000 }))
      .toBe('Mətn çox uzundur — ən çox 1000 hərf ola bilər.')
  })

  it('says what to do when the database refused without naming the column', () => {
    expect(limitErrorText({ error: 'TextTooLong' })).toContain('qısaldıb')
  })

  it('writes the money limit the way money is written here', () => {
    expect(limitErrorText({ error: 'NumberTooLarge', field: 'amount', max: 99999999.99 }, { amount: 'Məbləğ' }))
      .toBe('«Məbləğ» çox böyükdür — ən çox 99 999 999,99 ola bilər.') // no-break spaces, as formatNumber
  })

  it('leaves every other answer to the screen', () => {
    for (const data of [null, undefined, 'TextTooLong', {}, { error: 'DescriptionRequired' }, { status: 400 }]) {
      expect(isLimitError(data)).toBe(false)
      expect(limitErrorText(data)).toBeNull()
    }
  })
})
