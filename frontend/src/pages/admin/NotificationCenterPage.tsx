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
import { NotificationKindIcon } from '../../components/NotificationKindIcon'
import { IconCheck, IconRefresh } from '../../components/icons'
import { subscribeLive } from '../../lib/notificationHub'
import { groupByDay, lookOf, metaLine } from '../../lib/notificationKinds'
import { emitNotificationsChanged } from '../../lib/notificationEvents'

/**
 * «Bildirişlər» — the person's notification history: newest first, grouped by company day, read and unread, filtered
 * by type and branch, paged by the server (a cursor, so new arrivals never shift a page). New notifications appear at
 * the top as they are written (the live channel); after a reconnect the first page is fetched again.
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

export function NotificationCenterPage() {
  const navigate = useNavigate()
  const [type, setType] = useState('')
  const [branch, setBranch] = useState('')
  const [branches, setBranches] = useState<PreferenceBranch[]>([])
  const [items, setItems] = useState<CenterNotification[]>([])
  const [cursor, setCursor] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [loadingMore, setLoadingMore] = useState(false)
  const [error, setError] = useState(false)
  const [marking, setMarking] = useState(false)
  const [actionError, setActionError] = useState<string | null>(null)
  // Unread anywhere in the history, not just on the loaded page — what «Hamısını oxunmuş et» acts on.
  const [serverUnread, setServerUnread] = useState(0)
  const [fresh, setFresh] = useState(0)
  const filters = useRef({ type, branch })
  filters.current = { type, branch }

  const load = useCallback(async () => {
    setLoading(true)
    setError(false)
    const { status, data } = await getCenter({ type: type || undefined, locationId: branch || undefined, limit: 30 })
    setLoading(false)
    if (status === 200 && data) {
      setItems(data.items)
      setCursor(data.nextCursor)
      setFresh(0)
    } else {
      setError(true)
    }
    const count = await getUnreadCount()
    if (count.status === 200 && typeof count.data?.unread === 'number') setServerUnread(count.data.unread)
  }, [type, branch])

  useEffect(() => {
    void load()
  }, [load])

  useEffect(() => {
    void getPreferences().then(({ status, data }) => {
      if (status === 200 && data) setBranches(data.branches)
    })
  }, [])

  // Live: a new line for the current filter goes to the top; a pop-up of a type filtered out only bumps nothing.
  useEffect(
    () =>
      subscribeLive({
        onItems: (live) => {
          const { type: t, branch: b } = filters.current
          const add = live
            .filter((x) => x.id && (!t || x.typeKey === t) && !b)
            .map<CenterNotification>((x) => ({
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
            }))
          if (add.length > 0) {
            setItems((cur) => [...add.filter((a) => !cur.some((c) => c.id === a.id)), ...cur])
            setFresh((n) => n + add.length)
          }
          const lines = live.filter((x) => x.id).length
          if (lines > 0) setServerUnread((n) => n + lines)
          // A branch filter cannot be judged from the pop-up (it carries no branch id): reload instead.
          if (b && live.some((x) => x.id)) void load()
        },
        onReconnected: () => void load(),
      }),
    [load],
  )

  async function more() {
    if (!cursor) return
    setLoadingMore(true)
    setActionError(null)
    const { status, data } = await getCenter({ type: type || undefined, locationId: branch || undefined, cursor, limit: 30 })
    setLoadingMore(false)
    if (status === 200 && data) {
      setItems((cur) => [...cur, ...data.items.filter((d) => !cur.some((c) => c.id === d.id))])
      setCursor(data.nextCursor)
    } else {
      setActionError('Daha köhnə bildirişlər yüklənmədi. Bir azdan yenidən cəhd edin.')
    }
  }

  async function open(n: CenterNotification) {
    if (!n.readAtUtc) {
      setItems((cur) => cur.map((c) => (c.id === n.id ? { ...c, readAtUtc: new Date().toISOString() } : c)))
      setServerUnread((u) => Math.max(0, u - 1))
      void markRead(n.id).then(() => emitNotificationsChanged())
    }
    if (n.url) navigate(n.url)
  }

  async function readAll() {
    setMarking(true)
    setActionError(null)
    const { status } = await markAllRead(type || undefined)
    setMarking(false)
    if (status === 200) {
      const now = new Date().toISOString()
      setItems((cur) => cur.map((c) => (c.readAtUtc ? c : { ...c, readAtUtc: now })))
      setServerUnread(0)
      emitNotificationsChanged()
    } else {
      setActionError('Oxunmuş kimi qeyd olunmadı. İnternet bağlantısını yoxlayıb yenidən cəhd edin.')
    }
  }

  const unread = Math.max(items.filter((i) => !i.readAtUtc).length, serverUnread)
  const groups = groupByDay(items, (i) => i.createdAtUtc)

  return (
    <div style={{ maxWidth: 760 }}>
      <div style={{ display: 'flex', alignItems: 'flex-start', gap: 12, flexWrap: 'wrap', marginBottom: 16 }}>
        <div style={{ flex: 1, minWidth: 220 }}>
          <h1 style={{ margin: 0, fontSize: 22, fontWeight: 800, color: 'var(--c900)', letterSpacing: '-0.01em' }}>Bildirişlər</h1>
          <div className="muted" style={{ fontSize: 13, marginTop: 4 }}>
            Səlahiyyətinizdə olan filialların işçiləri üzrə giriş, çıxış və gecikmə bildirişləri.{' '}
            <Link to="/menu/notifications" style={{ color: 'var(--blue)', fontWeight: 600, textDecoration: 'underline', textUnderlineOffset: 2 }}>Bildiriş ayarları</Link>
          </div>
        </div>
        <button
          type="button"
          className="btn"
          onClick={() => void readAll()}
          disabled={marking || unread === 0}
          style={{ minHeight: 44 }}
        >
          <IconCheck />
          Hamısını oxunmuş et
        </button>
      </div>

      <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap', alignItems: 'center', marginBottom: 14 }}>
        <div className="seg" role="group" aria-label="Bildiriş növü" style={{ flex: '1 1 320px' }}>
          {TYPES.map((t) => (
            <button
              key={t.key || 'all'}
              type="button"
              className="seg-btn"
              aria-pressed={type === t.key}
              onClick={() => setType(t.key)}
              style={{ minHeight: 40 }}
            >
              {t.label}
            </button>
          ))}
        </div>
        {branches.length > 1 && (
          <label style={{ flex: '0 1 240px' }}>
            <span className="sr-only">Filial</span>
            <select className="inp" value={branch} onChange={(e) => setBranch(e.target.value)} style={{ minHeight: 44 }}>
              <option value="">Bütün filiallar</option>
              {branches.map((b) => (
                <option key={b.id} value={b.id}>{b.name}</option>
              ))}
            </select>
          </label>
        )}
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
            {type || branch ? 'Bu seçimə uyğun bildiriş yoxdur' : 'Hələ bildiriş yoxdur'}
          </div>
          <div className="muted" style={{ fontSize: 13, marginTop: 6, lineHeight: 1.5 }}>
            {type || branch
              ? 'Filtri dəyişin və ya «Hamısı»nı seçin.'
              : 'İşçi giriş, çıxış etdikdə və ya gecikdikdə bildirişlər burada görünəcək. Hansı bildirişləri alacağınızı ayarlarda seçin.'}
          </div>
          {!type && !branch && (
            <Link to="/menu/notifications" className="btn" style={{ marginTop: 14, display: 'inline-flex' }}>
              Bildiriş ayarları
            </Link>
          )}
        </div>
      )}

      {groups.map((g) => (
        <section key={g.day} aria-label={g.heading} style={{ marginBottom: 18 }}>
          <h2 style={{ fontSize: 13, fontWeight: 700, color: 'var(--c500)', margin: '0 0 8px 4px' }}>{g.heading}</h2>
          <div className="card" style={{ overflow: 'hidden' }}>
            {g.items.map((n, i) => (
              <button
                key={n.id}
                type="button"
                onClick={() => void open(n)}
                className="notif-row"
                aria-label={`${lookOf(n.typeKey).label}: ${n.body} ${metaLine(n.branchName, n.occurredAtUtc, n.wasOffline)}${n.readAtUtc ? '' : ', oxunmayıb'}`}
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  gap: 12,
                  width: '100%',
                  padding: '12px 16px',
                  minHeight: 60,
                  border: 'none',
                  borderTop: i === 0 ? 'none' : '1px solid var(--c100)',
                  background: n.readAtUtc ? 'var(--white, #fff)' : 'var(--c50)',
                  textAlign: 'left',
                  cursor: 'pointer',
                  font: 'inherit',
                }}
              >
                <NotificationKindIcon typeKey={n.typeKey} />
                {/* One sentence carries it — «Əliyev Elvin işə 25 dəqiqə gecikdi» — so it is the line; the type is the
                    icon and the accessible name, not a bold «İşçi gecikdi» above it saying the same thing again. */}
                <span style={{ flex: 1, minWidth: 0 }}>
                  <span
                    style={{
                      display: 'block', fontWeight: n.readAtUtc ? 500 : 700, color: n.readAtUtc ? 'var(--c700)' : 'var(--c900)',
                      fontSize: 14.5, lineHeight: 1.4, overflowWrap: 'anywhere',
                    }}
                  >
                    {n.body}
                  </span>
                  <span style={{ display: 'block', color: 'var(--c500)', fontSize: 12.5, marginTop: 3, fontVariantNumeric: 'tabular-nums' }}>
                    {metaLine(n.branchName, n.occurredAtUtc, n.wasOffline)}
                  </span>
                </span>
                {!n.readAtUtc && (
                  <span aria-hidden="true" style={{ flex: 'none', width: 9, height: 9, borderRadius: 999, background: 'var(--blue, #2563EB)', marginTop: 6 }} />
                )}
              </button>
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
