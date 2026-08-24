import { useCallback, useEffect, useLayoutEffect, useRef, useState, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { IconDots } from './icons'

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

const MENU_WIDTH = 212
const GAP = 6

/**
 * One visible button and a "⋯" menu for the rest.
 *
 * Every admin table row used to carry its whole action set as buttons — six on employees, six on
 * locations. They wrapped onto two lines, pushed the columns that carry the actual information out of
 * view, and put a red "Sil" one mis-tap away from every other action, on every row of a 64-person
 * table. The row is for reading; the actions are for the one row you came for.
 *
 * The menu is rendered into document.body rather than next to the button, because the tables scroll:
 * inside the row it was clipped by the scroll container and the last items were simply unreachable.
 * Being in the body means it has to be positioned by hand from the button's rect, and re-positioned
 * while anything scrolls — which is the price of not being trapped in a box.
 *
 * Styling lives in theme.css (.ra-*): `.btn svg` was the only rule in the app sizing these icons, so
 * outside a button they render at their intrinsic size — a trash can four rows tall.
 */
export function RowActions({ primary, actions }: { primary?: RowAction; actions: RowAction[] }) {
  const [open, setOpen] = useState(false)
  const [pos, setPos] = useState<{ top: number; left: number; flipped: boolean } | null>(null)
  const trigger = useRef<HTMLButtonElement | null>(null)
  const menu = useRef<HTMLDivElement | null>(null)

  const place = useCallback(() => {
    const button = trigger.current
    if (!button) return
    const r = button.getBoundingClientRect()
    const height = menu.current?.offsetHeight ?? 0
    // Below the button by default; above it when the viewport bottom is closer than the menu is tall.
    const flipped = height > 0 && r.bottom + GAP + height > window.innerHeight && r.top - GAP - height > 0
    setPos({
      top: flipped ? r.top - GAP - height : r.bottom + GAP,
      // Right-aligned with the button, and never off the left edge on a narrow screen.
      left: Math.max(8, r.right - MENU_WIDTH),
      flipped,
    })
  }, [])

  useLayoutEffect(() => {
    if (open) place()
  }, [open, place])

  useEffect(() => {
    if (!open) return
    function onDocPointer(e: MouseEvent) {
      const t = e.target as Node
      if (!trigger.current?.contains(t) && !menu.current?.contains(t)) setOpen(false)
    }
    function onEsc(e: KeyboardEvent) {
      if (e.key === 'Escape') setOpen(false)
    }
    // Capture: the table scrolls in its own container, not the window, and a bubbling listener on
    // document would never hear it.
    document.addEventListener('mousedown', onDocPointer)
    document.addEventListener('keydown', onEsc)
    window.addEventListener('scroll', place, true)
    window.addEventListener('resize', place)
    return () => {
      document.removeEventListener('mousedown', onDocPointer)
      document.removeEventListener('keydown', onEsc)
      window.removeEventListener('scroll', place, true)
      window.removeEventListener('resize', place)
    }
  }, [open, place])

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
    <div className="ra">
      {primary && !primary.hidden && (
        <button className="btn btn-sm" onClick={primary.onClick} disabled={primary.disabled} title={primary.title}>
          {primary.icon} {primary.label}
        </button>
      )}
      {shown.length > 0 && (
        <button
          ref={trigger}
          type="button"
          className="btn btn-sm ra-dots"
          onClick={() => setOpen((o) => !o)}
          aria-haspopup="menu"
          aria-expanded={open}
          aria-label="Digər əməliyyatlar"
        >
          <IconDots />
        </button>
      )}
      {open &&
        createPortal(
          <div
            ref={menu}
            className="ra-menu"
            role="menu"
            style={{
              top: pos?.top ?? -9999,
              left: pos?.left ?? -9999,
              // Until the first measurement lands the menu would flash at the wrong place, so it waits
              // one frame invisible rather than jumping.
              visibility: pos ? 'visible' : 'hidden',
            }}
          >
            {ordinary.map(item)}
            {destructive.length > 0 && ordinary.length > 0 && <div className="ra-sep" />}
            {destructive.map((a, i) => item(a, ordinary.length + i))}
          </div>,
          document.body,
        )}
    </div>
  )
}
