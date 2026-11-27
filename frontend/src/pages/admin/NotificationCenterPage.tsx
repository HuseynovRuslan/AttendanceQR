import { useCallback, useEffect, useRef, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import {
  getCenter,
  getPreferences,
  getUnreadCount,
  markAllRead,
  markRead,
  type CenterNotification,
  type PreferenceBranch,
} from '../../api/notificationCenter'
import { NotificationEntryIcon } from '../../components/NotificationKindIcon'
import { IconCheck, IconRefresh, IconSliders } from '../../components/icons'
import { subscribeLive } from '../../lib/notificationHub'
import { groupByDay, groupByEvent, lookOf, metaLine, unreadEntries, type EventGroup } from '../../lib/notificationKinds'
import { emitNotificationsChanged } from '../../lib/notificationEvents'

/**
 * «Bildirişlər» — the person's notification history: newest first, grouped by company day, read and unread, filtered
 * by read state, type and branch, paged by the server (a cursor, so new arrivals never shift a page). New notifications
 * appear at the top as they are written (the live channel); after a reconnect the first page is fetched again.
 *
 * A check-in and its lateness — two lines, from two settings — are one entry when both are there: the lateness leads
 * («… işə 25 dəqiqə gecikdi» says the arrival too) and the check-in's mark sits on its corner. Opening it reads both.
 *
 * The server lists only what the reader may still see; a click opens the person's card, which checks its own scope
 * again — a notification is never a way around a permission.
 */
const TYPES: { key: string; label: string }[] = [
  { key: '', label: 'Hamısı' },
  { key: 'attendance.check_in', label: 'Giriş' },
  { key: 'attendance.check_out', label: 'Çıxış' },
  { key: 'attendance.late', label: 'Gecikmə' },
]

/** Unread: a blue-tinted row with a bar on its leading edge, bold text and a dot — never colour alone. */
const UNREAD_BG = '#EEF4FF'
const UNREAD_BAR = '#2563EB'

type Line = CenterNotification

function asLine(x: {
  id: string | null
  typeKey: string
  title: string
  body: string
  branchName: string | null
  occurredAtUtc: string
  wasOffline: boolean
  minutes: number | null
  url: string | null
  eventId?: string | null
}): Line {
  return {
    id: x.id!,
    typeKey: x.typeKey,
    title: x.title,
    body: x.body,
    subjectEmployeeId: null,
    subjectName: null,
    locationId: null,
    branchName: x.branchName,
    occurredAtUtc: x.occurredAtUtc,
    wasOffline: x.wasOffline,
    minutes: x.minutes,
    url: x.url,
    createdAtUtc: new Date().toISOString(),
    readAtUtc: null,
    eventId: x.eventId ?? null,
  }
}

export function NotificationCenterPage() {
  const navigate = useNavigate()
  const [type, setType] = useState('')
  const [branch, setBranch] = useState('')
  const [onlyUnread, setOnlyUnread] = useState(false)
  const [branches, setBranches] = useState<PreferenceBranch[]>([])
  const [items, setItems] = useState<Line[]>([])
  const [cursor, setCursor] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [loadingMore, setLoadingMore] = useState(false)
  const [error, setError] = useState(false)
  const [marking, setMarking] = useState(false)
  const [actionError, setActionError] = useState<string | null>(null)
  // Unread entries anywhere in the history, not just on the loaded page — what «Hamısını oxunmuş et» acts on.
  const [serverUnread, setServerUnread] = useState(0)
  const [serverUnreadMore, setServerUnreadMore] = useState(false)
  const [fresh, setFresh] = useState(0)
  const filters = useRef({ type, branch })
  filters.current = { type, branch }

  const refreshCount = useCallback(async () => {
    const count = await getUnreadCount()
    if (count.status === 200 && typeof count.data?.unread === 'number') {
      setServerUnread(count.data.unread)
      setServerUnreadMore(!!count.data.more)
    }
  }, [])

  const load = useCallback(async () => {
    setLoading(true)
    setError(false)
    const { status, data } = await getCenter({
      type: type || undefined, locationId: branch || undefined, unread: onlyUnread || undefined, limit: 30,
    })
    setLoading(false)
    if (status === 200 && data) {
      setItems(data.items)
      setCursor(data.nextCursor)
      setFresh(0)
    } else {
      setError(true)
    }
    await refreshCount()
  }, [type, branch, onlyUnread, refreshCount])

  useEffect(() => {
    void load()
  }, [load])

  useEffect(() => {
    void getPreferences().then(({ status, data }) => {
      if (status === 200 && data) setBranches(data.branches)
    })
  }, [])

  // Live: a new line for the current filter goes to the top (new lines are unread, so the unread filter keeps them).
  useEffect(
    () =>
      subscribeLive({
        onItems: (live) => {
          const { type: t, branch: b } = filters.current
          const lines = live.filter((x) => x.id)
          const add = lines.filter((x) => !t || x.typeKey === t).map(asLine)
          if (add.length > 0 && !b) {
            setItems((cur) => [...add.filter((a) => !cur.some((c) => c.id === a.id)), ...cur])
            setFresh((n) => n + groupByEvent(add).length)
          }
          if (lines.length > 0) setServerUnread((n) => n + groupByEvent(lines.map(asLine)).length)
          // A branch filter cannot be judged from the pop-up (it carries no branch id): reload instead.
          if (b && lines.length > 0) void load()
        },
        onReconnected: () => void load(),
      }),
    [load],
  )

  async function more() {
    if (!cursor) return
    setLoadingMore(true)
    setActionError(null)
    const { status, data } = await getCenter({
      type: type || undefined, locationId: branch || undefined, unread: onlyUnread || undefined, cursor, limit: 30,
    })
    setLoadingMore(false)
    if (status === 200 && data) {
      setItems((cur) => [...cur, ...data.items.filter((d) => !cur.some((c) => c.id === d.id))])
      setCursor(data.nextCursor)
    } else {
      setActionError('Daha köhnə bildirişlər yüklənmədi. Bir azdan yenidən cəhd edin.')
    }
  }

  function open(g: EventGroup<Line>) {
    const unreadIds = g.members.filter((m) => !m.readAtUtc).map((m) => m.id)
    if (unreadIds.length > 0) {
      const now = new Date().toISOString()
      // Stays in place (also under «Oxunmayıb»), only shown as read: a row never jumps away under the finger.
      setItems((cur) => cur.map((c) => (unreadIds.includes(c.id) ? { ...c, readAtUtc: now } : c)))
      setServerUnread((u) => Math.max(0, u - 1))
      void Promise.all(unreadIds.map((id) => markRead(id))).then(() => emitNotificationsChanged())
    }
    if (g.lead.url) navigate(g.lead.url)
  }

  async function readAll() {
    setMarking(true)
    setActionError(null)
    const { status } = await markAllRead(type || undefined)
    setMarking(false)
    if (status === 200) {
      const now = new Date().toISOString()
      if (onlyUnread) {
        setItems([])
        setCursor(null)
      } else {
        setItems((cur) => cur.map((c) => (c.readAtUtc ? c : { ...c, readAtUtc: now })))
      }
      await refreshCount()
      emitNotificationsChanged()
    } else {
      setActionError('Oxunmuş kimi qeyd olunmadı. İnternet bağlantısını yoxlayıb yenidən cəhd edin.')
    }
  }

  const entries = groupByEvent(items)
  const unread = Math.max(unreadEntries(items), serverUnread)
  const unreadLabel = serverUnreadMore || unread > 99 ? '99+' : String(unread)
  const days = groupByDay(entries, (g) => g.lead.createdAtUtc)
  const filtered = !!(type || branch || onlyUnread)

  return (
    <div style={{ maxWidth: 760 }}>
      <div style={{ display: 'flex', alignItems: 'flex-start', gap: 12, flexWrap: 'wrap', marginBottom: 16 }}>
        <div style={{ flex: '1 1 260px', minWidth: 0 }}>
          <h1 style={{ margin: 0, fontSize: 22, fontWeight: 800, color: 'var(--c900)', letterSpacing: '-0.01em' }}>Bildirişlər</h1>
          <p style={{ fontSize: 13.5, lineHeight: 1.5, marginTop: 4, marginBottom: 0, color: 'var(--c600)' }}>
            Səlahiyyətinizdə olan filialların işçiləri üzrə giriş, çıxış və gecikmə bildirişləri.
          </p>
        </div>
        <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
          <Link to="/menu/notifications" className="btn" style={{ minHeight: 44 }}>
            <IconSliders aria-hidden="true" />
            Bildiriş ayarları
          </Link>
          <button
            type="button"
            className="btn"
            onClick={() => void readAll()}
            disabled={marking || unread === 0}
            style={{ minHeight: 44 }}
          >
            <IconCheck aria-hidden="true" />
            {marking ? 'Qeyd olunur…' : 'Hamısını oxunmuş et'}
          </button>
        </div>
      </div>

      <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap', alignItems: 'center', marginBottom: 14 }}>
        <button
          type="button"
          aria-pressed={onlyUnread}
          onClick={() => setOnlyUnread((v) => !v)}
          className="notif-unread-toggle"
          style={{
            display: 'inline-flex', alignItems: 'center', gap: 8, minHeight: 44, padding: '0 14px', borderRadius: 12,
            border: `1px solid ${onlyUnread ? UNREAD_BAR : 'var(--c200)'}`,
            background: onlyUnread ? UNREAD_BAR : 'var(--white, #fff)',
            color: onlyUnread ? '#fff' : 'var(--c700)',
            fontFamily: 'inherit', fontWeight: 700, fontSize: 13, cursor: 'pointer', whiteSpace: 'nowrap',
          }}
        >
          <span
            aria-hidden="true"
            style={{ width: 8, height: 8, borderRadius: 999, background: onlyUnread ? '#fff' : UNREAD_BAR, flex: 'none' }}
          />
          Oxunmayıb
          {unread > 0 && (
            <span
              style={{
                minWidth: 22, height: 22, padding: '0 6px', borderRadius: 999, display: 'inline-flex', alignItems: 'center',
                justifyContent: 'center', fontSize: 12, fontVariantNumeric: 'tabular-nums',
                background: onlyUnread ? 'rgba(255,255,255,.22)' : '#DBE7FE', color: onlyUnread ? '#fff' : '#1E40AF',
              }}
            >
              {unreadLabel}
            </span>
          )}
        </button>
        {branches.length > 1 && (
          <label style={{ flex: '1 1 160px', maxWidth: 260 }}>
            <span className="sr-only">Filial</span>
            <select className="inp" value={branch} onChange={(e) => setBranch(e.target.value)} style={{ minHeight: 44 }}>
              <option value="">Bütün filiallar</option>
              {branches.map((b) => (
                <option key={b.id} value={b.id}>{b.name}</option>
              ))}
            </select>
          </label>
        )}
        <div className="seg" role="group" aria-label="Bildiriş növü" style={{ flex: '1 1 320px' }}>
          {TYPES.map((t) => (
            <button
              key={t.key || 'all'}
              type="button"
              className="seg-btn"
              aria-pressed={type === t.key}
              onClick={() => setType(t.key)}
              style={{ minHeight: 40, color: type === t.key ? undefined : 'var(--c600)' }}
            >
              {t.label}
            </button>
          ))}
        </div>
      </div>

      {actionError && (
        <div className="fb fb-err" role="alert" style={{ marginBottom: 12 }}>
          {actionError}
        </div>
      )}

      <div role="status" aria-live="polite" className="sr-only">
        {fresh > 0 ? `${fresh} yeni bildiriş` : ''}
      </div>

      {error && (
        <div className="fb fb-err" role="alert" style={{ marginBottom: 12, alignItems: 'center' }}>
          <span style={{ flex: 1 }}>Bildirişlər yüklənmədi. İnternet bağlantısını yoxlayın.</span>
          <button type="button" className="btn btn-sm" onClick={() => void load()}>
            <IconRefresh /> Yenidən yüklə
          </button>
        </div>
      )}

      {loading && items.length === 0 && !error && <CenterSkeleton />}

      {!loading && !error && items.length === 0 && (
        <div className="card card-pad" style={{ textAlign: 'center', padding: '36px 20px' }}>
          <div style={{ fontWeight: 700, color: 'var(--c900)' }}>
            {onlyUnread && !type && !branch
              ? 'Oxunmamış bildiriş yoxdur'
              : filtered
                ? 'Bu seçimə uyğun bildiriş yoxdur'
                : 'Hələ bildiriş yoxdur'}
          </div>
          <div style={{ fontSize: 13.5, marginTop: 6, lineHeight: 1.5, color: 'var(--c600)' }}>
            {onlyUnread && !type && !branch
              ? 'Hamısını oxumusunuz. Köhnə bildirişlər «Oxunmayıb» filtri söndürüləndə görünür.'
              : filtered
                ? 'Filtri dəyişin və ya söndürün.'
                : 'İşçi giriş, çıxış etdikdə və ya gecikdikdə bildirişlər burada görünəcək. Hansı bildirişləri alacağınızı ayarlarda seçin.'}
          </div>
          {onlyUnread ? (
            <button type="button" className="btn" onClick={() => setOnlyUnread(false)} style={{ marginTop: 14, minHeight: 44 }}>
              Hamısını göstər
            </button>
          ) : (
            !filtered && (
              <Link to="/menu/notifications" className="btn" style={{ marginTop: 14, display: 'inline-flex', minHeight: 44 }}>
                <IconSliders aria-hidden="true" />
                Bildiriş ayarları
              </Link>
            )
          )}
        </div>
      )}

      {days.map((d) => (
        <section key={d.day} aria-label={d.heading} style={{ marginBottom: 18 }}>
          <h2 style={{ fontSize: 13, fontWeight: 700, color: 'var(--c600)', margin: '0 0 8px 4px' }}>{d.heading}</h2>
          <div className="card" style={{ overflow: 'hidden' }}>
            {d.items.map((g, i) => (
              <EntryRow key={g.key} entry={g} first={i === 0} onOpen={() => open(g)} />
            ))}
          </div>
        </section>
      ))}

      {cursor && (
        <div style={{ textAlign: 'center', margin: '6px 0 24px' }}>
          <button type="button" className="btn" onClick={() => void more()} disabled={loadingMore} style={{ minHeight: 44 }}>
            {loadingMore ? 'Yüklənir…' : 'Daha çox göstər'}
          </button>
        </div>
      )}
    </div>
  )
}

function EntryRow({ entry, first, onOpen }: { entry: EventGroup<Line>; first: boolean; onOpen: () => void }) {
  const n = entry.lead
  const unread = entry.unread
  const kinds = entry.members.map((m) => m.typeKey)
  const kindLabel = [...new Set(kinds.map((k) => lookOf(k).label))].join(' və ')
  const meta = metaLine(n.branchName, n.occurredAtUtc, n.wasOffline)
  return (
    <button
      type="button"
      onClick={onOpen}
      className="notif-row"
      data-unread={unread ? 'true' : undefined}
      aria-label={`${kindLabel}: ${n.body} ${meta}${unread ? ', oxunmayıb' : ''}`}
      style={{
        display: 'flex',
        alignItems: 'center',
        gap: 12,
        width: '100%',
        padding: '12px 16px',
        minHeight: 64,
        border: 'none',
        borderTop: first ? 'none' : '1px solid var(--c100)',
        // theme.css .notif-row paints it, and darkens it a touch on hover / press.
        ['--row-bg' as string]: unread ? UNREAD_BG : '#fff',
        boxShadow: unread ? `inset 3px 0 0 ${UNREAD_BAR}` : undefined,
        textAlign: 'left',
        cursor: 'pointer',
        font: 'inherit',
      }}
    >
      <NotificationEntryIcon typeKeys={kinds} ring={unread ? UNREAD_BG : '#fff'} />
      {/* One sentence carries it — «Əliyev Elvin işə 25 dəqiqə gecikdi» — so it is the line; the type is the icon and the
          accessible name, not a bold «İşçi gecikdi» above it saying the same thing again. */}
      <span style={{ flex: 1, minWidth: 0 }}>
        <span
          style={{
            display: 'block', fontWeight: unread ? 700 : 500, color: unread ? 'var(--c900)' : 'var(--c600)',
            fontSize: 14.5, lineHeight: 1.4, overflowWrap: 'anywhere',
          }}
        >
          {n.body}
        </span>
        <span
          style={{
            display: 'block', color: unread ? 'var(--c600)' : 'var(--c500)', fontSize: 12.5, marginTop: 3,
            fontVariantNumeric: 'tabular-nums',
          }}
        >
          {meta}
        </span>
      </span>
      {unread && (
        <span aria-hidden="true" style={{ flex: 'none', width: 10, height: 10, borderRadius: 999, background: UNREAD_BAR }} />
      )}
    </button>
  )
}

function CenterSkeleton() {
  return (
    <div className="card" aria-hidden="true" style={{ overflow: 'hidden' }}>
      {Array.from({ length: 5 }, (_, i) => (
        <div key={i} style={{ display: 'flex', gap: 12, padding: '14px 16px', borderTop: i === 0 ? 'none' : '1px solid var(--c100)' }}>
          <div className="animate-pulse motion-reduce:animate-none" style={{ width: 36, height: 36, borderRadius: 999, background: 'var(--c100)' }} />
          <div style={{ flex: 1 }}>
            <div className="animate-pulse motion-reduce:animate-none" style={{ width: 120, height: 12, borderRadius: 6, background: 'var(--c100)' }} />
            <div className="animate-pulse motion-reduce:animate-none" style={{ width: '70%', height: 12, borderRadius: 6, marginTop: 8, background: 'var(--c100)' }} />
          </div>
        </div>
      ))}
    </div>
  )
}
