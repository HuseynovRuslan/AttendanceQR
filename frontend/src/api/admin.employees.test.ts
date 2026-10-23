import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  getAllEmployees,
  getEmployee,
  getEmployeeSelection,
  getEmployeeStats,
  getEmployees,
} from './admin'

/**
 * What the Employees screen actually asks the server for.
 *
 * The page used to fetch the whole company and do the searching, filtering, counting and paging in
 * the browser — on production that read 227 million rows out of a 1,081-row table. All four of those
 * moved into SQL, which means the URL is now the behaviour: a missing parameter is a filter that
 * silently stopped applying, and an extra one is a count that reads its own number back.
 */
describe('the roster endpoints', () => {
  let urls: string[]

  beforeEach(() => {
    urls = []
    vi.stubGlobal('localStorage', {
      getItem: () => null, setItem: () => undefined, removeItem: () => undefined,
    })
    vi.stubGlobal('fetch', (url: string) => {
      urls.push(url)
      return Promise.resolve({ status: 200, text: () => Promise.resolve('{"items":[],"total":0}') })
    })
  })
  afterEach(() => vi.unstubAllGlobals())

  const qs = () => urls[0].slice(urls[0].indexOf('?') + 1)

  it('asks for ONE page, not the company', async () => {
    await getEmployees()

    expect(qs()).toContain('page=1')
    expect(qs()).toContain('pageSize=25')
  })

  it('sends every filter the screen offers', async () => {
    await getEmployees({
      page: 3, pageSize: 25, search: 'həsənov', status: 'notstarted',
      locationId: 'loc-1', role: 'Manager', showLeft: true,
    })

    const p = new URLSearchParams(qs())
    expect(p.get('page')).toBe('3')
    expect(p.get('search')).toBe('həsənov')
    expect(p.get('status')).toBe('notstarted')
    expect(p.get('locationId')).toBe('loc-1')
    expect(p.get('role')).toBe('Manager')
    expect(p.get('showLeft')).toBe('true')
  })

  it('leaves an untouched filter out instead of sending an empty one', async () => {
    await getEmployees({ search: '   ' })

    const p = new URLSearchParams(qs())
    expect(p.has('search')).toBe(false)
    expect(p.has('status')).toBe(false)
    expect(p.has('locationId')).toBe(false)
    expect(p.has('showLeft')).toBe(false)
  })

  it('reads ONE employee for the edit form rather than hunting through a list', async () => {
    // EmployeeUpdateRequest null-defaults every field it is not handed, so a form fed from a list row
    // would blank the email, the salary and the per-person overrides the list no longer carries.
    await getEmployee('abc-123')

    expect(urls[0]).toContain('/api/admin/employees/abc-123')
  })

  it('counts with the branch only — never with the status the tiles set', async () => {
    // The «Diqqət tələb edir» tiles ARE the status filter. A count that also applied it would answer
    // with its own number every time somebody pressed it.
    await getEmployeeStats('loc-7')

    expect(urls[0]).toContain('/stats?locationId=loc-7')
    expect(urls[0]).not.toContain('status')
    expect(urls[0]).not.toContain('search')
  })

  it('counts the whole company when no branch is chosen', async () => {
    await getEmployeeStats(null)

    expect(urls[0]).toMatch(/\/stats$/)
  })

  it('«Hamısını seç» asks for the whole matching set, not a page of it', async () => {
    // The invariant server-side paging could have broken quietly: a branch is routinely forty people,
    // and «grant this to the branch» must not come to mean its first twenty-five.
    await getEmployeeSelection({ page: 2, pageSize: 25, locationId: 'loc-1', status: 'nopush' })

    const p = new URLSearchParams(qs())
    expect(urls[0]).toContain('/selection')
    expect(p.has('page')).toBe(false)
    expect(p.has('pageSize')).toBe(false)
    // ...but it narrows by exactly what the list is narrowed by, or it would select the wrong people.
    expect(p.get('locationId')).toBe('loc-1')
    expect(p.get('status')).toBe('nopush')
  })

  it('a picker that genuinely needs every name says so, and gets rows not a page envelope', async () => {
    const { data } = await getAllEmployees()

    expect(qs()).toContain('pageSize=2000')
    expect(data).toEqual([])
  })
})
