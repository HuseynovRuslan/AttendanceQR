import { useCallback, useEffect, useRef, useState, type CSSProperties } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { getNotifications, type NotificationsSummary } from '../api/notifications'
import { getCenter, getUnreadCount, markRead, type CenterNotification } from '../api/notificationCenter'
import { subscribeLive, type LiveItem } from '../lib/notificationHub'
import { metaLine } from '../lib/notificationKinds'
import { emitNotificationsChanged, onNotificationsChanged } from '../lib/notificationEvents'
import { usePolling } from '../lib/usePolling'
import { IconBell, IconX } from './icons'
import { NotificationKindIcon } from './NotificationKindIcon'

const SEC: CSSProperties = { padding: '10px 14px 4px', fontSize: 12, fontWeight: 700, color: 'var(--c500)' }
const ITEM: CSSProperties = { display: 'block', padding: '10px 14px', fontSize: 13, color: 'var(--c700)', textDecoration: 'none', borderBottom: '1px solid var(--c50)' }

/**
 * The admin bell. Two things in one place, kept apart inside:
 *   • «Bildirişlər» — the person's own notification center (persisted, read/unread): the latest few and a link to all;
 *   • «Diqqət tələb edir» / «Son hərəkət» — the live, computed list of what needs doing today, as before.
 * The badge counts both. New notifications arrive over the live channel the moment they are written (with a short
 * pop-up), the count is also polled as a fallback, and a read mark anywhere updates it at once.
 */
