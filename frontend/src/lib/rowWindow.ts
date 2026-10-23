import { useEffect, useState } from 'react'

/**
 * How much of a long list to put on the page now — the rest arrives as the reader scrolls toward it.
 *
 * «Bütün işçilər» at Bakı Abadlıq is 914 people under 80 branch headings. Switching to it built all
 * 994 rows in one go, 23 thousand elements, and the screen sat still for 0.58 s on a 12-core machine
 * (03.10.2026, measured on the live board: 215 ms React, 269 ms laying out the table, 107 ms painting).
 * About fifteen rows fit on a screen, so nearly all of that work was for rows nobody was looking at.
 *
 * This is not paging — the owner turned pages down when the board was redesigned, and there are none:
 * the list keeps scrolling, and rows are made a little ahead of the reader instead of all at once.
 * The counts, the search box and the Excel export read the whole list, not this window.
 */
export const FIRST_ROWS = 150
export const MORE_ROWS = 150

export interface RowGroup<T> {
  name: string
  /** The rows of this group to draw now — all of them, or the first few when the window ends here. */
  rows: T[]
  /** The whole group's size, for its heading: a heading must not shrink because the window does. */
  total: number
}

/**
 * The first `limit` rows of `groups`, in order. A group the window ends inside keeps its heading and
 * its full count; the groups after it are left out entirely.
 */
export function takeRows<T>(groups: ReadonlyArray<readonly [string, readonly T[]]>, limit: number): RowGroup<T>[] {
  const out: RowGroup<T>[] = []
  let shown = 0
  for (const [name, rows] of groups) {
    if (shown >= limit) break
    const take = rows.slice(0, limit - shown)
    out.push({ name, rows: take, total: rows.length })
    shown += take.length
  }
  return out
}

/**
 * The window over a list of `total` rows: how many to draw, and a ref for an element placed after the
 * last one. When that element comes within a screen of view, the window grows.
 *
 * It starts over only when `resetKey` changes, and the caller should change it only when the SET of
 * rows changes — a filter, the day. Not on the thirty-second refresh, which would throw somebody
 * reading row 400 back to the top; and not on a sort, which only reorders what is already drawn: cut
 * back to the first window, the page would shrink under a reader standing at row 500, leave them at
 * its bottom, and the list would grow again straight away.
 */
export function useRowWindow(resetKey: string, total: number) {
  const [limit, setLimit] = useState(FIRST_ROWS)
  const [key, setKey] = useState(resetKey)
  if (key !== resetKey) {
    setKey(resetKey)
    setLimit(FIRST_ROWS)
  }

  const hasMore = limit < total
  // The element itself, held in state rather than a ref object, so the observer follows it if the
  // table is ever drawn afresh: watching a node that has left the page would stop the list silently.
  const [sentinel, setSentinel] = useState<HTMLTableRowElement | null>(null)

  // Re-armed after every growth on purpose: observing an element reports where it is right now, so a
  // reader who is already at the bottom keeps getting rows until the bottom is out of reach again.
  // An observer left armed would only speak when the element moved in or out of view — and after a
  // growth it may simply stay in view, which would stop the list there.
  useEffect(() => {
    if (!sentinel || !hasMore || typeof IntersectionObserver === 'undefined') return
    const io = new IntersectionObserver(
      (entries) => { if (entries.some((e) => e.isIntersecting)) setLimit((n) => n + MORE_ROWS) },
      { rootMargin: '800px 0px' },
    )
    io.observe(sentinel)
    return () => io.disconnect()
  }, [sentinel, limit, hasMore])

  return { limit, hasMore, sentinel: setSentinel }
}
