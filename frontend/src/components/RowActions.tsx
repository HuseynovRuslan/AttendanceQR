import { useEffect, useRef, useState, type ReactNode } from 'react'

export type RowAction = {
  label: string
  onClick: () => void
  icon?: ReactNode
  /** Destructive — rendered apart, at the bottom, in the clay accent. */
  danger?: boolean
  disabled?: boolean
  title?: string
  /** Hide entirely (a row where the action does not apply). */
  hidden?: boolean
}

/**
 * One visible button and a "⋯" menu for the rest.
 *
 * Every admin table row used to carry its whole action set as buttons — six on employees, six on
 * locations. They wrapped onto two lines, pushed the columns that carry the actual information out of
 * view, and put a red "Sil" one mis-tap away from every other action, on every row of a 64-person
 * table. The row is for reading; the actions are for the one row you came for.
 *
 * Styling lives in theme.css (.ra-*) rather than inline, and not only for tidiness: `.btn svg` was the
 * only rule sizing these icons, so the first version — inline styles, no class — rendered a trash can
 * the height of four rows.
 */
export function RowActions({ primary, actions }: { primary?: RowAction; actions: RowAction[] }) {
  const [open, setOpen] = useState(false)
  const wrap = useRef<HTMLDivElement | null>(null)

  useEffect(() => {
    if (!open) return
    function onDocClick(e: MouseEvent) {
      if (!wrap.current?.contains(e.target as Node)) setOpen(false)
    }
    function onEsc(e: KeyboardEvent) {
      if (e.key === 'Escape') setOpen(false)
    }
    document.addEventListener('mousedown', onDocClick)
    document.addEventListener('keydown', onEsc)
    return () => {
      document.removeEventListener('mousedown', onDocClick)
      document.removeEventListener('keydown', onEsc)
    }
  }, [open])

  const shown = actions.filter((a) => !a.hidden)
  const ordinary = shown.filter((a) => !a.danger)
  const destructive = shown.filter((a) => a.danger)

  const item = (a: RowAction, key: number) => (
    <button
      key={key}
      type="button"
      role="menuitem"
      className={`ra-item${a.danger ? ' danger' : ''}`}
      disabled={a.disabled}
      title={a.title}
      onClick={() => {
        setOpen(false)
        a.onClick()
      }}
    >
      {/* The icon column stays even when an action has no icon, so the labels line up. */}
      <span aria-hidden="true">{a.icon}</span>
      <span>{a.label}</span>
    </button>
  )

  return (
    <div className="ra" ref={wrap}>
      {primary && !primary.hidden && (
        <button className="btn btn-sm" onClick={primary.onClick} disabled={primary.disabled} title={primary.title}>
          {primary.icon} {primary.label}
        </button>
      )}
      {shown.length > 0 && (
        <button
          type="button"
          className="btn btn-sm ra-dots"
          onClick={() => setOpen((o) => !o)}
          aria-haspopup="menu"
          aria-expanded={open}
          aria-label="Digər əməliyyatlar"
        >
          •••
        </button>
      )}
      {open && (
        <div className="ra-menu" role="menu">
          {ordinary.map(item)}
          {destructive.length > 0 && ordinary.length > 0 && <div className="ra-sep" />}
          {destructive.map((a, i) => item(a, ordinary.length + i))}
        </div>
      )}
    </div>
  )
}
