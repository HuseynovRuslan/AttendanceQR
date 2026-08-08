import { useEffect, useState, type FormEvent } from 'react'
import { Circle, CircleMarker, MapContainer, TileLayer, Tooltip, useMap } from 'react-leaflet'
import 'leaflet/dist/leaflet.css'
import { LocationMapPicker } from '../../components/LocationMapPicker'
import {
  getFieldVisitBoard,
  getAssignablePeople,
  assignFieldVisit,
  cancelFieldVisit,
  getFieldVisitPhotos,
  type BoardFieldVisit,
  type AssignablePerson,
} from '../../api/fieldVisits'
import { getPosition } from '../../lib/geo'
import { fmtTime } from '../../lib/format'

const STATUS: Record<string, { label: string; bg: string; fg: string }> = {
  Assigned: { label: 'Tapşırılıb', bg: 'rgba(30,112,200,0.12)', fg: 'var(--blue, #1E70C8)' },
  CheckedIn: { label: 'Ərazidə', bg: 'rgba(200,140,0,0.14)', fg: '#B8860B' },
  Completed: { label: 'Tamamlandı', bg: 'var(--leaf-bg)', fg: 'var(--leaf-d)' },
  Cancelled: { label: 'Ləğv', bg: 'rgba(0,0,0,0.05)', fg: 'var(--c400)' },
}

const BAKU: [number, number] = [40.4093, 49.8671]

