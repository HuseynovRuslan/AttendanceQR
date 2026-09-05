import { useMemo, useState, type ReactNode } from 'react'

/**
 * The pieces every operator screen was missing, written once.
 *
 * Each console page was a row of stat cards and a bare table: no search, no filter, no sort, an empty
 * state that was a grey sentence in a table cell, and a load that showed the word «Yüklənir…» over an
 * empty box. Those are not four page-level omissions — they are one missing layer, and building it
 * per page is how five pages end up with five slightly different search boxes.
 *
 * Nothing here fetches or owns data. Every component takes what it renders and hands interaction back
 * out, so a page keeps its own API calls, its own state and its own types exactly as they were.
 */

// ---------------------------------------------------------------- avatar ----

/**
 * A company's initials on a colour that is always the same colour for that company.
 *
 * The hue comes from the name, so «Green Garden» is one hue on every screen and across every reload —
 * a colour that shuffles is worse than no colour, because the reader learns it and is then wrong. Two
 * letters, because one is not distinctive and three do not fit the circle at this size.
 */
export function TenantAvatar({ name, size = 32 }: { name: string; size?: number }) {
  const { initials, hue } = useMemo(() => {
    const words = (name ?? '').trim().split(/\s+/).filter(Boolean)
    const letters = words.length >= 2
      ? (words[0][0] ?? '') + (words[1][0] ?? '')
      : (words[0] ?? '??').slice(0, 2)
    // Any stable spread over 360 will do; this one is cheap and has no clustering on our names.
    let h = 0
    for (let i = 0; i < name.length; i++) h = (h * 31 + name.charCodeAt(i)) % 360
    return { initials: letters.toLocaleUpperCase('az-AZ'), hue: h }
  }, [name])

  return (
    <span
      className="con-avatar"
      aria-hidden="true"
      style={{
        width: size,
        height: size,
        fontSize: size * 0.4,
        color: `hsl(${hue} 80% 78%)`,
        background: `hsl(${hue} 60% 50% / 0.18)`,
        borderColor: `hsl(${hue} 60% 60% / 0.32)`,
      }}
    >
      {initials}
    </span>
  )
}

// --------------------------------------------------------------- toolbar ----

export interface ToolbarFilter {
  key: string
  label: string
  /** Shown beside the label. Omit where a count would be noise rather than information. */
  count?: number
}

/**
 * The strip above a table: search on the left, quick filters in the middle, the page's own action on
 * the right.
 *
 * It sits INSIDE the table's card, above the head, rather than floating above it — a control that
 * governs a table belongs to that table, and a detached row of buttons reads as page furniture.
 */
export function ConsoleToolbar({
  query, onQuery, placeholder, filters, activeFilter, onFilter, children,
}: {
  query: string
  onQuery: (v: string) => void
  placeholder: string
  filters?: ToolbarFilter[]
  activeFilter?: string
  onFilter?: (key: string) => void
  /** The page's primary action, if it has one. */
  children?: ReactNode
}) {
  return (
    <div className="con-toolbar">
      <label className="con-search">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" aria-hidden="true">
          <circle cx="11" cy="11" r="7" /><path d="m20 20-3.5-3.5" strokeLinecap="round" />
        </svg>
        <input
          value={query}
          onChange={(e) => onQuery(e.target.value)}
          placeholder={placeholder}
          type="search"
          aria-label={placeholder}
        />
        {query && (
          <button type="button" onClick={() => onQuery('')} aria-label="Axtarışı təmizlə">✕</button>
        )}
      </label>

      {filters && filters.length > 0 && (
        <div className="con-filters" role="group">
          {filters.map((f) => (
            <button
              key={f.key}
              type="button"
              className={`con-filter${activeFilter === f.key ? ' is-on' : ''}`}
              onClick={() => onFilter?.(f.key)}
            >
              {f.label}
              {f.count != null && <b>{f.count}</b>}
            </button>
          ))}
        </div>
      )}

      {children && <div className="con-toolbar-end">{children}</div>}
    </div>
  )
}

