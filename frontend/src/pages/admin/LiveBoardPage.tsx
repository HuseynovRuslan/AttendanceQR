import { useCallback, useEffect, useMemo, useState } from 'react'
import { getToday, type DayAttendanceRow } from '../../api/admin'
import { fmtTime } from '../../lib/format'
import { IconX } from '../../components/icons'

const REFRESH_MS = 20_000

type Bucket = 'in' | 'out' | 'absent' | 'off'

// What is this person doing RIGHT NOW? Derived from the raw check-in/out times so the board reads the
// same whatever the backend called the status. "in" = at work now (checked in, not out yet).
function bucketOf(r: DayAttendanceRow): Bucket {
  if (r.checkOutAtUtc) return 'out'
  if (r.checkInAtUtc) return 'in'
  if (r.status === 'DayOff' || r.status === 'OnLeave' || r.status === 'Permission') return 'off'
  return 'absent'
}

const BUCKET_ORDER: Record<Bucket, number> = { in: 0, absent: 1, out: 2, off: 3 }
const OFF_LABEL: Record<string, string> = { DayOff: 'İş günü deyil', OnLeave: 'Məzuniyyət', Permission: 'İcazəli' }

function twoDigit(n: number) {
  return String(n).padStart(2, '0')
}

export function LiveBoardPage() {
  const [rows, setRows] = useState<DayAttendanceRow[]>([])
  const [error, setError] = useState<string | null>(null)
  const [loadedOnce, setLoadedOnce] = useState(false)
  const [filterLoc, setFilterLoc] = useState<string | null>(null)
  const [updatedAt, setUpdatedAt] = useState<Date | null>(null)
  const [now, setNow] = useState(() => new Date())

  const load = useCallback(async () => {
    const { status, data } = await getToday()
    if (status === 200 && Array.isArray(data)) {
      setRows(data)
      setError(null)
      setUpdatedAt(new Date())
    } else if (status === 403) {
      setError('İcazəniz yoxdur')
    } else {
      setError('Məlumat yüklənmədi')
    }
    setLoadedOnce(true)
  }, [])

  // Poll the live data; the clock ticks separately every second so the board always feels alive.
  useEffect(() => {
    void load()
    const dataId = setInterval(() => void load(), REFRESH_MS)
    const clockId = setInterval(() => setNow(new Date()), 1000)
    return () => {
      clearInterval(dataId)
      clearInterval(clockId)
    }
  }, [load])

  const locations = useMemo(() => {
    const seen = new Map<string, string>()
    for (const r of rows) seen.set(r.locationId, r.locationName)
    return Array.from(seen, ([id, name]) => ({ id, name })).sort((a, b) => a.name.localeCompare(b.name))
  }, [rows])

  const scoped = filterLoc ? rows.filter((r) => r.locationId === filterLoc) : rows

  const counts = useMemo(() => {
    const c = { in: 0, out: 0, absent: 0, off: 0, late: 0 }
    for (const r of scoped) {
      c[bucketOf(r)]++
      if (r.status === 'Late') c.late++
    }
    return c
  }, [scoped])

  // Group by branch, and inside each branch sort by "who matters now" then by name.
  const byLocation = useMemo(() => {
    const map = new Map<string, { name: string; rows: DayAttendanceRow[] }>()
    for (const r of scoped) {
      let g = map.get(r.locationId)
      if (!g) {
        g = { name: r.locationName, rows: [] }
        map.set(r.locationId, g)
      }
      g.rows.push(r)
    }
    for (const g of map.values()) {
      g.rows.sort((a, b) => {
        const d = BUCKET_ORDER[bucketOf(a)] - BUCKET_ORDER[bucketOf(b)]
        return d !== 0 ? d : a.employeeName.localeCompare(b.employeeName)
      })
    }
    return Array.from(map.values()).sort((a, b) => a.name.localeCompare(b.name))
  }, [scoped])

  function toggleFullscreen() {
    if (document.fullscreenElement) void document.exitFullscreen()
    else void document.documentElement.requestFullscreen().catch(() => {})
  }

  return (
    <div>
      <div className="live-head">
        <span className="live-dot" aria-hidden />
        <span className="live-live">Canlı</span>
        <span className="live-clock">{twoDigit(now.getHours())}:{twoDigit(now.getMinutes())}:{twoDigit(now.getSeconds())}</span>
        {updatedAt && (
          <span className="live-updated">
            Son yeniləmə {twoDigit(updatedAt.getHours())}:{twoDigit(updatedAt.getMinutes())}:{twoDigit(updatedAt.getSeconds())}
          </span>
        )}
        <button className="btn btn-sm live-fs" onClick={toggleFullscreen}>⛶ Tam ekran</button>
      </div>

      {locations.length > 1 && (
        <div className="chip-row">
          <span className={`chip${!filterLoc ? ' active' : ''}`} onClick={() => setFilterLoc(null)}>
            Bütün filiallar
          </span>
          {locations.map((l) => (
            <span
              key={l.id}
              className={`chip${filterLoc === l.id ? ' active' : ''}`}
              onClick={() => setFilterLoc(l.id)}
            >
              {l.name}
            </span>
          ))}
        </div>
      )}

      {error && (
        <div className="fb fb-err" style={{ marginBottom: 12 }}>
          <IconX />
          <span>{error}</span>
        </div>
      )}

      <div className="live-hero">
        <div className="live-tile in">
          <div className="lbl">İşdə</div>
          <div className="val">{counts.in}</div>
          <div className="sub">Hazırda işdədir</div>
        </div>
        <div className="live-tile out">
          <div className="lbl">Çıxıb</div>
          <div className="val">{counts.out}</div>
          <div className="sub">Bu gün işini bitirib</div>
        </div>
        <div className="live-tile absent">
          <div className="lbl">Gəlməyib</div>
          <div className="val">{counts.absent}</div>
          <div className="sub">Heç giriş etməyib</div>
        </div>
        <div className="live-tile late">
          <div className="lbl">Gecikib</div>
          <div className="val">{counts.late}</div>
          <div className="sub">Bu gün gec gəlib</div>
        </div>
      </div>

      {byLocation.map((g) => {
        const inNow = g.rows.filter((r) => bucketOf(r) === 'in').length
        const expected = g.rows.filter((r) => bucketOf(r) !== 'off').length
        return (
          <div className="live-section" key={g.name}>
            <div className="live-section-head">
              <h3>{g.name}</h3>
              <span className="meta">{inNow}/{expected} işdə</span>
            </div>
            <div className="live-grid">
              {g.rows.map((r) => {
                const b = bucketOf(r)
                const late = r.status === 'Late'
                return (
                  <div className={`live-card ${b}`} key={r.employeeId}>
                    <span className="nm">{r.employeeName}</span>
                    {b === 'in' && <span className="tm">Giriş {fmtTime(r.checkInAtUtc)}</span>}
                    {b === 'out' && <span className="tm">{fmtTime(r.checkInAtUtc)} → {fmtTime(r.checkOutAtUtc)}</span>}
                    {b === 'absent' && <span className="tm">Gəlməyib</span>}
                    {b === 'off' && <span className="tm">{OFF_LABEL[r.status] ?? 'Yoxdur'}</span>}
                    {late && (b === 'in' || b === 'out') && <span className="live-late">Gecikib</span>}
                  </div>
                )
              })}
            </div>
          </div>
        )
      })}

      {loadedOnce && !error && rows.length === 0 && (
        <div className="muted" style={{ textAlign: 'center', padding: 40 }}>
          Bu gün üçün məlumat yoxdur
        </div>
      )}
    </div>
  )
}
