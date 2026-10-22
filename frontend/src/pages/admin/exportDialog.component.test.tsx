// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { DayTemplate } from '../../api/admin'
import { unlistedAreas } from './exportRows'
import { ExportDialog } from './TodayPage'

/**
 * The export dialog's choice of form for «Xülasə».
 *
 * A company form (Bakı Abadlıq's «Davamiyyət siyahısı») is offered only where the company has one and
 * only for the branch view, starts unchosen, and — once chosen — will not let an area be ticked that
 * the form has no line for, because the server would refuse that file.
 */

afterEach(cleanup)

const form: DayTemplate = {
  key: 'davamiyyet-siyahisi',
  name: 'Davamiyyət siyahısı (şablon)',
  areas: ['Qala Anbar', 'Zoopark', 'İDEA'],
}

const areas = [
  { name: 'Qala Anbar', count: 10 },
  { name: 'Ramana Yaşıllıq', count: 50 },
  { name: 'İDEA', count: 1 },
]

function renderDialog(over: Partial<Parameters<typeof ExportDialog>[0]> = {}) {
  const props: Parameters<typeof ExportDialog>[0] = {
    date: '21 oktyabr 2026',
    areas,
    view: 'actual',
    onView: vi.fn(),
    hasPaper: true,
    templates: [form],
    template: null,
    onTemplate: vi.fn(),
    fits: () => true,
    selected: areas.map((a) => a.name),
    onToggle: vi.fn(),
    onAll: vi.fn(),
    onNone: vi.fn(),
    busy: false,
    onRun: vi.fn(),
    onClose: vi.fn(),
    ...over,
  }
  render(<ExportDialog {...props} />)
  return props
}

const checkboxOf = (name: string) =>
  screen.getByText(name).closest('label')!.querySelector('input') as HTMLInputElement

describe('ExportDialog — a company form for «Xülasə»', () => {
  it('shows no choice of form for a company that has none', () => {
    renderDialog({ templates: [] })
    expect(screen.queryByText('«Xülasə» vərəqi hansı formada olsun?')).toBeNull()
    expect(screen.queryByText('Standart')).toBeNull()
  })

  it('shows no choice of form in the «sənəd üzrə» view — the form is for branches', () => {
    renderDialog({ view: 'paper' })
    expect(screen.queryByText('«Xülasə» vərəqi hansı formada olsun?')).toBeNull()
  })

  it('offers the standard summary and the form, and says what the standard one does', () => {
    renderDialog()
    expect(screen.getByText('Standart')).toBeTruthy()
    expect(screen.getByText(form.name)).toBeTruthy()
    expect(screen.getByText(/Hər seçilmiş ərazi üçün bir sətir/)).toBeTruthy()
  })

  it('reports the choice either way', () => {
    const props = renderDialog()
    fireEvent.click(screen.getByText(form.name))
    expect(props.onTemplate).toHaveBeenCalledWith('davamiyyet-siyahisi')
    fireEvent.click(screen.getByText('Standart'))
    expect(props.onTemplate).toHaveBeenCalledWith(null)
  })

  it('with the form chosen, an area it has no line for cannot be ticked and says why', () => {
    renderDialog({
      template: form,
      fits: (name) => unlistedAreas([name], form.areas).length === 0,
      selected: ['Qala Anbar', 'İDEA'],
    })

    expect(checkboxOf('Ramana Yaşıllıq').disabled).toBe(true)
    expect(screen.getByText('şablonda yoxdur')).toBeTruthy()
    expect(checkboxOf('Qala Anbar').disabled).toBe(false)
    expect(screen.getByText('10 nəfər')).toBeTruthy()
    // The form's own size, and what happens to its empty lines, said before the file is made.
    expect(screen.getByText('3 sətri')).toBeTruthy()
    expect(screen.getByText(/hələ filial kimi açılmayan sətirlər/)).toBeTruthy()
  })

  it('in the standard summary every area can be ticked', () => {
    renderDialog()
    for (const a of areas) expect(checkboxOf(a.name).disabled).toBe(false)
    expect(screen.queryByText('şablonda yoxdur')).toBeNull()
  })
})