// ----------------------------------------------------------- empty state ----

/**
 * What a screen says when it has nothing to show.
 *
 * It was «Aktiv şirkət yoxdur» — grey text in a table cell with 18px of padding, which reads as a
 * failure rather than as a state. An empty table is usually the reader's FIRST view of a screen, and
 * it is the one moment they will accept being told what the screen is for.
 */
export function EmptyState({ icon, title, note, action }: {
  icon: string
  title: string
  note?: string
  action?: ReactNode
}) {
  return (
    <div className="con-empty">
      <span className="con-empty-icon" aria-hidden="true">{icon}</span>
      <b>{title}</b>
      {note && <p>{note}</p>}
      {action}
    </div>
  )
}

// -------------------------------------------------------------- skeleton ----

/**
 * The shape of the table, before the table.
 *
 * «Yüklənir…» over an empty box tells the reader to wait without telling them what for, and the jump
 * from one line of text to twenty rows reads as a flicker. Bars in the columns the data will occupy
 * make the wait feel like the page arriving rather than the page being replaced.
 */
export function TableSkeleton({ rows = 6, cols = 4 }: { rows?: number; cols?: number }) {
  return (
    <>
      {Array.from({ length: rows }, (_, r) => (
        <tr key={r} className="con-skel-row" aria-hidden="true">
          {Array.from({ length: cols }, (_, c) => (
            <td key={c}>
              {/* Uneven widths on purpose: equal bars read as a loading GRAPHIC, uneven ones as text. */}
              <span className="con-skel" style={{ width: `${[68, 42, 55, 34, 60, 48, 38][(r + c) % 7]}%` }} />
            </td>
          ))}
        </tr>
      ))}
    </>
  )
}

// ---------------------------------------------------------------- sorting ----

export type SortDir = 'asc' | 'desc'

/**
 * Column sorting for a table the page already holds in memory.
 *
 * Returns the comparator state and a `<Th>` that renders the header AND its own control, so a page
 * adds a sortable column by naming the field rather than by wiring three handlers.
 */
export function useTableSort<T>(initial: keyof T & string, initialDir: SortDir = 'asc') {
  const [key, setKey] = useState<keyof T & string>(initial)
  const [dir, setDir] = useState<SortDir>(initialDir)

  function toggle(k: keyof T & string) {
    if (k === key) setDir((d) => (d === 'asc' ? 'desc' : 'asc'))
    else { setKey(k); setDir('asc') }
  }

  /** Sorts a copy — never the caller's array, which is usually state. */
  function sort(rows: T[]): T[] {
    return [...rows].sort((a, b) => {
      const x = a[key] as unknown
      const y = b[key] as unknown
      if (x == null && y == null) return 0
      if (x == null) return 1   // blanks last, whichever way the column is pointing
      if (y == null) return -1
      const n = typeof x === 'number' && typeof y === 'number'
        ? x - y
        // Azerbaijani collation: ə/ş/ç/ı sort where a reader expects, not by code point.
        : String(x).localeCompare(String(y), 'az-AZ')
      return dir === 'asc' ? n : -n
    })
  }

  function Th({ field, children, className }: { field: keyof T & string; children: ReactNode; className?: string }) {
    const on = key === field
    return (
      <th className={className}>
        <button type="button" className={`con-sort${on ? ' is-on' : ''}`} onClick={() => toggle(field)}>
          {children}
          <i aria-hidden="true">{on ? (dir === 'asc' ? '↑' : '↓') : '↕'}</i>
        </button>
      </th>
    )
  }

  return { key, dir, sort, Th }
}

// ----------------------------------------------------------------- match ----

/** Case- and locale-folded «does this row match what was typed». */
export function matches(query: string, ...fields: (string | null | undefined)[]): boolean {
  const q = query.trim().toLocaleLowerCase('az-AZ')
  if (!q) return true
  return fields.some((f) => (f ?? '').toLocaleLowerCase('az-AZ').includes(q))
}