export function NotificationBell({ actionItems = true }: { actionItems?: boolean }) {
  const navigate = useNavigate()
  const [summary, setSummary] = useState<NotificationsSummary | null>(null)
  const [unread, setUnread] = useState(0)
  const [latest, setLatest] = useState<CenterNotification[] | null>(null)
  const [open, setOpen] = useState(false)
  const [toast, setToast] = useState<LiveItem | null>(null)
  const rootRef = useRef<HTMLDivElement>(null)
  const buttonRef = useRef<HTMLButtonElement>(null)

  usePolling(async () => {
    // The «needs attention» list is the admin's (and Enforced dashboard holders'); a manager's bell is their center only.
    if (!actionItems) return
    const { status, data } = await getNotifications()
    if (status === 200) setSummary(data)
  }, 30_000)

  const refreshUnread = useCallback(async () => {
    const { status, data } = await getUnreadCount()
    if (status === 200 && data) setUnread(data.unread)
  }, [])

  // The fallback for when the live channel cannot be had (a borrowed session, a network that refuses it).
  usePolling(refreshUnread, 60_000)
  useEffect(() => onNotificationsChanged(() => void refreshUnread()), [refreshUnread])

  useEffect(
    () =>
      subscribeLive({
        onItems: (items) => {
          const lines = items.filter((i) => i.id)
          if (lines.length > 0) setUnread((n) => n + lines.length)
          setToast(items[items.length - 1])
          setLatest(null)
        },
        onReconnected: () => void refreshUnread(),
      }),
    [refreshUnread],
  )

  useEffect(() => {
    if (!toast) return
    const t = setTimeout(() => setToast(null), 6000)
    return () => clearTimeout(t)
  }, [toast])

  useEffect(() => {
    if (!open || latest) return
    void getCenter({ limit: 5 }).then(({ status, data }) => {
      if (status === 200 && data) setLatest(data.items)
    })
  }, [open, latest])

  useEffect(() => {
    function onClickOutside(e: MouseEvent) {
      if (rootRef.current && !rootRef.current.contains(e.target as Node)) setOpen(false)
    }
    function onKey(e: KeyboardEvent) {
      if (e.key === 'Escape' && open) {
        setOpen(false)
        buttonRef.current?.focus()
      }
    }
    document.addEventListener('mousedown', onClickOutside)
    document.addEventListener('keydown', onKey)
    return () => {
      document.removeEventListener('mousedown', onClickOutside)
      document.removeEventListener('keydown', onKey)
    }
  }, [open])

  function openLine(n: CenterNotification) {
    setOpen(false)
    if (!n.readAtUtc) {
      setLatest((cur) => cur?.map((c) => (c.id === n.id ? { ...c, readAtUtc: new Date().toISOString() } : c)) ?? null)
      void markRead(n.id).then(() => emitNotificationsChanged())
    }
    if (n.url) navigate(n.url)
  }

  const count = (summary?.totalCount ?? 0) + unread
  const label = count > 0 ? `Bildirişlər, ${count} yeni` : 'Bildirişlər'

  return (
    <div ref={rootRef} style={{ position: 'relative' }}>
      <button
        ref={buttonRef}
        className="btn btn-ghost btn-sm"
        onClick={() => setOpen((v) => !v)}
        style={{ position: 'relative', padding: 10, minWidth: 44, minHeight: 44 }}
        aria-label={label}
        aria-expanded={open}
        aria-haspopup="dialog"
      >
        <IconBell />
        {count > 0 && (
          <span
            aria-hidden="true"
            style={{
              position: 'absolute',
              top: 4,
              right: 4,
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
          role="dialog"
          aria-label="Bildirişlər"
          style={{
            position: 'absolute',
            right: 0,
            top: '110%',
            width: 'min(340px, calc(100vw - 24px))',
            maxHeight: 460,
            overflowY: 'auto',
            zIndex: 40,
          }}
        >
          <div style={{ display: 'flex', alignItems: 'center', padding: '10px 14px', borderBottom: '1px solid var(--c100)' }}>
            <span style={{ flex: 1, fontWeight: 700, fontSize: 13, color: 'var(--c900)' }}>Bildirişlər</span>
            <Link to="/admin/notifications" onClick={() => setOpen(false)} style={{ fontSize: 12.5, fontWeight: 600 }}>
              Hamısına bax
            </Link>
          </div>

          {latest === null && unread > 0 && <div className="muted" style={{ padding: 14, fontSize: 13 }}>Yüklənir…</div>}
          {latest && latest.length > 0 && (
            <div>
              {latest.map((n) => (
                <button
                  key={n.id}
                  type="button"
                  onClick={() => openLine(n)}
                  style={{
                    display: 'flex', gap: 10, alignItems: 'flex-start', width: '100%', padding: '10px 14px', border: 'none',
                    borderBottom: '1px solid var(--c50)', background: n.readAtUtc ? 'transparent' : 'var(--c50)', textAlign: 'left',
                    cursor: 'pointer', font: 'inherit',
                  }}
                >
                  <NotificationKindIcon typeKey={n.typeKey} size={30} />
                  <span style={{ flex: 1, minWidth: 0 }}>
                    <span style={{ display: 'block', fontSize: 13, fontWeight: n.readAtUtc ? 600 : 800, color: 'var(--c900)' }}>{n.body}</span>
                    <span style={{ display: 'block', fontSize: 12, color: 'var(--c500)', marginTop: 2 }}>
                      {metaLine(n.branchName, n.occurredAtUtc, n.wasOffline)}
                    </span>
                  </span>
                </button>
              ))}
            </div>
          )}

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

          {(!summary || (summary.items.length === 0 && summary.activity.length === 0)) && latest?.length === 0 && (
            <div className="muted" style={{ padding: 16, fontSize: 13, textAlign: 'center' }}>
              Hələ bildiriş yoxdur
            </div>
          )}
        </div>
      )}

      {/* The live pop-up: announced politely, never takes focus, gone by itself in a few seconds. */}
      <div aria-live="polite" role="status" style={{ position: 'fixed', right: 16, bottom: 16, zIndex: 60, maxWidth: 'calc(100vw - 32px)' }}>
        {toast && (
          <div
            className="card"
            style={{ display: 'flex', alignItems: 'flex-start', gap: 10, padding: '12px 12px 12px 14px', width: 340, maxWidth: '100%', boxShadow: 'var(--sh)' }}
          >
            <NotificationKindIcon typeKey={toast.typeKey} size={32} />
            <button
              type="button"
              onClick={() => {
                setToast(null)
                if (toast.url) navigate(toast.url)
              }}
              style={{ flex: 1, minWidth: 0, border: 'none', background: 'transparent', padding: 0, textAlign: 'left', cursor: 'pointer', font: 'inherit' }}
            >
              <span style={{ display: 'block', fontWeight: 800, fontSize: 13, color: 'var(--c900)' }}>{toast.title}</span>
              <span style={{ display: 'block', fontSize: 13, color: 'var(--c700)', marginTop: 2 }}>{toast.body}</span>
              <span style={{ display: 'block', fontSize: 12, color: 'var(--c500)', marginTop: 3 }}>
                {metaLine(toast.branchName, toast.occurredAtUtc, toast.wasOffline)}
              </span>
            </button>
            <button
              type="button"
              className="btn btn-ghost btn-sm"
              onClick={() => setToast(null)}
              aria-label="Bağla"
              style={{ minWidth: 32, minHeight: 32, padding: 6 }}
            >
              <IconX />
            </button>
          </div>
        )}
      </div>
    </div>
  )
}
