import { useEffect, useMemo, useRef, useState, type FormEvent } from 'react'
import { Circle, CircleMarker, MapContainer, TileLayer, Tooltip, useMap } from 'react-leaflet'
import 'leaflet/dist/leaflet.css'
import { LocationMapPicker } from '../../components/LocationMapPicker'
import { EmployeeLink } from '../../components/EmployeeLink'
import {
  getFieldVisitBoard,
  getAssignablePeople,
  assignFieldVisit,
  cancelFieldVisit,
  forceCheckOutFieldVisit,
  getFieldVisitChecklist,
  getFieldVisitWorkPhoto,
  type BoardFieldVisit,
  type AssignablePerson,
  type ChecklistItem,
} from '../../api/fieldVisits'
import { getPosition } from '../../lib/geo'
import { fmtTime } from '../../lib/format'

const STATUS: Record<string, { label: string; bg: string; fg: string }> = {
  Assigned: { label: 'Tapşırılıb', bg: 'var(--purple-bg)', fg: 'var(--purple)' },
  CheckedIn: { label: 'Ərazidə', bg: 'var(--amber-bg)', fg: 'var(--amber)' },
  Completed: { label: 'Tamamlandı', bg: 'var(--leaf-bg)', fg: 'var(--leaf-d)' },
  Cancelled: { label: 'Ləğv', bg: 'rgba(0,0,0,0.05)', fg: 'var(--c400)' },
}

const BAKU: [number, number] = [40.4093, 49.8671]

/** Mirrors the server cap. The server is the real rule (it trims silently); this is a courtesy. */
const MAX_CHECKLIST = 10
/** The words these crews actually use. A hardcoded starter set, NOT a catalogue — no table, no page:
 *  a manager assigning ~20 near-identical visits will not type Azerbaijani ə/ş/ç/ı sixty times. */
const QUICK_TASKS = ['süpür', 'zibili boşalt', 'sula', 'ot biç', 'döşəməni yu']
// Local calendar date "YYYY-MM-DD". Deliberately NOT toISOString() — that returns the UTC date, which
// in Baku (UTC+4) is the previous day before 04:00 and breaks the day navigation round-trip.
const todayStr = () => {
  const n = new Date()
  return `${n.getFullYear()}-${String(n.getMonth() + 1).padStart(2, '0')}-${String(n.getDate()).padStart(2, '0')}`
}

function fmtDist(m: number): string {
  return m >= 1000 ? `${(m / 1000).toFixed(1)} km` : `${Math.round(m)} m`
}
/** A CheckedIn visit whose day has already passed = the worker forgot to check out. */
function isStuck(v: BoardFieldVisit, date: string): boolean {
  return v.status === 'CheckedIn' && date < todayStr()
}
/**
 * Arrived outside the pinned radius. This is the ONLY thing that may colour or word the distance
 * verdict — it is a statement about where a named person stood, and it must never be driven by the
 * broader "needs attention" flag below. (It was, briefly: a worker who arrived 12 m from a 200 m
 * target but left one line unticked was printed as "⚠️ 12 m uzaq" on the board.)
 */
function isFarFromTarget(v: BoardFieldVisit): boolean {
  return v.checkInDistanceMeters != null && v.targetRadiusMeters != null && v.checkInDistanceMeters > v.targetRadiusMeters
}

/**
 * "The visit closed but something doesn't add up" — one attention number rather than several the
 * manager has to reconcile. Advisory only: nothing here blocked the worker, and the per-row chips
 * still show WHICH cause it was. Used for the stat card and the «Yalnız diqqət» filter, never for
 * the distance wording.
 *
 * Deliberately NOT included: "completed with no departure GPS". An admin force-close leaves exactly
 * that shape (FieldVisitController.ForceCheckOut stamps a time and no position), and the board row
 * carries no flag to tell the two apart — so it would count the admin's own cleanup as a worker's
 * omission, forever, with no way to clear it. Restore it only behind a real ForceClosedAtUtc column.
 */
function isFlagged(v: BoardFieldVisit): boolean {
  const workLeftUnticked = v.status === 'Completed' && v.checklistTotal > 0 && v.checklistDone < v.checklistTotal
  return isFarFromTarget(v) || workLeftUnticked
}

