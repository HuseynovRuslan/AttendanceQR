import { Fragment, useEffect, useState } from 'react'
import { MapContainer, TileLayer, CircleMarker, Circle, Polyline, Tooltip, useMap } from 'react-leaflet'
import { basemap } from '../../lib/basemap'
import { getPersonDay, type PersonDay } from '../../api/hq'
import { HqDrawer } from './HqDrawer'
import { timeOf } from './format'

// Through the shared helper, NOT a hand-written CARTO URL. CARTO began requiring a key in August 2026
// and answers unauthenticated requests with a 200 and a tile reading "API KEY REQUIRED" — no error,
// no log line, just a watermark across the customer's map. Writing the URL here bypassed the key AND
// the OpenStreetMap fallback that exists for exactly this.
const BASE = basemap('dark')

function fmtDist(m: number): string {
  return m >= 1000 ? `${(m / 1000).toFixed(1)} km` : `${Math.round(m)} m`
}

/** Great-circle metres. Same formula as the server's, so the two cannot disagree about a distance. */
function metresBetween(aLat: number, aLng: number, bLat: number, bLng: number): number {
  const R = 6371000
  const rad = (d: number) => (d * Math.PI) / 180
  const dLat = rad(bLat - aLat)
  const dLng = rad(bLng - aLng)
  const h = Math.sin(dLat / 2) ** 2 + Math.cos(rad(aLat)) * Math.cos(rad(bLat)) * Math.sin(dLng / 2) ** 2
  return 2 * R * Math.asin(Math.min(1, Math.sqrt(h)))
}

function elapsed(iso: string, now: number): string {
  const mins = Math.max(0, Math.floor((now - new Date(iso).getTime()) / 60000))
  if (mins < 60) return `${mins} dəq`
  return `${Math.floor(mins / 60)} saat ${mins % 60} dəq`
}

/** Sətrin gününü tarixlə: dünənki gecə növbəsi bu səhərin sətri ilə qarışmasın. */
function dayLabel(date: string, today: string): string {
  return date === today ? 'bu gün' : 'dünən'
}

/**
 * Frames the branch AND the person together.
 *
 * Zooming to the person alone answered "where is this dot" and left out what a director is actually
 * asking — how far from her own park is she? A cleaner who belongs at Dədə Qorqud and is standing at
 * Fəvvarələr Meydanı looks identical to one who never left, unless both are on the screen at once.
 */
function FitBoth({ pts }: { pts: [number, number][] }) {
  const map = useMap()
  const key = pts.map((p) => p.join(',')).join('|')
  useEffect(() => {
    if (pts.length === 0) return
    if (pts.length === 1) { map.setView(pts[0], 15); return }
    map.fitBounds(pts, { padding: [40, 40], maxZoom: 15 })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key])
  return null
}

/**
 * One person from the live feed, opened.
 *
 * The thing this panel exists to show is a MAP, and only for field visits — but that is the case that
 * needed it. The feed's place column for a «səyyar» arrival is TargetLabel, free text the worker
 * types, and on production it had fragmented into four spellings of «Obyektdeyem» — the sentence "I
 * am at the site", which is not a place. What cannot be typed is the check-in's own GPS.
 *
 * Above the map sit the two sentences a director needs before looking at anything: whether she is
 * there NOW, and how far that is from the branch she belongs to.
 */
