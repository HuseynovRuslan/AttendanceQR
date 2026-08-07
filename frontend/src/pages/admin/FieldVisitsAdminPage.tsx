import { useEffect, useState, type FormEvent } from 'react'
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
function mapLink(lat: number, lng: number) {
  return `https://www.google.com/maps?q=${lat},${lng}`
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
          <div style={{ display: 'grid', gap: 12, gridTemplateColumns: '1fr 1fr' }}>
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

      <div className="card">
        <table className="tbl">
          <thead>
            <tr>
              <th>İşçi</th><th>Status</th><th>Hədəf</th><th>Giriş</th><th>Çıxış</th><th className="num">Müddət</th><th>Foto</th><th>Tapşıran</th><th />
            </tr>
          </thead>
          <tbody>
            {loading && <tr><td colSpan={9} className="muted" style={{ padding: 18 }}>Yüklənir…</td></tr>}
            {!loading && rows.length === 0 && <tr><td colSpan={9} className="muted" style={{ padding: 18 }}>Bu gün üçün sahə ziyarəti yoxdur</td></tr>}
            {rows.map((v) => {
              const st = STATUS[v.status] ?? { label: v.status, bg: 'rgba(0,0,0,0.05)', fg: 'var(--c400)' }
              const inRadius = v.checkInDistanceMeters != null && v.checkInDistanceMeters <= (v.targetRadiusMeters ?? 200)
              return (
                <tr key={v.id}>
                  <td>
                    <div style={{ fontWeight: 700 }}>{v.employeeName}</div>
                    {v.selfReported && <span className="muted" style={{ fontSize: 11 }}>özü qeyd etdi</span>}
                  </td>
                  <td><span className="tag" style={{ background: st.bg, color: st.fg }}>{st.label}</span></td>
                  <td style={{ fontSize: 13, maxWidth: 160 }}>
                    {v.targetLabel || <span className="muted">—</span>}
                    {v.targetLatitude != null && (
                      <> · <a href={mapLink(v.targetLatitude, v.targetLongitude!)} target="_blank" rel="noreferrer">hədəf</a></>
                    )}
                  </td>
                  <td style={{ fontSize: 13, whiteSpace: 'nowrap' }}>
                    {v.checkInAtUtc ? (
                      <>
                        <b>{fmtTime(v.checkInAtUtc)}</b>{' '}
                        {v.checkInLatitude != null && <a href={mapLink(v.checkInLatitude, v.checkInLongitude!)} target="_blank" rel="noreferrer">xəritə</a>}
                        {v.checkInDistanceMeters != null && (
                          <div style={{ fontSize: 11, color: inRadius ? 'var(--leaf-d)' : 'var(--clay)' }}>
                            {inRadius ? '✅ ərazidə' : `⚠️ ${fmtDist(v.checkInDistanceMeters)} uzaq`}
                          </div>
                        )}
                      </>
                    ) : <span className="muted">—</span>}
                  </td>
                  <td style={{ fontSize: 13, whiteSpace: 'nowrap' }}>
                    {v.checkOutAtUtc ? (
                      <><b>{fmtTime(v.checkOutAtUtc)}</b>{' '}
                        {v.checkOutLatitude != null && <a href={mapLink(v.checkOutLatitude, v.checkOutLongitude!)} target="_blank" rel="noreferrer">xəritə</a>}</>
                    ) : <span className="muted">—</span>}
                  </td>
                  <td className="num" style={{ fontVariantNumeric: 'tabular-nums' }}>{v.durationMinutes != null ? `${v.durationMinutes} dəq` : '—'}</td>
                  <td>
                    {v.hasCheckInPhoto || v.hasCheckOutPhoto
                      ? <button className="btn btn-sm" onClick={() => void openPhotos(v)}>📷</button>
                      : <span className="muted">—</span>}
                  </td>
                  <td style={{ fontSize: 13 }}>{v.assignedByName ?? <span className="muted">—</span>}</td>
                  <td style={{ textAlign: 'right' }}>
                    {v.status === 'Assigned' && <button className="btn btn-sm" onClick={() => void cancel(v)}>Ləğv et</button>}
                  </td>
                </tr>
              )
            })}
          </tbody>
        </table>
      </div>

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
