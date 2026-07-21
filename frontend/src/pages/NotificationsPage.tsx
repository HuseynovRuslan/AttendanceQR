import { useEffect, useMemo, useState } from 'react'
import type { ComponentType, SVGProps } from 'react'
import { getMyAttendance, type AttendanceRecord } from '../api/attendance'
import { EmptyCard, SkeletonList } from '../components/employeeBits'
import { IconBell, IconCheck, IconClock, IconLogout } from '../components/icons'
import { getAnnouncements } from '../api/announcements'
import { getInbox } from '../lib/push'
import { fmtDate, fmtTime } from '../lib/format'

// No 'late': a check-in is a check-in. Every employee keeps their own hours, so a location-wide
// shift cannot say who was late — telling someone they were is simply wrong.
// 'announcement' and 'reminder' join the feed so this tab is the one place everything the employee
// was told actually lives — a push banner is gone the moment it's swiped away.
type FeedType = 'checkin' | 'checkout' | 'announcement' | 'reminder'
interface FeedItem {
  id: string
  at: string
  date: string
  type: FeedType
  /** Set on announcements/reminders, which carry their own words rather than a fixed label. */
  title?: string
  body?: string
}

const SEEN_KEY = 'attendanceqr.notifSeen'

const META: Record<
  FeedType,
  { title: string; Icon: ComponentType<SVGProps<SVGSVGElement>>; ring: string }
> = {
  checkin: { title: 'Giriş qeydə alındı', Icon: IconCheck, ring: 'bg-green-100 text-green-600' },
  checkout: { title: 'Çıxış qeydə alındı', Icon: IconLogout, ring: 'bg-blue-100 text-blue-600' },
  announcement: { title: 'Elan', Icon: IconBell, ring: 'bg-amber-100 text-amber-600' },
  reminder: { title: 'Xatırlatma', Icon: IconClock, ring: 'bg-slate-100 text-slate-600' },
}

function buildFeed(records: AttendanceRecord[]): FeedItem[] {
  const items: FeedItem[] = []
  for (const r of records) {
    if (r.checkInAtUtc)
      items.push({ id: `${r.recordId}:in`, at: r.checkInAtUtc, date: r.attendanceDate, type: 'checkin' })
    if (r.checkOutAtUtc) items.push({ id: `${r.recordId}:out`, at: r.checkOutAtUtc, date: r.attendanceDate, type: 'checkout' })
  }
  return items.sort((a, b) => (a.at < b.at ? 1 : -1))
}

export function NotificationsPage() {
  const [records, setRecords] = useState<AttendanceRecord[]>([])
  const [extra, setExtra] = useState<FeedItem[]>([])
  const [loading, setLoading] = useState(true)
  const [onlyUnread, setOnlyUnread] = useState(false)
  const [seenBefore] = useState<string>(() => localStorage.getItem(SEEN_KEY) ?? '')

  useEffect(() => {
    void (async () => {
      // Three sources, one list: own scans, company announcements, and the reminders the server sent.
      const [a, ann, inbox] = await Promise.all([getMyAttendance(), getAnnouncements(), getInbox()])
      if (a.status === 200 && Array.isArray(a.data)) setRecords(a.data)

      const items: FeedItem[] = []
      if (ann.status === 200 && Array.isArray(ann.data)) {
        for (const x of ann.data) {
          items.push({
            id: `a:${x.id}`,
            at: x.createdAtUtc,
            date: x.createdAtUtc.slice(0, 10),
            type: 'announcement',
            title: x.title ?? 'Elan',
            body: x.message,
          })
        }
      }
      if (inbox.status === 200 && Array.isArray(inbox.data)) {
        for (const x of inbox.data) {
          items.push({
            id: `r:${x.id}`,
            at: x.createdAtUtc,
            date: x.createdAtUtc.slice(0, 10),
            type: 'reminder',
            title: x.title,
            body: x.body,
          })
        }
      }
      setExtra(items)
      setLoading(false)
    })()
  }, [])

  const feed = useMemo(
    () => [...buildFeed(records), ...extra].sort((a, b) => (a.at < b.at ? 1 : -1)),
    [records, extra],
  )

  // Once loaded, mark the newest event as "seen" so a later visit shows these as read.
  useEffect(() => {
    if (feed.length > 0) localStorage.setItem(SEEN_KEY, feed[0].at)
  }, [feed])

  const visible = onlyUnread ? feed.filter((f) => f.at > seenBefore) : feed
  const unreadCount = feed.filter((f) => f.at > seenBefore).length

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-center justify-between">
        <h1 className="text-xl font-extrabold">Bildirişlər</h1>
        {unreadCount > 0 && (
          <span className="rounded-full bg-blue-600 px-2.5 py-1 text-xs font-bold text-white">{unreadCount} yeni</span>
        )}
      </div>

      <div className="flex gap-2">
        <FilterTab label="Hamısı" active={!onlyUnread} onClick={() => setOnlyUnread(false)} />
        <FilterTab label="Oxunmayıb" active={onlyUnread} onClick={() => setOnlyUnread(true)} />
      </div>

      {loading ? (
        <SkeletonList rows={5} />
      ) : visible.length === 0 ? (
        <EmptyCard text={onlyUnread ? 'Oxunmamış bildiriş yoxdur' : 'Hələ bildiriş yoxdur'} />
      ) : (
        <FeedList items={visible} unreadSince={seenBefore} />
      )}
    </div>
  )
}

function FeedList({ items, unreadSince }: { items: FeedItem[]; unreadSince: string }) {
  const groups: { date: string; items: FeedItem[] }[] = []
  for (const it of items) {
    const last = groups[groups.length - 1]
    if (last && last.date === it.date) last.items.push(it)
    else groups.push({ date: it.date, items: [it] })
  }
  return (
    <div className="flex flex-col gap-4">
      {groups.map((g) => (
        <div key={g.date}>
          <div className="mb-2 px-1 text-xs font-bold uppercase tracking-wide text-slate-400">{fmtDate(g.date)}</div>
          <div className="flex flex-col gap-2">
            {g.items.map((it) => {
              const m = META[it.type]
              const unread = it.at > unreadSince
              return (
                <div
                  key={it.id}
                  className={`flex items-center gap-3 rounded-2xl border p-3.5 shadow-sm ${
                    unread ? 'border-blue-100 bg-blue-50/60' : 'border-slate-100 bg-white'
                  }`}
                >
                  <div className={`flex h-10 w-10 shrink-0 items-center justify-center rounded-full ${m.ring}`}>
                    <m.Icon className="h-5 w-5" />
                  </div>
                  <div className="min-w-0 flex-1">
                    <div className="font-semibold">{it.title ?? m.title}</div>
                    {it.body ? (
                      <div className="whitespace-pre-line text-sm text-slate-600">{it.body}</div>
                    ) : null}
                    <div className="text-sm text-slate-500">Saat {fmtTime(it.at)}</div>
                  </div>
                  {unread && <span className="h-2.5 w-2.5 shrink-0 rounded-full bg-blue-600" />}
                </div>
              )
            })}
          </div>
        </div>
      ))}
    </div>
  )
}

function FilterTab({ label, active, onClick }: { label: string; active: boolean; onClick: () => void }) {
  return (
    <button
      onClick={onClick}
      className={`rounded-full px-4 py-1.5 text-sm font-semibold transition ${
        active ? 'bg-slate-900 text-white' : 'bg-white text-slate-500 border border-slate-200'
      }`}
    >
      {label}
    </button>
  )
}
