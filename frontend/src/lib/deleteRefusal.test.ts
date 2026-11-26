import { describe, expect, it } from 'vitest'
import { employeeHistoryPrompt, employeeReferencedText, locationInUseText } from './deleteRefusal'

describe('deleting a person with history asks about what it actually is', () => {
  it('names each kind it found', () => {
    const text = employeeHistoryPrompt('Əli', { attendance: 12, requests: 1 })
    expect(text).toContain('12 davamiyyət qeydi')
    expect(text).toContain('1 telefon dəyişmə sorğusu')
    expect(text).toContain('TAM silinsin')
  })

  it('does not call a phone-change request «attendance» when there are no scans', () => {
    // An admin whose only past is a new-phone request they approved: the old prompt said
    // «davamiyyət tarixçəsi var» about them.
    const text = employeeHistoryPrompt('Əli', { attendance: 0, requests: 2 })
    expect(text).not.toContain('davamiyyət')
    expect(text).toContain('2 telefon dəyişmə sorğusu')
  })

  it('still asks when an older server sends no counts', () => {
    expect(employeeHistoryPrompt('Əli', {})).toBe(
      '"Əli" işçisinin tarixçəsi var. Hamısı daxil olmaqla TAM silinsin? Bu geri qaytarılmır.',
    )
  })

  it('says what holds the person when the delete has no rule for it, and that nothing was deleted', () => {
    const text = employeeReferencedText({ tables: ['SomeTable'] })
    expect(text).toContain('SomeTable')
    expect(text).toContain('heç nə silinmədi')
    expect(text).toContain('deaktiv')
    expect(employeeReferencedText({})).not.toContain('()')
  })
})

describe('a branch that will not delete says why', () => {
  it('sends a branch with scans to deactivation', () => {
    expect(locationInUseText('Mərkəz', { historyCount: 5, employeeCount: 2 })).toContain('5 davamiyyət qeydi')
  })

  it('names grants and approval records, and sends them to deactivation too, before staff', () => {
    // Moving staff out would not help — the records stay — so that is not the advice.
    const text = locationInUseText('Mərkəz', { employeeCount: 2, accessCount: 1, approvalCount: 3 })
    expect(text).toContain('1 səlahiyyət təyinatı')
    expect(text).toContain('3 təsdiq sahəsi və ya sorğusu')
    expect(text).toContain('Dayandır')
    expect(text).not.toContain('köçürün')
  })

  it('names a table it has no word for', () => {
    expect(locationInUseText('Mərkəz', { otherTables: ['NewThings'] })).toContain('NewThings')
  })

  it('tells the admin to move the staff when staff is all there is', () => {
    const text = locationInUseText('Mərkəz', { employeeCount: 4 })
    expect(text).toContain('4 işçi')
    expect(text).toContain('köçürün')
  })
})