function fmtDist(m: number): string {
  return m >= 1000 ? `${(m / 1000).toFixed(1)} km` : `${Math.round(m)} m`
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
  const centre: [number, number] = marks.length ? marks[0].at : [40.4093, 49.8671]
  return (
    <div style={{ height: 340, width: '100%', borderRadius: 10, overflow: 'hidden', border: '1px solid var(--c100)' }}>
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

export function FieldVisitsAdminPage() {
  const [date, setDate] = useState(() => new Date().toISOString().slice(0, 10))
  const [rows, setRows] = useState<BoardFieldVisit[]>([])
  const [people, setPeople] = useState<AssignablePerson[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  // assign form
  const [showForm, setShowForm] = useState(false)
  const [employeeId, setEmployeeId] = useState('')
  const [label, setLabel] = useState('')
  const [useTarget, setUseTarget] = useState(false)
  const [lat, setLat] = useState(BAKU[0])
  const [lng, setLng] = useState(BAKU[1])
  const [radius, setRadius] = useState(200)
  const [note, setNote] = useState('')
  const [saving, setSaving] = useState(false)

  const [photo, setPhoto] = useState<{ checkInUrl: string | null; checkOutUrl: string | null } | null>(null)
  const [mapVisit, setMapVisit] = useState<BoardFieldVisit | null>(null)

  async function load() {
    setLoading(true)
    const res = await getFieldVisitBoard(date)
    setLoading(false)
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

  async function useMyLocation() {
    const geo = await getPosition()
    if (geo.ok) {
      setLat(geo.coords.latitude)
      setLng(geo.coords.longitude)
      setUseTarget(true)
    } else setError('GPS alınmadı')
  }

  async function submit(e: FormEvent) {
    e.preventDefault()
    if (!employeeId) {
      setError('İşçi seçin')
      return
    }
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
    })
    setSaving(false)
    if (status === 200) {
      setLabel('')
      setNote('')
      setEmployeeId('')
      setUseTarget(false)
      setShowForm(false)
      await load()
    } else setError('Tapşırıq yaradıla bilmədi')
  }

  async function cancel(v: BoardFieldVisit) {
    if (!window.confirm(`«${v.employeeName}» tapşırığı ləğv edilsin?`)) return
    const { status } = await cancelFieldVisit(v.id)
    if (status === 200) await load()
    else setError('Ləğv edilmədi')
  }

  async function openPhotos(v: BoardFieldVisit) {
    const { status, data } = await getFieldVisitPhotos(v.id)
    if (status === 200 && data) setPhoto(data)
  }

  function shiftDay(delta: number) {
    const d = new Date(date + 'T00:00:00')
    d.setDate(d.getDate() + delta)
    setDate(d.toISOString().slice(0, 10))
  }

  return (
    <div>
      {/* Date bar */}
      <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 16, flexWrap: 'wrap' }}>
        <button className="btn btn-sm" onClick={() => shiftDay(-1)}>←</button>
        <input className="inp" type="date" value={date} onChange={(e) => setDate(e.target.value)} style={{ maxWidth: 170 }} />
        <button className="btn btn-sm" onClick={() => shiftDay(1)}>→</button>
        <div style={{ flex: 1 }} />
        <button className="btn btn-primary" onClick={() => setShowForm((s) => !s)}>
          {showForm ? 'Bağla' : '+ Yeni tapşırıq'}
        </button>
      </div>

      {error && <div className="fb fb-err" style={{ marginBottom: 14 }}>{error}</div>}

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

          <div style={{ marginTop: 12 }}>
            <label className="form-label">Qeyd (istəyə bağlı)</label>
            <input className="inp" value={note} onChange={(e) => setNote(e.target.value)} placeholder="məs. materialları çatdır" maxLength={300} />
          </div>

          <button className="btn btn-primary" style={{ marginTop: 14 }} disabled={saving}>
            {saving ? 'Göndərilir…' : 'Tapşır'}
          </button>
        </form>
      )}

      {loading ? (
        <div className="card card-pad muted" style={{ textAlign: 'center', padding: 28 }}>Yüklənir…</div>
      ) : rows.length === 0 ? (
        <div className="card card-pad" style={{ textAlign: 'center', padding: '40px 20px' }}>
          <div style={{ fontSize: 34, marginBottom: 6 }}>📍</div>
          <div style={{ fontWeight: 700, color: 'var(--c900)' }}>Bu gün üçün sahə ziyarəti yoxdur</div>
          <div className="muted" style={{ fontSize: 13, marginTop: 4 }}>«+ Yeni tapşırıq» ilə bir işçini əraziyə tapşırın.</div>
        </div>
      ) : (
        <div className="tbl-wrap tbl-cards">
          <table>
            <thead>
              <tr>
                <th>İşçi</th><th>Status</th><th>Hədəf</th><th>Giriş</th><th>Çıxış</th><th className="num">Müddət</th><th>Foto</th><th>Tapşıran</th><th />
              </tr>
            </thead>
            <tbody>
              {rows.map((v) => {
                const st = STATUS[v.status] ?? { label: v.status, bg: 'rgba(0,0,0,0.05)', fg: 'var(--c400)' }
                const inRadius = v.checkInDistanceMeters != null && v.checkInDistanceMeters <= (v.targetRadiusMeters ?? 200)
                return (
                  <tr key={v.id}>
                    <td data-label="İşçi">
                      <div style={{ fontWeight: 700, color: 'var(--c900)' }}>{v.employeeName}</div>
                      {v.selfReported && <span className="muted" style={{ fontSize: 11 }}>özü qeyd etdi</span>}
                    </td>
                    <td data-label="Status"><span className="tag" style={{ background: st.bg, color: st.fg }}>{st.label}</span></td>
                    <td data-label="Hədəf" style={{ maxWidth: 180 }}>
                      {v.targetLabel || <span className="muted">—</span>}
                      {(v.targetLatitude != null || v.checkInLatitude != null || v.checkOutLatitude != null) && (
                        <div>
                          <button
                            onClick={() => setMapVisit(v)}
                            style={{ border: 'none', background: 'none', color: 'var(--blue, #1E70C8)', cursor: 'pointer', padding: 0, font: 'inherit', textDecoration: 'underline' }}
                          >
                            🗺️ Xəritədə bax
                          </button>
                        </div>
                      )}
                    </td>
                    <td data-label="Giriş" style={{ whiteSpace: 'nowrap' }}>
                      {v.checkInAtUtc ? (
                        <>
                          <b>{fmtTime(v.checkInAtUtc)}</b>
                          {v.checkInDistanceMeters != null && (
                            <div style={{ fontSize: 11, color: inRadius ? 'var(--leaf-d)' : 'var(--clay)' }}>
                              {inRadius ? '✅ ərazidə' : `⚠️ ${fmtDist(v.checkInDistanceMeters)} uzaq`}
                            </div>
                          )}
                        </>
                      ) : <span className="muted">—</span>}
                    </td>
                    <td data-label="Çıxış" style={{ whiteSpace: 'nowrap' }}>
                      {v.checkOutAtUtc ? <b>{fmtTime(v.checkOutAtUtc)}</b> : <span className="muted">—</span>}
                    </td>
                    <td data-label="Müddət" className="num" style={{ fontVariantNumeric: 'tabular-nums' }}>{v.durationMinutes != null ? `${v.durationMinutes} dəq` : '—'}</td>
                    <td data-label="Foto">
                      {v.hasCheckInPhoto || v.hasCheckOutPhoto
                        ? <button className="btn btn-sm" onClick={() => void openPhotos(v)}>📷 Bax</button>
                        : <span className="muted">—</span>}
                    </td>
                    <td data-label="Tapşıran">{v.assignedByName ?? <span className="muted">—</span>}</td>
                    <td data-label="">
                      {v.status === 'Assigned' && <button className="btn btn-sm" onClick={() => void cancel(v)}>Ləğv et</button>}
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>
      )}

      {mapVisit && (
        <div
          onClick={() => setMapVisit(null)}
          style={{ position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.6)', display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 100, padding: 20 }}
        >
          <div className="card card-pad" onClick={(e) => e.stopPropagation()} style={{ width: 'min(560px,94vw)' }}>
            <div className="card-title" style={{ marginBottom: 10 }}>{mapVisit.employeeName} — {mapVisit.targetLabel || 'Sahə ziyarəti'}</div>
            <VisitMap v={mapVisit} />
            <div style={{ display: 'flex', gap: 14, marginTop: 10, fontSize: 12, flexWrap: 'wrap', alignItems: 'center' }}>
              {mapVisit.targetLatitude != null && <span><span style={{ color: '#1E70C8' }}>●</span> Hədəf</span>}
              {mapVisit.checkInLatitude != null && <span><span style={{ color: '#16a34a' }}>●</span> Giriş</span>}
              {mapVisit.checkOutLatitude != null && <span><span style={{ color: '#d97706' }}>●</span> Çıxış</span>}
              {mapVisit.checkInDistanceMeters != null && <span className="muted">Hədəfə: {fmtDist(mapVisit.checkInDistanceMeters)}</span>}
            </div>
            <button className="btn btn-sm" style={{ marginTop: 12 }} onClick={() => setMapVisit(null)}>Bağla</button>
          </div>
        </div>
      )}

      {photo && (
        <div
          onClick={() => setPhoto(null)}
          style={{ position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.6)', display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 100, padding: 20 }}
        >
          <div className="card card-pad" onClick={(e) => e.stopPropagation()} style={{ maxWidth: 640, display: 'flex', gap: 14, flexWrap: 'wrap', justifyContent: 'center' }}>
            {photo.checkInUrl && (
              <figure style={{ margin: 0, textAlign: 'center' }}>
                <img src={photo.checkInUrl} alt="Giriş" style={{ maxWidth: 260, borderRadius: 10 }} />
                <figcaption className="muted" style={{ fontSize: 12, marginTop: 4 }}>Giriş</figcaption>
              </figure>
            )}
            {photo.checkOutUrl && (
              <figure style={{ margin: 0, textAlign: 'center' }}>
                <img src={photo.checkOutUrl} alt="Çıxış" style={{ maxWidth: 260, borderRadius: 10 }} />
                <figcaption className="muted" style={{ fontSize: 12, marginTop: 4 }}>Çıxış</figcaption>
              </figure>
            )}
            <button className="btn btn-sm" style={{ flexBasis: '100%' }} onClick={() => setPhoto(null)}>Bağla</button>
          </div>
        </div>
      )}
    </div>
  )
}
