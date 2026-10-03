import { describe, expect, it } from 'vitest'
import { FIRST_ROWS, takeRows } from './rowWindow'

const groups: Array<[string, number[]]> = [
  ['Balaxanı ümumi anbar', [1, 2, 3, 4, 5]],
  ['Bazar qabağı', [6, 7, 8]],
  ['Komsomol-1', [9]],
]

describe('takeRows', () => {
  it('stops inside the first group when the window is smaller than it', () => {
    expect(takeRows(groups, 3)).toEqual([{ name: 'Balaxanı ümumi anbar', rows: [1, 2, 3], total: 5 }])
  })

  it('keeps the full count on the heading of a group the window ends inside', () => {
    // «Bazar qabağı 3» must still say 3 with one of its rows drawn — the heading describes the branch,
    // not how far the reader has scrolled.
    expect(takeRows(groups, 6)).toEqual([
      { name: 'Balaxanı ümumi anbar', rows: [1, 2, 3, 4, 5], total: 5 },
      { name: 'Bazar qabağı', rows: [6], total: 3 },
    ])
  })

  it('draws everything, in order, once the window is as long as the list', () => {
    expect(takeRows(groups, 50).map((g) => [g.name, g.rows])).toEqual(groups)
  })

  it('treats an ungrouped list as one nameless group', () => {
    expect(takeRows([['', [1, 2, 3, 4]]], 2)).toEqual([{ name: '', rows: [1, 2], total: 4 }])
  })

  it('draws nothing for an empty list', () => {
    expect(takeRows([], FIRST_ROWS)).toEqual([])
  })

  it('starts with far more rows than one screen holds', () => {
    // About fifteen rows fit on a screen; the first window must leave room to scroll before the next
    // one is needed, or the reader would watch it arrive.
    expect(FIRST_ROWS).toBeGreaterThanOrEqual(100)
  })
})
