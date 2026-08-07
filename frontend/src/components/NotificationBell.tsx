import { useEffect, useRef, useState, type CSSProperties } from 'react'
import { Link } from 'react-router-dom'
import { getNotifications, type NotificationsSummary } from '../api/notifications'
import { usePolling } from '../lib/usePolling'
import { IconBell } from './icons'

const SEC: CSSProperties = { padding: '8px 14px 3px', fontSize: 11, fontWeight: 700, letterSpacing: '.04em', textTransform: 'uppercase', color: 'var(--c400)' }
const ITEM: CSSProperties = { display: 'block', padding: '9px 14px', fontSize: 13, color: 'var(--c700)', textDecoration: 'none', borderBottom: '1px solid var(--c50)' }

/** Admin-only bell icon — live count + dropdown, no read/unread state (see api/notifications.ts). */
export function NotificationBell() {
  const [summary, setSummary] = useState<NotificationsSummary | null>(null)
  const [open, setOpen] = useState(false)
  const rootRef = useRef<HTMLDivElement>(null)

  usePolling(async () => {
    const { status, data } = await getNotifications()
    if (status === 200) setSummary(data)
  }, 30_000)

  useEffect(() => {
    function onClickOutside(e: MouseEvent) {
      if (rootRef.current && !rootRef.current.contains(e.target as Node)) setOpen(false)
    }
    document.addEventListener('mousedown', onClickOutside)
    return () => document.removeEventListener('mousedown', onClickOutside)
  }, [])

  const count = summary?.totalCount ?? 0

  return (
    <div ref={rootRef} style={{ position: 'relative' }}>
      <button
        className="btn btn-ghost btn-sm"
        onClick={() => setOpen((v) => !v)}
        style={{ position: 'relative', padding: 8 }}
        aria-label="Bildirişlər"
      >
        <IconBell />
        {count > 0 && (
          <span
            style={{
              position: 'absolute',
              top: 2,
              right: 2,
              minWidth: 16,
              height: 16,
              padding: '0 4px',
              borderRadius: 999,
              background: 'var(--clay)',
              color: '#fff',
              fontSize: 10,
              fontWeight: 800,
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              lineHeight: 1,
            }}
          >
            {count > 99 ? '99+' : count}
          </span>
        )}
      </button>

      {open && (
        <div
          className="card"
          style={{
            position: 'absolute',
            right: 0,
            top: '110%',
            width: 300,
            maxHeight: 360,
            overflowY: 'auto',
            zIndex: 40,
          }}
        >
          <div style={{ padding: '10px 14px', borderBottom: '1px solid var(--c100)', fontWeight: 700, fontSize: 13, color: 'var(--c900)' }}>
            Bildirişlər
          </div>

          {summary && summary.items.length > 0 && (
            <div>
              <div style={SEC}>Diqqət tələb edir</div>
              {summary.items.map((item, i) => (
                <Link key={i} to={item.linkTo} onClick={() => setOpen(false)} style={ITEM}>
                  {item.message}
                </Link>
              ))}
            </div>
          )}

          {summary && summary.activity.length > 0 && (
            <div>
              <div style={SEC}>Son hərəkət</div>
              {summary.activity.map((a, i) => (
                <Link key={i} to={a.linkTo} onClick={() => setOpen(false)} style={{ ...ITEM, display: 'flex', alignItems: 'center', gap: 8 }}>
                  <span style={{ flex: 'none', width: 7, height: 7, borderRadius: 999, background: a.isIn ? 'var(--leaf, #2e9e44)' : '#c0762a' }} />
                  <span style={{ flex: 1, minWidth: 0 }}>{a.message}</span>
                  <span style={{ flex: 'none', color: 'var(--c400)', fontVariantNumeric: 'tabular-nums' }}>{a.at}</span>
                </Link>
              ))}
            </div>
          )}

          {summary && summary.items.length === 0 && summary.activity.length === 0 && (
            <div className="muted" style={{ padding: 16, fontSize: 13, textAlign: 'center' }}>
              Hələ hərəkət yoxdur
            </div>
          )}
        </div>
      )}
    </div>
  )
}
