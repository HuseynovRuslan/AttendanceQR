import { useCallback, useEffect, useRef, useState, type CSSProperties } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { getNotifications, type NotificationsSummary } from '../api/notifications'
import {
  getCenter,
  getPreferences,
  getUnreadCount,
  markRead,
  type CenterNotification,
  type NotificationPreferences,
} from '../api/notificationCenter'
import { subscribeLive, type LiveItem } from '../lib/notificationHub'
import { groupByEvent, lookOf, metaLine, type EventGroup } from '../lib/notificationKinds'
import { activityBesideFeed, feedCoversOffice, toastOf } from '../lib/bellActivity'
import { emitNotificationsChanged, onNotificationsChanged } from '../lib/notificationEvents'
import { usePolling } from '../lib/usePolling'
import { IconBell, IconX } from './icons'
import { NotificationEntryIcon } from './NotificationKindIcon'

const SEC: CSSProperties = { padding: '10px 14px 4px', fontSize: 12, fontWeight: 700, color: 'var(--c600)' }
const UNREAD_BG = '#EEF4FF'
const UNREAD_BAR = '#2563EB'

const ITEM: CSSProperties = { display: 'block', padding: '10px 14px', fontSize: 13, color: 'var(--c700)', textDecoration: 'none', borderBottom: '1px solid var(--c50)' }

/**
 * The admin bell. Two things in one place, kept apart inside:
 *   • «Bildirişlər» — the person's own notification center (persisted, read/unread): the latest few entries (a check-in
 *     and its lateness are one) and a link to all;
 *   • «Diqqət tələb edir» / «Son hərəkət» — the live, computed list of what needs doing today, as before. «Son hərəkət»
 *     drops what the notifications above already say (lib/bellActivity) — field visits and anything the person's
 *     settings leave out stay.
 * The badge counts both. New notifications arrive over the live channel the moment they are written (with a short
 * pop-up), the count is also polled as a fallback, and a read mark anywhere updates it at once.
 */
