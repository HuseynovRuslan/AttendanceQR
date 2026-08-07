import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { getMyFieldVisits, type MyFieldVisit } from '../api/fieldVisits'

/**
 * Home-screen banner for the worker's field visits: shows the moment a manager assigns one (or while
 * they're on site so they don't forget to check out), and taps straight through to /field. The reliable
 * counterpart to the best-effort push sent on assign — a worker who never enabled notifications still
 * sees the task the next time they open the app.
 */
export function FieldVisitBanner() {
  const navigate = useNavigate()
  const [visits, setVisits] = useState<MyFieldVisit[]>([])

  useEffect(() => {
    void getMyFieldVisits().then((r) => {
      if (r.status === 200 && Array.isArray(r.data)) setVisits(r.data)
    })
  }, [])

  const assigned = visits.filter((v) => v.status === 'Assigned')
  const onSite = visits.find((v) => v.status === 'CheckedIn')
  if (assigned.length === 0 && !onSite) return null

  const title = onSite ? 'Ərazidəsən — çıxışı unutma' : 'Sənə sahə tapşırığı var'
  const sub = onSite
    ? onSite.targetLabel || 'Sahə ziyarəti'
    : assigned.length === 1
      ? assigned[0].targetLabel || 'Sahə ziyarəti'
      : `${assigned.length} tapşırıq — bax`
  const tone = onSite ? 'bg-amber-500 text-slate-900' : 'bg-blue-600 text-white'

  return (
    <button
      onClick={() => navigate('/field')}
      className={`w-full max-w-md mx-auto flex items-center gap-3 rounded-2xl px-4 py-3 mb-3 text-left shadow-lg ${tone}`}
    >
      <span className="text-2xl leading-none">📍</span>
      <div className="flex-1 min-w-0">
        <div className="font-bold leading-tight">{title}</div>
        <div className="text-sm opacity-90 truncate">{sub}</div>
      </div>
      <span className="text-xl font-bold">→</span>
    </button>
  )
}
