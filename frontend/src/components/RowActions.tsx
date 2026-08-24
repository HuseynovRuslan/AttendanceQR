import { useEffect, useRef, useState, type ReactNode } from 'react'

export type RowAction = {
  label: string
  onClick: () => void
  icon?: ReactNode
  /** Destructive — rendered apart, at the bottom, in red. */
  danger?: boolean
  disabled?: boolean
  title?: string
  /** Hide entirely (a row where the action does not apply). */
  hidden?: boolean
}

/**
 * One visible button and a "⋯" menu for the rest.
 *
 * Every admin table row used to carry its whole action set as buttons — six of them on the employees
 * table, six on locations — which wrapped onto two lines, pushed the columns that carry the actual
 * information out of view, and put a red "Sil" one mis-tap away from every other action. The row is
 * for reading; the actions are for the one row you came for.
 *
 * The primary action stays visible because it is the one people came to press. Everything else lives
 * behind the ⋯, in the order given, with destructive actions separated at the bottom — so deleting
 * takes two deliberate taps and can never be the one your thumb lands on.
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

  function run(a: RowAction) {
    setOpen(false)
    a.onClick()
  }

  const item = (a: RowAction, i: number) => (
    <button
      key={i}
      type="button"
      onClick={() => run(a)}
      disabled={a.disabled}
      title={a.title}
      style={{
        display: 'flex',
        alignItems: 'center',
        gap: 8,
        width: '100%',
        padding: '9px 12px',
        background: 'none',
        border: 'none',
        textAlign: 'left',
        fontSize: 13.5,
        fontWeight: 500,
        cursor: a.disabled ? 'default' : 'pointer',
        opacity: a.disabled ? 0.45 : 1,
        color: a.danger ? 'var(--danger, #C0392B)' : 'inherit',
        whiteSpace: 'nowrap',
      }}
      onMouseEnter={(e) => {
        if (!a.disabled) e.currentTarget.style.background = 'rgba(15,27,45,0.05)'
      }}
      onMouseLeave={(e) => {
        e.currentTarget.style.background = 'none'
      }}
    >
      {a.icon}
      {a.label}
    </button>
  )

  return (
    <div ref={wrap} style={{ position: 'relative', display: 'inline-flex', gap: 8, alignItems: 'center' }}>
      {primary && !primary.hidden && (
        <button className="btn btn-sm" onClick={primary.onClick} disabled={primary.disabled} title={primary.title}>
          {primary.icon} {primary.label}
        </button>
      )}
      {shown.length > 0 && (
        <button
          className="btn btn-sm"
          type="button"
          onClick={() => setOpen((o) => !o)}
          aria-haspopup="menu"
          aria-expanded={open}
          aria-label="Digər əməliyyatlar"
          style={{ padding: '6px 10px', fontSize: 16, lineHeight: 1 }}
        >
          ⋯
        </button>
      )}
      {open && (
        <div
          role="menu"
          style={{
            position: 'absolute',
            top: 'calc(100% + 6px)',
            right: 0,
            zIndex: 40,
            minWidth: 190,
            background: '#fff',
            border: '1px solid var(--line, rgba(15,27,45,0.10))',
            borderRadius: 12,
            boxShadow: '0 12px 32px -12px rgba(15,27,45,0.35)',
            padding: '6px 0',
          }}
        >
          {ordinary.map(item)}
          {destructive.length > 0 && ordinary.length > 0 && (
            <div style={{ height: 1, background: 'var(--line, rgba(15,27,45,0.10))', margin: '6px 0' }} />
          )}
          {destructive.map((a, i) => item(a, ordinary.length + i))}
        </div>
      )}
    </div>
  )
}
