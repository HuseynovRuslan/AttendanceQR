import { useEffect, useState } from 'react'
import { getAnnouncements, type Announcement } from '../api/announcements'

// Which announcements this employee has already dismissed (by id). Kept in localStorage so a dismissed
// banner stays gone across visits — but it reappears for everyone if the admin posts a NEW one.
const SEEN_KEY = 'attendanceqr.announcementsSeen'

function seenIds(): Set<string> {
  try {
    return new Set(JSON.parse(localStorage.getItem(SEEN_KEY) ?? '[]') as string[])
  } catch {
    return new Set()
  }
}

function markSeen(id: string) {
  const ids = seenIds()
  ids.add(id)
  localStorage.setItem(SEEN_KEY, JSON.stringify([...ids]))
}

/** Dismissible banners for the tenant's active announcements. Silent (renders nothing) when there are
 *  none, or when the employee has dismissed them all. Best-effort — a failed fetch just shows nothing. */
export function AnnouncementBanner() {
  const [items, setItems] = useState<Announcement[]>([])

  useEffect(() => {
    let cancelled = false
    void getAnnouncements().then((r) => {
      if (cancelled || r.status !== 200 || !Array.isArray(r.data)) return
      const seen = seenIds()
      setItems(r.data.filter((a) => !seen.has(a.id)))
    })
    return () => {
      cancelled = true
    }
  }, [])

  function dismiss(id: string) {
    markSeen(id)
    setItems((list) => list.filter((a) => a.id !== id))
  }

  if (items.length === 0) return null

  return (
    <div className="flex flex-col gap-2">
      {items.map((a) => (
        <div
          key={a.id}
          className="flex items-start gap-3 rounded-3xl border border-amber-200 bg-amber-50 p-4 shadow-sm"
        >
          <span className="text-xl leading-none">📣</span>
          <p className="min-w-0 flex-1 whitespace-pre-line text-sm font-medium text-amber-900">{a.message}</p>
          <button
            onClick={() => dismiss(a.id)}
            aria-label="Bağla"
            className="shrink-0 rounded-full px-2 text-amber-500 transition hover:text-amber-700"
          >
            ✕
          </button>
        </div>
      ))}
    </div>
  )
}
