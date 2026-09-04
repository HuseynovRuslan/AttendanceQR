import { Fragment, useEffect, useState } from 'react'
import { MapContainer, TileLayer, CircleMarker, Circle, Polyline, Tooltip } from 'react-leaflet'
import { getPersonDay, type PersonDay } from '../../api/hq'
import { HqDrawer } from './HqDrawer'
import { timeOf } from './format'

const TILES = 'https://{s}.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}{r}.png'
const ATTR = '&copy; OpenStreetMap &copy; CARTO'

function fmtDist(m: number): string {
  return m >= 1000 ? `${(m / 1000).toFixed(1)} km` : `${Math.round(m)} m`
}

/** Sətrin gününü tarixlə: dünənki gecə növbəsi bu səhərin sətri ilə qarışmasın. */
function dayLabel(date: string, today: string): string {
  return date === today ? 'bu gün' : 'dünən'
}

/**
 * One person from the live feed, opened.
 *
 * The thing this panel exists to show is a MAP, and only for field visits — but that is the case that
 * needed it. The feed's place column for a «səyyar» arrival is TargetLabel, free text the worker
 * types, and on production it has already fragmented: «Obyekdeyem» / «Obyektdəyəm» / «Obyekt deyem» /
 * «Obyektdeyem» (which is not a place at all — it is the sentence "I am at the site"), and
 * «Nərimanov ofisi» / «Nerimanov» for one office. Of ~70 visits only 4 carry a target coordinate, so
 * for nearly every one there is nothing to check the arrival against either.
 *
 * What cannot be typed is the check-in's own GPS. So the row opens onto where the person actually
 * stood, with what they wrote shown beside it as a claim rather than as the answer. When there IS an
 * assigned target, the line between the two is drawn and measured.
 */
export function PersonDrawer({ employeeId, accent, onClose }: {
  employeeId: string
  accent: string
  onClose: () => void
}) {
  const [data, setData] = useState<PersonDay | null>(null)
  const [failed, setFailed] = useState(false)

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

  // Every point worth putting on the map: each field arrival, and the branch it belongs to.
  const pins = data.visits.filter((v) => v.lat != null && v.lng != null)
  const hasMap = pins.length > 0
  const centre: [number, number] = hasMap
    ? [pins[0].lat as number, pins[0].lng as number]
    : [data.branchLat ?? 40.4093, data.branchLng ?? 49.8671]

  return (
    <HqDrawer
      title={data.fullName}
      accent={accent}
      onClose={onClose}
      subtitle={<>
        {data.company}{data.branch ? ` · ${data.branch}` : ''}{data.position ? ` · ${data.position}` : ''}
      </>}
    >
      {hasMap && (
        <section className="hq-drawer-sec">
          <div className="hq-drawer-sec-title">Harada olub</div>
          <div className="hq-person-map">
            <MapContainer center={centre} zoom={14} scrollWheelZoom={false} style={{ height: '100%', width: '100%' }}>
              <TileLayer url={TILES} attribution={ATTR} />
              {data.branchLat != null && data.branchLng != null && (
                <Circle
                  center={[data.branchLat, data.branchLng]}
                  radius={data.branchRadius ?? 150}
                  pathOptions={{ color: '#64748B', fillOpacity: 0.05, opacity: 0.5, weight: 1, dashArray: '4 5' }}
                />
              )}
              {pins.map((v) => (
                <Fragment key={v.id}>
                  {/* Where the arrival was assigned to be, when it was assigned at all — with the
                      line between claim and fact drawn to scale. */}
                  {v.targetLat != null && v.targetLng != null && (
                    <>
                      <CircleMarker
                        center={[v.targetLat, v.targetLng]}
                        radius={6}
                        pathOptions={{ color: '#F59E0B', fillColor: '#F59E0B', fillOpacity: 0.5, weight: 1 }}
                      >
                        <Tooltip>Hədəf</Tooltip>
                      </CircleMarker>
                      <Polyline
                        positions={[[v.targetLat, v.targetLng], [v.lat as number, v.lng as number]]}
                        pathOptions={{ color: '#F59E0B', weight: 1, opacity: 0.5, dashArray: '3 4' }}
                      />
                    </>
                  )}
                  <CircleMarker
                    center={[v.lat as number, v.lng as number]}
                    radius={8}
                    pathOptions={{ color: accent, fillColor: accent, fillOpacity: 0.85, weight: 2 }}
                  >
                    <Tooltip>{timeOf(v.checkInAtUtc!)} · {v.label || 'etiketsiz'}</Tooltip>
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
                    {v.checkOutAtUtc ? ` → ${timeOf(v.checkOutAtUtc)}` : ' → açıqdır'}
                  </div>
                  <div className="hq-drawer-feed-loc">
                    {dayLabel(v.date, data.today)}
                    {/* What the worker typed, marked as such. It is the least reliable thing here. */}
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
                  <span className="hq-ns-tag is-nophone" title="Hədəf təyin olunmayıb — müqayisə ediləcək nöqtə yoxdur">
                    hədəfsiz
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
          <p className="hq-drawer-empty">Poster skanı yoxdur.</p>
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