export function NotificationBell({ actionItems = true }: { actionItems?: boolean }) {
  const navigate = useNavigate()
  const [summary, setSummary] = useState<NotificationsSummary | null>(null)
  const [unread, setUnread] = useState(0)
  const [unreadMore, setUnreadMore] = useState(false)
  const [latest, setLatest] = useState<CenterNotification[] | null>(null)
  const [open, setOpen] = useState(false)
  const [toast, setToast] = useState<{ lead: LiveItem; others: number } | null>(null)
  const [prefs, setPrefs] = useState<NotificationPreferences | null>(null)
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
    if (status === 200 && data) {
      setUnread(data.unread)
      setUnreadMore(!!data.more)
    }
  }, [])

  // The fallback for when the live channel cannot be had (a borrowed session, a network that refuses it).
  usePolling(refreshUnread, 60_000)
  useEffect(() => onNotificationsChanged(() => void refreshUnread()), [refreshUnread])

  useEffect(
    () =>
      subscribeLive({
        onItems: (items) => {
          const lines = items.filter((i) => i.id)
          // Entries, as the server counts them: a check-in and its lateness are one.
          if (lines.length > 0) setUnread((n) => n + groupByEvent(lines.map((l) => ({ ...l, id: l.id!, readAtUtc: null }))).length)
          setToast(toastOf(items))
          setLatest(null)
        },
        onReconnected: () => void refreshUnread(),
      }),
    [refreshUnread],
  )

  // Gone by itself after a few seconds — but not while it is being read with the pointer on it or focus in it.
  const [toastHeld, setToastHeld] = useState(false)
  useEffect(() => {
    if (!toast || toastHeld) return
    const t = setTimeout(() => setToast(null), 6000)
    return () => clearTimeout(t)
  }, [toast, toastHeld])

  useEffect(() => {
    if (!open || latest) return
    void getCenter({ limit: 8 }).then(({ status, data }) => {
      if (status === 200 && data) setLatest(data.items)
    })
  }, [open, latest])

  // What the person receives decides what «Son hərəkət» still has to say. Read once, when the bell is first opened.
  useEffect(() => {
    if (!open || !actionItems || prefs) return
    void getPreferences().then(({ status, data }) => {
      if (status === 200 && data) setPrefs(data)
    })
  }, [open, actionItems, prefs])

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

  function openEntry(g: EventGroup<CenterNotification>) {
    setOpen(false)
    const ids = g.members.filter((m) => !m.readAtUtc).map((m) => m.id)
    if (ids.length > 0) {
      const now = new Date().toISOString()
      setLatest((cur) => cur?.map((c) => (ids.includes(c.id) ? { ...c, readAtUtc: now } : c)) ?? null)
      setUnread((u) => Math.max(0, u - 1))
      void Promise.all(ids.map((id) => markRead(id))).then(() => emitNotificationsChanged())
    }
    if (g.lead.url) navigate(g.lead.url)
  }

  const entries = latest ? groupByEvent(latest).slice(0, 5) : null
  const activity = summary
    ? activityBesideFeed(summary.activity, entries?.flatMap((e) => e.members) ?? [], feedCoversOffice(prefs))
    : []

  const count = (summary?.totalCount ?? 0) + unread
  const many = count > 99 || unreadMore
  const label = count > 0 ? `Bildirişlər, ${many ? '99-dan çox' : count} yeni` : 'Bildirişlər'

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
            {many ? '99+' : count}
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

          {latest === null && unread > 0 && <div style={{ padding: 14, fontSize: 13, color: 'var(--c600)' }}>Yüklənir…</div>}
          {entries && entries.length > 0 && (
            <div>
              {entries.map((g) => {
                const n = g.lead
                const kinds = g.members.map((m) => m.typeKey)
                const meta = metaLine(n.branchName, n.occurredAtUtc, n.wasOffline)
                const kindLabel = [...new Set(kinds.map((k) => lookOf(k).label))].join(' və ')
                return (
                  <button
                    key={g.key}
                    type="button"
                    className="notif-row"
                    onClick={() => openEntry(g)}
                    aria-label={kindLabel + ': ' + n.body + ' ' + meta + (g.unread ? ', oxunmayıb' : '')}
                    style={{
                      display: 'flex', gap: 10, alignItems: 'center', width: '100%', padding: '10px 14px', border: 'none',
                      borderBottom: '1px solid var(--c100)', textAlign: 'left', cursor: 'pointer', font: 'inherit',
                      ['--row-bg' as string]: g.unread ? UNREAD_BG : '#fff',
                      boxShadow: g.unread ? 'inset 3px 0 0 ' + UNREAD_BAR : undefined,
                    }}
                  >
                    <NotificationEntryIcon typeKeys={kinds} size={30} ring={g.unread ? UNREAD_BG : '#fff'} />
                    <span style={{ flex: 1, minWidth: 0 }}>
                      <span
                        style={{
                          display: 'block', fontSize: 13, lineHeight: 1.4, fontWeight: g.unread ? 700 : 500,
                          color: g.unread ? 'var(--c900)' : 'var(--c600)',
                        }}
                      >
                        {n.body}
                      </span>
                      <span style={{ display: 'block', fontSize: 12, color: g.unread ? 'var(--c600)' : 'var(--c500)', marginTop: 2 }}>
                        {meta}
                      </span>
                    </span>
                    {g.unread && (
                      <span aria-hidden="true" style={{ flex: 'none', width: 8, height: 8, borderRadius: 999, background: UNREAD_BAR }} />
                    )}
                  </button>
                )
              })}
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

          {activity.length > 0 && (
            <div>
              <div style={SEC}>Son hərəkət</div>
              {activity.map((a, i) => (
                <Link key={i} to={a.linkTo} onClick={() => setOpen(false)} style={{ ...ITEM, display: 'flex', alignItems: 'center', gap: 8 }}>
                  <span style={{ flex: 'none', width: 7, height: 7, borderRadius: 999, background: a.isIn ? 'var(--leaf, #2e9e44)' : '#c0762a' }} />
                  <span style={{ flex: 1, minWidth: 0 }}>{a.message}</span>
                  <span style={{ flex: 'none', color: 'var(--c500)', fontVariantNumeric: 'tabular-nums' }}>{a.at}</span>
                </Link>
              ))}
            </div>
          )}

          {(!summary || (summary.items.length === 0 && activity.length === 0)) && latest?.length === 0 && (
            <div style={{ padding: 16, fontSize: 13, textAlign: 'center', color: 'var(--c600)' }}>
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
            onMouseEnter={() => setToastHeld(true)}
            onMouseLeave={() => setToastHeld(false)}
            onFocus={() => setToastHeld(true)}
            onBlur={() => setToastHeld(false)}
            style={{ display: 'flex', alignItems: 'flex-start', gap: 10, padding: '12px 12px 12px 14px', width: 340, maxWidth: '100%', boxShadow: 'var(--sh)' }}
          >
            <NotificationEntryIcon typeKeys={[toast.lead.typeKey]} size={32} />
            <button
              type="button"
              aria-label={toast.lead.title + ': ' + toast.lead.body + (toast.others > 0 ? '. Daha ' + toast.others + ' bildiriş' : '')}
              onClick={() => {
                setToast(null)
                setToastHeld(false)
                navigate(toast.others > 0 ? '/admin/notifications' : toast.lead.url ?? '/admin/notifications')
              }}
              style={{ flex: 1, minWidth: 0, border: 'none', background: 'transparent', padding: 0, textAlign: 'left', cursor: 'pointer', font: 'inherit' }}
            >
              <span style={{ display: 'block', fontWeight: 700, fontSize: 13.5, color: 'var(--c900)', lineHeight: 1.4 }}>{toast.lead.body}</span>
              <span style={{ display: 'block', fontSize: 12, color: 'var(--c600)', marginTop: 3 }}>
                {metaLine(toast.lead.branchName, toast.lead.occurredAtUtc, toast.lead.wasOffline)}
                {toast.others > 0 ? ' · daha ' + toast.others + ' bildiriş' : ''}
              </span>
            </button>
            <button
              type="button"
              className="btn btn-ghost btn-sm"
              onClick={() => {
                setToast(null)
                setToastHeld(false)
              }}
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
