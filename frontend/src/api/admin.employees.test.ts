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

  it('a picker that genuinely needs every name walks the pages, and gets rows not a page envelope', async () => {
    const { data } = await getAllEmployees()

    expect(qs()).toContain('pageSize=200')
    expect(urls).toHaveLength(1) // an empty company is one request
    expect(data).toEqual([])
  })
})

/**
 * «Bütün işçilər» for the shift counts and the equipment picker. The server never returns more than 200
 * rows per request and answers a bigger ask with its default 25 — which is how «pageSize: 2000» showed the
 * first 25 people of every company for seven weeks. The roster is read page by page until it is whole.
 */
describe('getAllEmployees — the whole roster, not its first page', () => {
  const person = (i: number) => ({ id: `e${i}`, fullName: `İşçi ${i}` })
  let asked: URLSearchParams[]

  function serve(total: number, failOnPage?: number) {
    asked = []
    vi.stubGlobal('localStorage', { getItem: () => null, setItem: () => undefined, removeItem: () => undefined })
    vi.stubGlobal('fetch', (url: string) => {
      const p = new URLSearchParams(url.slice(url.indexOf('?') + 1))
      asked.push(p)
      const page = Number(p.get('page')), size = Number(p.get('pageSize'))
      if (page === failOnPage) return Promise.resolve({ status: 500, text: () => Promise.resolve('') })
      const from = (page - 1) * size
      const items = Array.from({ length: Math.max(0, Math.min(size, total - from)) }, (_, k) => person(from + k))
      return Promise.resolve({ status: 200, text: () => Promise.resolve(JSON.stringify({ items, total, page, pageSize: size })) })
    })
  }
  afterEach(() => vi.unstubAllGlobals())

  it('reads every page of a big company — 929 people, nobody missing, nobody twice', async () => {
    serve(929)
    const { status, data } = await getAllEmployees()

    expect(status).toBe(200)
    expect(data).toHaveLength(929)
    expect(new Set(data!.map((e) => e.id)).size).toBe(929)
    expect(asked.map((p) => p.get('page'))).toEqual(['1', '2', '3', '4', '5'])
    expect(asked.every((p) => p.get('pageSize') === '200')).toBe(true)
  })

  it('stops after one request when the company fits in a page', async () => {
    serve(49)
    const { data } = await getAllEmployees()

    expect(data).toHaveLength(49)
    expect(asked).toHaveLength(1)
  })

  it('an exactly full last page does not cost an extra empty request', async () => {
    serve(400)
    const { data } = await getAllEmployees()

    expect(data).toHaveLength(400)
    expect(asked).toHaveLength(2)
  })

  it('a failed page is a failure, not a quietly shorter roster', async () => {
    serve(929, 3)
    const { status, data } = await getAllEmployees()

    expect(status).toBe(500)
    expect(data).toBeNull()
  })
})