export function PersonDrawer({ employeeId, accent, onClose }: {
  employeeId: string
  accent: string
  onClose: () => void
}) {
  const [data, setData] = useState<PersonDay | null>(null)
  const [failed, setFailed] = useState(false)
  // Ticks on its own minute. A counter that only moves when the panel is reopened reads as a frozen
  // number, which is the one thing an elapsed time must never look like.
  const [now, setNow] = useState(() => Date.now())

  useEffect(() => {
    const t = setInterval(() => setNow(Date.now()), 60_000)
    return () => clearInterval(t)
  }, [])

  useEffect(() => {
    let alive = true
    void (async () => {
      const { status, data: d } = await getPersonDay(employeeId)
      if (!alive) return
      if (status === 200 && d && 'fullName' in d) setData(d)
      else setFailed(true)
    })()
    return () => { alive = false }
  }, [employeeId])

  if (failed) {
    return (
      <HqDrawer title="Açılmadı" accent={accent} onClose={onClose}>
        <p className="hq-drawer-empty">Bu işçinin məlumatı gəlmədi — yenidən cəhd edin.</p>
      </HqDrawer>
    )
  }
  if (!data) {
    return (
      <HqDrawer title="Yüklənir…" accent={accent} onClose={onClose}>
        <p className="hq-drawer-empty">Yüklənir…</p>
      </HqDrawer>
    )
  }

  const pins = data.visits.filter((v) => v.lat != null && v.lng != null)
  const hasBranch = data.branchLat != null && data.branchLng != null
  const hasMap = pins.length > 0 || hasBranch

  // The visits arrive newest first, so this is where they are now — or, once everything is closed,
  // the last place they were.
  const latest = data.visits.find((v) => !v.checkOutAtUtc) ?? data.visits[0] ?? null
  const isThereNow = Boolean(latest && latest.checkInAtUtc && !latest.checkOutAtUtc)
  const here = pins[0]

  const awayFromBranch = hasBranch && here?.lat != null && here?.lng != null
    ? metresBetween(data.branchLat!, data.branchLng!, here.lat, here.lng)
    : null

  const frame: [number, number][] = []
  if (hasBranch) frame.push([data.branchLat!, data.branchLng!])
  for (const p of pins) {
    frame.push([p.lat!, p.lng!])
    if (p.targetLat != null && p.targetLng != null) frame.push([p.targetLat, p.targetLng])
  }

  const centre: [number, number] = here?.lat != null
    ? [here.lat, here.lng!]
    : hasBranch ? [data.branchLat!, data.branchLng!] : [40.4093, 49.8671]

  // A link the reader opens themselves, not a background call: satellite and street view are the two
  // things our dark tiles cannot give, and "is that really a building" is a fair question to have.
  const googleUrl = here?.lat != null ? `https://www.google.com/maps?q=${here.lat},${here.lng}` : null

  return (
    <HqDrawer
      title={data.fullName}
      accent={accent}
      onClose={onClose}
      subtitle={<>
        {data.company}{data.branch ? ` · ${data.branch}` : ''}{data.position ? ` · ${data.position}` : ''}
      </>}
    >
      {/* The two-second read, before any map: is she there now, and how far from her own branch. */}
      {latest && (
        <div className="hq-person-hero">
          <div className="hq-person-hero-top">
            <span className={`hq-person-pill ${isThereNow ? 'is-live' : 'is-done'}`}>
              <i className="hq-pulse-dot" />
              {isThereNow ? 'HAZIRDA ƏRAZİDƏDİR' : 'GÜNÜN SON QEYDİ'}
            </span>
            {latest.checkInAtUtc && (
              <span className="hq-person-elapsed">
                {timeOf(latest.checkInAtUtc)}-dən
                {isThereNow ? ` · ${elapsed(latest.checkInAtUtc, now)}` : ''}
              </span>
            )}
          </div>
          <div className="hq-hero-loc-title">📍 {latest.label || 'Səyyar ərazi'}</div>
          {data.branch && (
            <div className="hq-hero-loc-sub">
              🏢 Təhkim filialı: <b>{data.branch}</b>
              {/* The number that changes the reading. A cleaner two kilometres from her own park is a
                  different fact from one standing in it, and the panel stated neither. */}
              {awayFromBranch != null && <> · {fmtDist(awayFromBranch)} aralıda</>}
            </div>
          )}
        </div>
      )}

      {hasMap && (
        <section className="hq-drawer-sec">
          <div className="hq-sec-head">
            <div className="hq-drawer-sec-title" style={{ margin: 0 }}>Harada olub</div>
            {googleUrl && (
              <a href={googleUrl} target="_blank" rel="noreferrer" className="hq-map-ext-link"
                 title="Peyk və küçə görünüşü üçün">
                Google xəritədə ↗
              </a>
            )}
          </div>
          <div className="hq-person-map">
            <MapContainer center={centre} zoom={14} scrollWheelZoom={false} style={{ height: '100%', width: '100%' }}>
              <TileLayer
                url={BASE.url}
                attribution={BASE.attribution}
                subdomains={BASE.subdomains}
                maxZoom={BASE.maxZoom}
                className={BASE.needsDarkFilter ? 'tiles-dark' : undefined}
              />
              <FitBoth pts={frame} />

              {hasBranch && (
                <>
                  <Circle
                    center={[data.branchLat!, data.branchLng!]}
                    radius={data.branchRadius ?? 150}
                    pathOptions={{ color: '#64748B', fillOpacity: 0.07, opacity: 0.45, weight: 1, dashArray: '4 5' }}
                  />
                  <CircleMarker
                    center={[data.branchLat!, data.branchLng!]}
                    radius={7}
                    pathOptions={{ color: '#94A3B8', fillColor: '#475569', fillOpacity: 0.9, weight: 2 }}
                  >
                    <Tooltip permanent direction="bottom" offset={[0, 8]} className="hq-tip">
                      🏢 {data.branch || 'Filial'}
                    </Tooltip>
                  </CircleMarker>
                </>
              )}

              {/* Branch to person, drawn to scale — the distance stated above, made visible. */}
              {hasBranch && here?.lat != null && (
                <Polyline
                  positions={[[data.branchLat!, data.branchLng!], [here.lat, here.lng!]]}
                  pathOptions={{ color: '#38BDF8', weight: 1.8, opacity: 0.5, dashArray: '4 6' }}
                />
              )}

              {pins.map((v, i) => (
                <Fragment key={v.id}>
                  {v.targetLat != null && v.targetLng != null && (
                    <>
                      <CircleMarker
                        center={[v.targetLat, v.targetLng]}
                        radius={6}
                        pathOptions={{ color: '#F59E0B', fillColor: '#F59E0B', fillOpacity: 0.5, weight: 1 }}
                      >
                        <Tooltip>🎯 Hədəf</Tooltip>
                      </CircleMarker>
                      <Polyline
                        positions={[[v.targetLat, v.targetLng], [v.lat!, v.lng!]]}
                        pathOptions={{ color: '#F59E0B', weight: 1, opacity: 0.5, dashArray: '3 4' }}
                      />
                    </>
                  )}
                  <CircleMarker
                    center={[v.lat!, v.lng!]}
                    radius={i === 0 ? 9 : 7}
                    pathOptions={{ color: '#fff', fillColor: accent, fillOpacity: 0.95, weight: i === 0 ? 2.5 : 1.5 }}
                  >
                    {/* Permanent on the CURRENT point only. On somebody with four visits, four
                        always-open labels overlap into a pile that hides the map beneath them. */}
                    <Tooltip permanent={i === 0} direction="top" offset={[0, -10]} className="hq-tip">
                      📍 {v.label || 'Ərazi'}{v.checkInAtUtc ? ` · ${timeOf(v.checkInAtUtc)}` : ''}
                    </Tooltip>
                  </CircleMarker>
                </Fragment>
              ))}
            </MapContainer>
          </div>
        </section>
      )}

      <section className="hq-drawer-sec">
        <div className="hq-drawer-sec-title">Səyyar ziyarətlər ({data.visits.length})</div>
        {data.visits.length === 0 ? (
          <p className="hq-drawer-empty">Səyyar ziyarət yoxdur.</p>
        ) : (
          <div className="hq-drawer-sites">
            {data.visits.map((v) => (
              <div key={v.id} className="hq-ns-row">
                <div className="hq-ns-main">
                  <div className="hq-drawer-feed-name">
                    {v.checkInAtUtc ? timeOf(v.checkInAtUtc) : '—'}
                    {v.checkOutAtUtc ? ` → ${timeOf(v.checkOutAtUtc)}` : ' → davam edir'}
                    {!v.checkOutAtUtc && v.checkInAtUtc && (
                      <span className="hq-ns-open"> ⏱ {elapsed(v.checkInAtUtc, now)}</span>
                    )}
                  </div>
                  <div className="hq-drawer-feed-loc">
                    {dayLabel(v.date, data.today)}
                    {v.label ? ` · «${v.label}»` : ' · etiket yazılmayıb'}
                    {v.selfReported ? ' · özü qeyd edib' : ' · təyin olunub'}
                    {v.note ? ` · ${v.note}` : ''}
                  </div>
                </div>
                {v.distanceMeters != null ? (
                  <span className={`hq-ns-tag is-${v.distanceMeters <= 300 ? 'opened' : 'nologin'}`}>
                    {v.distanceMeters <= 300 ? '✅' : '⚠️'} {fmtDist(v.distanceMeters)}
                  </span>
                ) : (
                  // It said «hədəfsiz», in the same red as a face mismatch, and a director read it as
                  // an accusation. Nothing is wrong: nobody assigned this visit a target, because the
                  // worker filed it themselves — which is the ordinary case and the whole point of
                  // the feature.
                  <span className="hq-ns-tag is-free" title="Bu ziyarəti işçi özü qeyd edib — təyin olunmuş hədəf yoxdur, ona görə müqayisə ediləcək məsafə də yoxdur">
                    🙋 sərbəst
                  </span>
                )}
              </div>
            ))}
          </div>
        )}
      </section>

      <section className="hq-drawer-sec">
        <div className="hq-drawer-sec-title">Poster skanları ({data.records.length})</div>
        {data.records.length === 0 ? (
          // «(0)» followed by nothing reads as missing data. It is not: this person worked a day with
          // no poster to scan, which is what the whole «səyyar» feature exists for.
          <div className="hq-drawer-info">
            Bu gün posterdən skan olunmayıb — birbaşa sahədən qeydə alınıb.
          </div>
        ) : (
          <div className="hq-drawer-sites">
            {data.records.map((r) => (
              <div key={r.date} className="hq-ns-row">
                <div className="hq-ns-main">
                  <div className="hq-drawer-feed-name">
                    {r.checkInAtUtc ? timeOf(r.checkInAtUtc) : '—'}
                    {r.checkOutAtUtc ? ` → ${timeOf(r.checkOutAtUtc)}` : ' → çıxış yoxdur'}
                  </div>
                  <div className="hq-drawer-feed-loc">
                    {dayLabel(r.date, data.today)}
                    {r.wasOffline ? ' · 📴 oflayn' : ''}
                    {r.manual ? ' · əl ilə yazılıb' : ''}
                  </div>
                </div>
              </div>
            ))}
          </div>
        )}
      </section>
    </HqDrawer>
  )
}