// Read-only map fitting all of a visit's points (target + check-in + check-out).
function FitPts({ pts }: { pts: [number, number][] }) {
  const map = useMap()
  useEffect(() => {
    if (pts.length === 0) return
    if (pts.length === 1) { map.setView(pts[0], 16); return }
    map.fitBounds(pts, { padding: [40, 40], maxZoom: 17 })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [pts.length])
  return null
}

function VisitMap({ v }: { v: BoardFieldVisit }) {
  const marks: { at: [number, number]; label: string; color: string }[] = []
  if (v.targetLatitude != null) marks.push({ at: [v.targetLatitude, v.targetLongitude!], label: 'Hədəf', color: '#1E70C8' })
  if (v.checkInLatitude != null) marks.push({ at: [v.checkInLatitude, v.checkInLongitude!], label: 'Giriş', color: '#16a34a' })
  if (v.checkOutLatitude != null) marks.push({ at: [v.checkOutLatitude, v.checkOutLongitude!], label: 'Çıxış', color: '#d97706' })
  const centre: [number, number] = marks.length ? marks[0].at : BAKU
  return (
    <div style={{ height: 300, width: '100%', borderRadius: 10, overflow: 'hidden', border: '1px solid var(--c100)' }}>
      <MapContainer center={centre} zoom={15} scrollWheelZoom style={{ height: '100%', width: '100%' }} attributionControl={false}>
        <TileLayer url="https://{s}.basemaps.cartocdn.com/light_all/{z}/{x}/{y}{r}.png" />
        <FitPts pts={marks.map((m) => m.at)} />
        {v.targetLatitude != null && v.targetRadiusMeters != null && (
          <Circle center={[v.targetLatitude, v.targetLongitude!]} radius={v.targetRadiusMeters}
            pathOptions={{ color: '#1E70C8', fillColor: '#1E70C8', fillOpacity: 0.08, weight: 1.5 }} />
        )}
        {marks.map((m, i) => (
          <CircleMarker key={i} center={m.at} radius={8} pathOptions={{ color: '#fff', weight: 2, fillColor: m.color, fillOpacity: 0.95 }}>
            <Tooltip direction="top">{m.label}</Tooltip>
          </CircleMarker>
        ))}
      </MapContainer>
    </div>
  )
}

type StatusFilter = 'all' | 'Assigned' | 'CheckedIn' | 'Completed' | 'Cancelled'

export function FieldVisitsAdminPage() {
  const [date, setDate] = useState(todayStr)
  const [rows, setRows] = useState<BoardFieldVisit[]>([])
  const [people, setPeople] = useState<AssignablePerson[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  // filters
  const [statusFilter, setStatusFilter] = useState<StatusFilter>('all')
  const [search, setSearch] = useState('')
  const [onlyFlagged, setOnlyFlagged] = useState(false)
  const [onlySelf, setOnlySelf] = useState(false)
  const [onlyPhoto, setOnlyPhoto] = useState(false)

  // assign form
  const [showForm, setShowForm] = useState(false)
  const [employeeId, setEmployeeId] = useState('')
  const [label, setLabel] = useState('')
  const [useTarget, setUseTarget] = useState(false)
  const [lat, setLat] = useState(BAKU[0])
  const [lng, setLng] = useState(BAKU[1])
  const [radius, setRadius] = useState(200)
  const [note, setNote] = useState('')
  const [checklist, setChecklist] = useState<string[]>([])
  const [saving, setSaving] = useState(false)

  const [detail, setDetail] = useState<BoardFieldVisit | null>(null)
  const [detailChecklist, setDetailChecklist] = useState<ChecklistItem[] | null>(null)
  const [detailWorkPhoto, setDetailWorkPhoto] = useState<{ url: string | null } | null>(null)
  const [lightbox, setLightbox] = useState<string | null>(null)
  const detailReq = useRef(0)

  const isToday = date === todayStr()

  async function load(spinner = true) {
    if (spinner) setLoading(true)
    const res = await getFieldVisitBoard(date)
    if (spinner) setLoading(false)
    if (res.status === 200 && Array.isArray(res.data)) {
      setRows(res.data)
      setError(null)
    } else if (res.status === 403) setError('İcazəniz yoxdur')
    else setError('Yüklənmədi')
  }
  useEffect(() => {
    void load()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [date])
  useEffect(() => {
    void getAssignablePeople().then((r) => {
      if (r.status === 200 && Array.isArray(r.data)) setPeople(r.data)
    })
  }, [])
  // Live board: while viewing today, refresh silently every 30s so check-ins appear as they happen.
  useEffect(() => {
    if (!isToday) return
    const t = window.setInterval(() => void load(false), 30_000)
    return () => window.clearInterval(t)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [date])

  const stats = useMemo(() => {
    let assigned = 0, onsite = 0, completed = 0, minutes = 0, flagged = 0
    for (const v of rows) {
      if (v.status === 'Assigned') assigned++
      else if (v.status === 'CheckedIn') onsite++
      else if (v.status === 'Completed') { completed++; minutes += v.durationMinutes ?? 0 }
      if (isFlagged(v)) flagged++
    }
    return { assigned, onsite, completed, minutes, flagged }
  }, [rows])

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase()
    return rows.filter((v) => {
      if (statusFilter !== 'all' && v.status !== statusFilter) return false
      if (onlyFlagged && !isFlagged(v)) return false
      if (onlySelf && !v.selfReported) return false
      if (onlyPhoto && !v.hasWorkPhoto) return false
      if (q && !(v.employeeName.toLowerCase().includes(q) || (v.phone ?? '').toLowerCase().includes(q))) return false
      return true
    })
  }, [rows, statusFilter, search, onlyFlagged, onlySelf, onlyPhoto])

  async function useMyLocation() {
    const geo = await getPosition()
    if (geo.ok) { setLat(geo.coords.latitude); setLng(geo.coords.longitude); setUseTarget(true) }
    else setError('GPS alınmadı')
  }

  async function submit(e: FormEvent) {
    e.preventDefault()
    if (!employeeId) { setError('İşçi seçin'); return }
    setSaving(true)
    setError(null)
    const { status } = await assignFieldVisit({
      employeeId,
      targetLabel: label.trim() || null,
      targetLatitude: useTarget ? lat : null,
      targetLongitude: useTarget ? lng : null,
      targetRadiusMeters: useTarget ? radius : null,
      visitDate: date,
      note: note.trim() || null,
      checklist: checklist.length > 0 ? checklist : null,
    })
    setSaving(false)
    if (status === 200) {
      setLabel(''); setNote(''); setEmployeeId(''); setUseTarget(false); setChecklist([]); setShowForm(false)
      await load()
    } else setError('Tapşırıq yaradıla bilmədi')
  }

  async function cancel(v: BoardFieldVisit) {
    if (!window.confirm(`«${v.employeeName}» tapşırığı ləğv edilsin?`)) return
    const { status } = await cancelFieldVisit(v.id)
    if (status === 200) { setDetail(null); await load() }
    else setError('Ləğv edilmədi')
  }

  async function forceCheckout(v: BoardFieldVisit) {
    if (!window.confirm(`«${v.employeeName}» çıxış etməyib. Ziyarəti indi bağlamaq istəyirsiniz?`)) return
    const { status } = await forceCheckOutFieldVisit(v.id)
    if (status === 200) { setDetail(null); await load() }
    else setError('Bağlanmadı')
  }

  async function openDetail(v: BoardFieldVisit) {
    setDetail(v)
    setDetailChecklist(null)
    setDetailWorkPhoto(null)
    // One request counter guards BOTH fetches: opening A, closing it and opening B must never let A's
    // slower response paint into B's panel.
    const reqId = ++detailReq.current
    if (v.checklistTotal > 0) {
      const { status, data } = await getFieldVisitChecklist(v.id)
      if (reqId !== detailReq.current) return
      setDetailChecklist(status === 200 && data ? data.items : [])
    }
    if (v.hasWorkPhoto) {
      const { status, data } = await getFieldVisitWorkPhoto(v.id)
      if (reqId !== detailReq.current) return
      setDetailWorkPhoto(status === 200 && data ? { url: data.url } : { url: null })
    }
  }

  function shiftDay(delta: number) {
    // Local-component math — see todayStr; toISOString() would skew the date by the Baku offset.
    const [y, m, d] = date.split('-').map(Number)
    const dt = new Date(y, m - 1, d + delta)
    setDate(`${dt.getFullYear()}-${String(dt.getMonth() + 1).padStart(2, '0')}-${String(dt.getDate()).padStart(2, '0')}`)
  }

  const chips: { key: StatusFilter; label: string }[] = [
    { key: 'all', label: 'Hamısı' },
    { key: 'Assigned', label: 'Tapşırılıb' },
    { key: 'CheckedIn', label: 'Ərazidə' },
    { key: 'Completed', label: 'Tamamlandı' },
    { key: 'Cancelled', label: 'Ləğv' },
  ]

  return (
    <div>
      {/* Header */}
      <div style={{ display: 'flex', alignItems: 'center', gap: 12, marginBottom: 18, flexWrap: 'wrap' }}>
        <div style={{ minWidth: 0 }}>
          <h1 style={{ fontSize: 18, fontWeight: 800, color: 'var(--c900)', margin: 0 }}>Sahə ziyarətləri</h1>
          <div className="muted" style={{ fontSize: 13 }}>QR-suz GPS davamiyyət — kim harada, nə vaxt oldu.</div>
        </div>
        <div style={{ flex: 1 }} />
        {isToday && <span className="lux-live"><i />CANLI</span>}
        <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
          <button className="btn btn-sm" onClick={() => shiftDay(-1)}>←</button>
          <input className="inp" type="date" value={date} onChange={(e) => setDate(e.target.value)} style={{ maxWidth: 160 }} />
          <button className="btn btn-sm" onClick={() => shiftDay(1)}>→</button>
        </div>
        <button className="btn btn-primary" onClick={() => setShowForm((s) => !s)}>
          {showForm ? 'Bağla' : '+ Yeni tapşırıq'}
        </button>
      </div>

      {error && <div className="fb fb-err" style={{ marginBottom: 14 }}>{error}</div>}

      {/* Summary — each card is a one-tap filter */}
      <div className="stat-grid">
        <StatCard variant="purple" label="Tapşırılıb" value={stats.assigned} sub="girişi gözlənilir"
          active={statusFilter === 'Assigned'} onClick={() => { setOnlyFlagged(false); setStatusFilter(statusFilter === 'Assigned' ? 'all' : 'Assigned') }} />
        <StatCard variant="amber" label="Hazırda sahədə" value={stats.onsite} sub="girib, çıxmayıb"
          active={statusFilter === 'CheckedIn'} onClick={() => { setOnlyFlagged(false); setStatusFilter(statusFilter === 'CheckedIn' ? 'all' : 'CheckedIn') }} />
        <StatCard variant="leaf" label="Tamamlandı" value={stats.completed} sub={`${Math.round(stats.minutes / 60)} saat sahədə`}
          active={statusFilter === 'Completed'} onClick={() => { setOnlyFlagged(false); setStatusFilter(statusFilter === 'Completed' ? 'all' : 'Completed') }} />
        <StatCard variant="clay" label="Diqqət tələb edir" value={stats.flagged} sub="uzaq giriş / yarımçıq iş"
          active={onlyFlagged} onClick={() => { setStatusFilter('all'); setOnlyFlagged((f) => !f) }} />
      </div>

      {/* Assign form */}
      {showForm && (
        <form onSubmit={submit} className="card card-pad" style={{ marginBottom: 18 }}>
          <div className="card-title">Sahə tapşırığı ver</div>
          <div style={{ display: 'grid', gap: 12, gridTemplateColumns: 'repeat(auto-fit, minmax(190px, 1fr))' }}>
            <div>
              <label className="form-label">İşçi</label>
              <select className="inp" value={employeeId} onChange={(e) => setEmployeeId(e.target.value)}>
                <option value="">— seçin —</option>
                {people.map((p) => <option key={p.id} value={p.id}>{p.fullName}</option>)}
              </select>
              {people.length === 0 && <div className="muted" style={{ fontSize: 11, marginTop: 4 }}>Sahə girişi icazəsi olan işçi yoxdur (işçi profilində açın).</div>}
            </div>
            <div>
              <label className="form-label">Yer / obyekt (ad)</label>
              <input className="inp" value={label} onChange={(e) => setLabel(e.target.value)} placeholder="məs. Nərimanov parkı" maxLength={160} />
            </div>
          </div>

          <label style={{ display: 'flex', alignItems: 'center', gap: 8, marginTop: 12, fontSize: 14 }}>
            <input type="checkbox" checked={useTarget} onChange={(e) => setUseTarget(e.target.checked)} />
            Xəritədə dəqiq hədəf təyin et (məsafə yoxlanılsın)
            <button type="button" className="btn btn-sm" style={{ marginLeft: 'auto' }} onClick={() => void useMyLocation()}>
              Cari yerimi götür
            </button>
          </label>

          {useTarget && (
            <div style={{ marginTop: 10 }}>
              <LocationMapPicker latitude={lat} longitude={lng} radiusMeters={radius} onPick={(la, ln) => { setLat(la); setLng(ln) }} />
              <div style={{ display: 'flex', gap: 10, marginTop: 8, alignItems: 'center', fontSize: 13 }}>
                <span className="muted">Radius:</span>
                <input className="inp" type="number" min={30} max={5000} value={radius} onChange={(e) => setRadius(Math.max(30, Number(e.target.value) || 200))} style={{ maxWidth: 120 }} /> m
                <span className="muted" style={{ marginLeft: 'auto' }}>{lat.toFixed(5)}, {lng.toFixed(5)}</span>
              </div>
            </div>
          )}

          <ChecklistEditor
            value={checklist}
            onChange={setChecklist}
            lastUsed={rows.find((r) => r.checklistTotal > 0)?.id}
            onCopyLast={async (visitId) => {
              const { status, data } = await getFieldVisitChecklist(visitId)
              if (status === 200 && data) setChecklist(data.items.map((i) => i.label).slice(0, MAX_CHECKLIST))
            }}
          />

          <div style={{ marginTop: 12 }}>
            <label className="form-label">Qeyd (istəyə bağlı)</label>
            <input className="inp" value={note} onChange={(e) => setNote(e.target.value)} placeholder="məs. açar qonşudadır" maxLength={300} />
            <div className="muted" style={{ fontSize: 11, marginTop: 4 }}>
              Siyahıya girməyən əlavə izah. Tapşırıqdan sonra siyahı dəyişdirilmir — səhv olsa ləğv edib yenidən tapşırın.
            </div>
          </div>

          <button className="btn btn-primary" style={{ marginTop: 14 }} disabled={saving}>
            {saving ? 'Göndərilir…' : 'Tapşır'}
          </button>
        </form>
      )}

      {/* Filter bar */}
      {!loading && rows.length > 0 && (
        <>
          <div className="chip-row">
            {chips.map((c) => (
              <button key={c.key} className={`chip ${statusFilter === c.key ? 'active' : ''}`}
                onClick={() => setStatusFilter(c.key)}>{c.label}</button>
            ))}
          </div>
          <div style={{ display: 'flex', gap: 10, marginBottom: 14, flexWrap: 'wrap', alignItems: 'center' }}>
            <input className="inp" value={search} onChange={(e) => setSearch(e.target.value)} placeholder="İşçi adı ilə axtar…" style={{ maxWidth: 220 }} />
            <button className={`chip ${onlyFlagged ? 'active' : ''}`} onClick={() => setOnlyFlagged((f) => !f)}>⚠️ Yalnız diqqət</button>
            <button className={`chip ${onlySelf ? 'active' : ''}`} onClick={() => setOnlySelf((f) => !f)}>🙋 Özü qeyd etdi</button>
            {/* "Show me what I can send the client" — the sales story of the whole feature. */}
            <button className={`chip ${onlyPhoto ? 'active' : ''}`} onClick={() => setOnlyPhoto((f) => !f)}>📷 İş şəkli</button>
          </div>
        </>
      )}

      {loading ? (
        <div className="card card-pad muted" style={{ textAlign: 'center', padding: 28 }}>Yüklənir…</div>
      ) : rows.length === 0 ? (
        <div className="card card-pad" style={{ textAlign: 'center', padding: '40px 20px' }}>
          <div style={{ fontSize: 34, marginBottom: 6 }}>📍</div>
          <div style={{ fontWeight: 700, color: 'var(--c900)' }}>Bu gün üçün sahə ziyarəti yoxdur</div>
          <div className="muted" style={{ fontSize: 13, marginTop: 4 }}>«+ Yeni tapşırıq» ilə bir işçini əraziyə tapşırın.</div>
        </div>
      ) : filtered.length === 0 ? (
        <div className="card card-pad muted" style={{ textAlign: 'center', padding: 28 }}>Bu filtrə uyğun ziyarət yoxdur.</div>
      ) : (
        <div className="tbl-wrap tbl-cards">
          <table>
            <thead>
              <tr>
                <th>İşçi</th><th>Status</th><th>Hədəf</th><th>Giriş → Çıxış</th><th>Tapşıran</th><th />
              </tr>
            </thead>
            <tbody>
              {filtered.map((v) => {
                const st = STATUS[v.status] ?? { label: v.status, bg: 'rgba(0,0,0,0.05)', fg: 'var(--c400)' }
                const far = isFarFromTarget(v)
                const stuck = isStuck(v, date)
                return (
                  <tr key={v.id}>
                    <td data-label="İşçi">
                      <EmployeeLink id={v.employeeId} name={v.employeeName} />
                      <div style={{ display: 'flex', gap: 8, alignItems: 'center', marginTop: 2, flexWrap: 'wrap' }}>
                        {v.phone && <a href={`tel:${v.phone}`} className="muted" style={{ fontSize: 11 }}>📞 {v.phone}</a>}
                        {v.selfReported && <span className="tag">🙋 özü qeyd etdi</span>}
                      </div>
                    </td>
                    <td data-label="Status">
                      <span className="badge" style={{ background: st.bg, color: st.fg }}>{st.label}</span>
                      {stuck && <div className="tag" style={{ marginTop: 4, background: 'var(--clay-bg)', color: 'var(--clay)' }}>çıxış edilməyib</div>}
                    </td>
                    <td data-label="Hədəf" style={{ maxWidth: 180 }}>
                      {v.targetLabel || <span className="muted">—</span>}
                      {(v.targetLatitude != null || v.checkInLatitude != null) && (
                        <button className="btn-link" onClick={() => void openDetail(v)}>🗺️ Xəritə</button>
                      )}
                      {v.checklistTotal > 0 && (
                        <div>
                          <span
                            className="tag"
                            style={
                              v.checklistDone === v.checklistTotal
                                ? { background: 'var(--leaf-bg)', color: 'var(--leaf-d)' }
                                : v.status === 'Completed'
                                  ? { background: 'var(--amber-bg)', color: 'var(--amber)' }
                                  : undefined
                            }
                          >
                            ☑ {v.checklistDone}/{v.checklistTotal}
                          </span>
                        </div>
                      )}
                    </td>
                    <td data-label="Giriş → Çıxış" style={{ whiteSpace: 'nowrap' }}>
                      {v.checkInAtUtc ? (
                        <>
                          <b>{fmtTime(v.checkInAtUtc)}</b>
                          <span className="muted"> → </span>
                          {v.checkOutAtUtc ? <b>{fmtTime(v.checkOutAtUtc)}</b> : <span className="muted">{v.status === 'CheckedIn' ? 'davam edir' : '—'}</span>}
                          <div style={{ fontSize: 11, marginTop: 2, display: 'flex', gap: 8, alignItems: 'center' }}>
                            {v.checkInDistanceMeters != null && (
                              <span style={{ color: far ? 'var(--clay)' : 'var(--leaf-d)' }}>
                                {far ? `⚠️ ${fmtDist(v.checkInDistanceMeters)} uzaq` : '✅ ərazidə'}
                              </span>
                            )}
                            {v.durationMinutes != null && <span className="muted" style={{ fontVariantNumeric: 'tabular-nums' }}>{v.durationMinutes} dəq</span>}
                            {/* Present = a work photo exists. The negative is deliberately silent —
                                printing "foto yoxdur" on every row makes the absence the loudest
                                thing on the board, and a column of thumbnails would be the browsing
                                surface this product just removed. */}
                            {v.hasWorkPhoto && <span title="İş şəkli var">📷</span>}
                          </div>
                        </>
                      ) : <span className="muted">—</span>}
                    </td>
                    <td data-label="Tapşıran">{v.assignedByName ?? <span className="muted">—</span>}</td>
                    <td data-label="">
                      <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap', justifyContent: 'flex-end' }}>
                        <button className="btn btn-sm" onClick={() => void openDetail(v)}>Təfərrüat</button>
                        {v.status === 'Assigned' && <button className="btn btn-sm" onClick={() => void cancel(v)}>Ləğv et</button>}
                        {stuck && <button className="btn btn-sm btn-danger" onClick={() => void forceCheckout(v)}>Çıxışı bağla</button>}
                      </div>
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>
      )}

      {detail && (
        <DetailOverlay
          v={detail}
          checklist={detailChecklist}
          workPhoto={detailWorkPhoto}
          onOpenPhoto={setLightbox}
          onClose={() => setDetail(null)}
          onCancel={() => void cancel(detail)}
          onForceCheckout={() => void forceCheckout(detail)}
          stuck={isStuck(detail, date)}
        />
      )}

      {lightbox && (
        <div
          onClick={() => setLightbox(null)}
          style={{ position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.85)', display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 200, padding: 16 }}
        >
          <img src={lightbox} alt="İş şəkli" style={{ maxWidth: '100%', maxHeight: '90vh', borderRadius: 10 }} />
        </div>
      )}
    </div>
  )
}

/**
 * The lines the worker will tick. Never required — zero items is the default and «Tapşır» stays
 * enabled: the moment an empty list blocks an assign, managers stop using the form.
 */
function ChecklistEditor({ value, onChange, lastUsed, onCopyLast }: {
  value: string[]
  onChange: (v: string[]) => void
  lastUsed?: string
  onCopyLast: (visitId: string) => void | Promise<void>
}) {
  const [draft, setDraft] = useState('')
  const full = value.length >= MAX_CHECKLIST

  function add(text: string) {
    const t = text.trim()
    if (!t || full) return
    if (value.some((x) => x.toLowerCase() === t.toLowerCase())) { setDraft(''); return }
    onChange([...value, t.slice(0, 120)])
    setDraft('')
  }

  return (
    <div style={{ marginTop: 12 }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
        <label className="form-label" style={{ margin: 0 }}>Görüləcək işlər (istəyə bağlı)</label>
        <span className="muted" style={{ fontSize: 11, marginLeft: 'auto', fontVariantNumeric: 'tabular-nums' }}>
          {value.length} / {MAX_CHECKLIST}
        </span>
      </div>

      <div className="chip-row" style={{ marginTop: 6, marginBottom: 8 }}>
        {QUICK_TASKS.map((q) => (
          <button key={q} type="button" className="chip" disabled={full} onClick={() => add(q)}>+ {q}</button>
        ))}
        {lastUsed && (
          <button type="button" className="chip" onClick={() => void onCopyLast(lastUsed)}>Son siyahını köçür</button>
        )}
      </div>

      <div style={{ display: 'flex', gap: 8 }}>
        <input
          className="inp"
          value={draft}
          disabled={full}
          maxLength={120}
          placeholder={full ? 'Ən çoxu 10 iş' : 'məs. zibili boşalt'}
          onChange={(e) => setDraft(e.target.value)}
          // Enter adds without submitting the outer assign form — the same habit the self-report
          // sheet uses, so five items are five keystrokes-plus-Enter.
          onKeyDown={(e) => { if (e.key === 'Enter') { e.preventDefault(); add(draft) } }}
        />
        <button type="button" className="btn btn-sm" disabled={full || !draft.trim()} onClick={() => add(draft)}>
          Əlavə et
        </button>
      </div>

      {value.length > 0 && (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 4, marginTop: 8 }}>
          {value.map((item, i) => (
            <div key={`${item}-${i}`} style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 13 }}>
              <span className="tag">{i + 1}</span>
              <span style={{ flex: 1 }}>{item}</span>
              <button
                type="button"
                className="btn btn-sm"
                aria-label="Sil"
                onClick={() => onChange(value.filter((_, n) => n !== i))}
              >
                ×
              </button>
            </div>
          ))}
        </div>
      )}
    </div>
  )
}

function StatCard({ variant, label, value, sub, active, onClick }: {
  variant: string; label: string; value: number; sub: string; active: boolean; onClick: () => void
}) {
  return (
    <button
      className={`stat-card ${variant}`}
      onClick={onClick}
      style={{ textAlign: 'left', cursor: 'pointer', outline: active ? '2px solid var(--c900)' : 'none', outlineOffset: -1 }}
    >
      <div className="stat-lbl">{label}</div>
      <div className="stat-val">{value}</div>
      <div className="stat-sub">{sub}</div>
    </button>
  )
}

function DetailOverlay({ v, checklist, workPhoto, onOpenPhoto, onClose, onCancel, onForceCheckout, stuck }: {
  v: BoardFieldVisit
  checklist: ChecklistItem[] | null
  workPhoto: { url: string | null } | null
  onOpenPhoto: (url: string) => void
  onClose: () => void
  onCancel: () => void
  onForceCheckout: () => void
  stuck: boolean
}) {
  const st = STATUS[v.status] ?? { label: v.status, bg: 'rgba(0,0,0,0.05)', fg: 'var(--c400)' }
  const far = isFarFromTarget(v)
  return (
    <div onClick={onClose} style={{ position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.6)', display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 100, padding: 20 }}>
      <div className="card card-pad" onClick={(e) => e.stopPropagation()} style={{ width: 'min(560px,94vw)', maxHeight: '92vh', overflowY: 'auto' }}>
        <div style={{ display: 'flex', alignItems: 'flex-start', gap: 10, marginBottom: 12 }}>
          <div style={{ minWidth: 0, flex: 1 }}>
            <div style={{ fontWeight: 800, fontSize: 16, color: 'var(--c900)' }}>{v.employeeName}</div>
            <div className="muted" style={{ fontSize: 13 }}>{v.targetLabel || 'Sahə ziyarəti'}</div>
            {v.phone && <a href={`tel:${v.phone}`} className="muted" style={{ fontSize: 12 }}>📞 {v.phone}</a>}
          </div>
          <span className="badge" style={{ background: st.bg, color: st.fg }}>{st.label}</span>
        </div>

        {/* «İş sübutu» sits ABOVE the map on purpose: the customer's question is "was the job done?",
            and presence (map, distance, timeline) is one screen further down. */}
        <WorkEvidence v={v} checklist={checklist} workPhoto={workPhoto} onOpenPhoto={onOpenPhoto} />

        {(v.targetLatitude != null || v.checkInLatitude != null) && (
          <>
            <VisitMap v={v} />
            <div style={{ display: 'flex', gap: 14, margin: '8px 0 12px', fontSize: 12, flexWrap: 'wrap' }}>
              {v.targetLatitude != null && <span><span style={{ color: '#1E70C8' }}>●</span> Hədəf</span>}
              {v.checkInLatitude != null && <span><span style={{ color: '#16a34a' }}>●</span> Giriş</span>}
              {v.checkOutLatitude != null && <span><span style={{ color: '#d97706' }}>●</span> Çıxış</span>}
            </div>
          </>
        )}

        {v.checkInDistanceMeters != null ? (
          <div className={`fb ${far ? 'fb-warn' : 'fb-ok'}`} style={{ marginBottom: 12 }}>
            {far
              ? `⚠️ Giriş hədəfdən ${fmtDist(v.checkInDistanceMeters)} uzaqda`
              : `✅ Giriş hədəf ərazisində (${fmtDist(v.checkInDistanceMeters)})`}
          </div>
        ) : v.checkInAtUtc ? (
          <div className="fb fb-info" style={{ marginBottom: 12 }}>Hədəf təyin edilməyib — məsafə yoxlanmayıb.</div>
        ) : null}

        {/* Timeline */}
        <div style={{ display: 'flex', flexDirection: 'column', gap: 8, fontSize: 13, marginBottom: 12 }}>
          <TimelineRow label="Tapşırılıb" value={v.selfReported ? 'özü qeyd etdi' : (v.assignedByName ?? '—')} />
          <TimelineRow label="Giriş" value={v.checkInAtUtc ? fmtTime(v.checkInAtUtc) : '—'} />
          <TimelineRow label="Çıxış" value={v.checkOutAtUtc ? fmtTime(v.checkOutAtUtc) : (v.status === 'CheckedIn' ? 'davam edir' : '—')} />
          <TimelineRow label="Müddət" value={v.durationMinutes != null ? `${v.durationMinutes} dəq` : '—'} />
        </div>

        {v.note && <div className="muted" style={{ fontSize: 13, marginBottom: 12, fontStyle: 'italic' }}>Qeyd: {v.note}</div>}

        {/* The selfie render path is GONE with its endpoint — after this there is no code on the
            admin field surface that can display a field selfie, only the work photo above. */}

        <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
          {v.status === 'Assigned' && <button className="btn btn-sm" onClick={onCancel}>Ləğv et</button>}
          {stuck && <button className="btn btn-sm btn-danger" onClick={onForceCheckout}>Çıxışı bağla</button>}
          <button className="btn btn-sm" style={{ marginLeft: 'auto' }} onClick={onClose}>Bağla</button>
        </div>
      </div>
    </div>
  )
}

/**
 * «İş sübutu» — what was ticked, and the photo of the result. The per-item tick TIMES are the point:
 * "09:40 / 10:15 / 11:02" and "all four ticked at 17:59 on the way out" are different stories, and
 * that difference is why a manager opens this panel at all.
 */
function WorkEvidence({ v, checklist, workPhoto, onOpenPhoto }: {
  v: BoardFieldVisit
  checklist: ChecklistItem[] | null
  workPhoto: { url: string | null } | null
  onOpenPhoto: (url: string) => void
}) {
  if (v.checklistTotal === 0 && !v.hasWorkPhoto && v.status !== 'Completed') return null
  const allDone = v.checklistTotal > 0 && v.checklistDone === v.checklistTotal
  const missed = v.checklistTotal - v.checklistDone

  return (
    <div className="card card-pad" style={{ marginBottom: 12, background: 'var(--c50)' }}>
      <div className="card-title" style={{ marginBottom: 10 }}>İş sübutu</div>

      {v.checklistTotal > 0 && (
        <>
          <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 6 }}>
            <span style={{ fontSize: 13, fontWeight: 700 }}>Yoxlama siyahısı</span>
            <span className="muted" style={{ fontSize: 12, marginLeft: 'auto', fontVariantNumeric: 'tabular-nums' }}>
              {v.checklistDone}/{v.checklistTotal} tamamlandı
            </span>
          </div>
          {checklist === null ? (
            <div className="muted" style={{ fontSize: 12 }}>Yüklənir…</div>
          ) : (
            <div style={{ display: 'flex', flexDirection: 'column', gap: 6, marginBottom: 10 }}>
              {checklist.map((i) => (
                <div key={i.id} style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 13 }}>
                  <span style={{ color: i.isDone ? 'var(--leaf-d)' : 'var(--c400)' }}>{i.isDone ? '☑' : '☐'}</span>
                  {/* No strikethrough: struck text reads as "done" and would invert the meaning. */}
                  <span style={{ color: i.isDone ? 'var(--c900)' : 'var(--c500)' }}>{i.label}</span>
                  {i.doneAtUtc && (
                    <span className="muted" style={{ marginLeft: 'auto', fontSize: 11 }}>{fmtTime(i.doneAtUtc)}</span>
                  )}
                </div>
              ))}
            </div>
          )}
          {/* Advisory grammar, never fb-err: on an open visit an incomplete list is simply normal. */}
          {v.status === 'Completed' && (
            <div className={`fb ${allDone ? 'fb-ok' : 'fb-warn'}`} style={{ marginBottom: 10 }}>
              {allDone ? '✅ Bütün işlər işarələnib' : `⚠️ ${v.checklistTotal} işdən ${missed}-si işarələnməyib`}
            </div>
          )}
        </>
      )}

      <div style={{ fontSize: 13, fontWeight: 700 }}>İş şəkli</div>
      <div className="muted" style={{ fontSize: 11, marginBottom: 6 }}>İşin nəticəsi — üz şəkli deyil</div>
      {v.hasWorkPhoto ? (
        workPhoto === null ? (
          <div className="muted" style={{ fontSize: 12 }}>Şəkil yüklənir…</div>
        ) : workPhoto.url ? (
          <img
            src={workPhoto.url}
            alt="İş şəkli"
            onClick={() => onOpenPhoto(workPhoto.url!)}
            style={{ maxWidth: 200, borderRadius: 10, cursor: 'zoom-in', display: 'block' }}
          />
        ) : (
          <div className="muted" style={{ fontSize: 12 }}>Şəkil yüklənmədi.</div>
        )
      ) : (
        // A failed camera must not read as an accusation, so this is muted, never a warning.
        <div className="muted" style={{ fontSize: 12 }}>
          İş şəkli yoxdur — işçi şəkil çəkə bilməyib (giriş qeydi keçərlidir)
        </div>
      )}
    </div>
  )
}

function TimelineRow({ label, value }: { label: string; value: string }) {
  return (
    <div style={{ display: 'flex', justifyContent: 'space-between', gap: 12 }}>
      <span className="muted">{label}</span>
      <span style={{ fontWeight: 600, color: 'var(--c900)' }}>{value}</span>
    </div>
  )
}
