import { useEffect, useRef, type ReactNode } from 'react'

/** What the focus trap treats as reachable inside the panel. */
const FOCUSABLE = 'a[href], button:not([disabled]), input, [tabindex]:not([tabindex="-1"])'

interface HqDrawerProps {
  title: string
  /** One line under the title — what this panel is showing, in the reader's terms. */
  subtitle?: ReactNode
  /** The stripe across the top; a company's accent, or the board's own when it is not about one. */
  accent: string
  /** Sits between the header and the scrolling body: a button, a search box, nothing. */
  above?: ReactNode
  children: ReactNode
  onClose: () => void
}

/**
 * The slide-over shell shared by every panel on the group board.
 *
 * Extracted once there were three of them. The chrome is not the interesting part of any panel, but
 * it is the part that is easy to get subtly wrong in three different ways — Escape, the focus trap,
 * restoring focus on the way out, locking the page behind it. One copy, so a fix to any of those is
 * a fix everywhere rather than in whichever panel someone remembered.
 */
export function HqDrawer({ title, subtitle, accent, above, children, onClose }: HqDrawerProps) {
  const panelRef = useRef<HTMLElement | null>(null)
  const closeRef = useRef<HTMLButtonElement | null>(null)

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') { onClose(); return }
      // Keep Tab inside. The panel carries aria-modal, which tells a screen reader the rest of the
      // page is unavailable; letting focus walk out into it makes that attribute a lie.
      if (e.key !== 'Tab' || !panelRef.current) return
      const focusable = panelRef.current.querySelectorAll<HTMLElement>(FOCUSABLE)
      if (focusable.length === 0) return
      const first = focusable[0]
      const last = focusable[focusable.length - 1]
      if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus() }
      else if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus() }
    }
    window.addEventListener('keydown', onKey)

    // Move focus in, and put it back on the way out — otherwise a keyboard reader is returned to the
    // top of the document rather than to the thing they opened.
    const returnTo = document.activeElement as HTMLElement | null
    closeRef.current?.focus()

    // The board behind is a scrolling page; letting it scroll under a modal is how a reader loses
    // the place they came back to.
    const prevOverflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'

    return () => {
      window.removeEventListener('keydown', onKey)
      document.body.style.overflow = prevOverflow
      returnTo?.focus?.()
    }
  }, [onClose])

  return (
    <div className="hq-drawer-root">
      <div className="hq-drawer-backdrop" onClick={onClose} aria-hidden="true" />
      <aside ref={panelRef} className="hq-drawer" role="dialog" aria-modal="true" aria-label={title}>
        <div className="hq-drawer-bar" style={{ background: accent }} />

        <div className="hq-drawer-head">
          <div>
            <h2 className="hq-drawer-title">{title}</h2>
            {subtitle && <div className="hq-drawer-sub">{subtitle}</div>}
          </div>
          <button ref={closeRef} type="button" className="hq-drawer-close" onClick={onClose} aria-label="Bağla">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round">
              <path d="M18 6L6 18M6 6l12 12" />
            </svg>
          </button>
        </div>

        {above}

        <div className="hq-drawer-body">{children}</div>
      </aside>
    </div>
  )
}
