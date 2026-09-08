import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { getGeofenceFit, type GeofenceFitReport } from '../../api/admin'

/**
 * «Radius uyğunsuzluğu» — sites whose GPS circle refuses people who are standing at them.
 *
 * A branch is stored as one point and one radius. That fits a shop; it does not fit a park, a bridge
 * or a stretch of road, and when it does not fit, nothing announces it. The scan is refused, the
 * worker taps four more times, gives up, and the day is written as Qayıb — while every one of those
 * refusals sits in the audit log where nobody looks.
 *
 * Measured the day this was built: Qafur Məmmədov Parkı had refused 133 scans in forty days, and the
 * NEAREST of them was from 466 metres against a 150-metre circle. Not one person was cheating; the
 * crew works across a park and the circle was drawn round one corner of it.
 *
 * The screen deliberately separates the two shapes, because they need opposite fixes — and getting
 * that backwards is expensive. A circle a few metres short is widened. A circle in the wrong PLACE is
 * moved: widening that one would licence scanning from home.
 */
export function GeofenceFitPage() {
  const [days, setDays] = useState(40)
  const [report, setReport] = useState<GeofenceFitReport | null>(null)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    setLoading(true)
    void getGeofenceFit(days).then((r) => {
      setLoading(false)
      if (r.status === 200 && r.data && 'rows' in r.data) { setReport(r.data); setError('') }
      else setError('Məlumat alınmadı. Çıxıb yenidən girin.')
    })
  }, [days])

  return (
    <div>
      <div className="chip-row" style={{ marginBottom: 14 }}>
        {[14, 30, 40, 90].map((d) => (
          <span key={d} className={`chip${days === d ? ' active' : ''}`} onClick={() => setDays(d)}>
            son {d} gün
          </span>
        ))}
      </div>

      {error && <div className="muted" style={{ marginBottom: 12 }}>{error}</div>}

      {report && (
        <div className="muted" style={{ fontSize: 12, marginBottom: 14, lineHeight: 1.6 }}>
          GPS divarı açıq olan {report.checked} filial yoxlandı.{' '}
          {report.rows.length === 0
            ? 'Heç birində işçilər sərhəddə ilişmir.'
            : `${report.rows.length} filialda işçilər skan vura bilmir.`}
          {' '}Bu, işçi haqqında iddia deyil — <b>dairənin yerində olub-olmaması sualıdır</b>.
        </div>
      )}

      {loading && <div className="muted">Yüklənir…</div>}

      {report && report.rows.length > 0 && (
        <div className="tbl-wrap tbl-cards tbl-dense">
          <table>
            <thead>
              <tr>
                <th>Filial</th>
                <th>Radius</th>
                <th>Rədd edilən</th>
                <th>Ən yaxın rədd</th>
                <th>Ortanca</th>
                <th>Qəbul olunan ən uzaq</th>
                <th>Nə etməli</th>
              </tr>
            </thead>
            <tbody>
              {report.rows.map((r) => (
                <tr key={r.locationId}>
                  <td data-label="Filial" style={{ fontWeight: 700, color: 'var(--c900)' }}>
                    {r.locationName}
                    <div className="muted" style={{ fontWeight: 500, fontSize: 11 }}>
                      {r.peopleAffected} nəfər
                    </div>
                  </td>
                  <td className="mono" data-label="Radius">{r.radiusMeters} m</td>
                  <td data-label="Rədd edilən">
                    <span className="badge b-absent">{r.rejections}</span>
                  </td>
                  {/* The number that decides which problem this is: if even the closest refusal is far
                      outside, nobody was standing at the site and the circle is in the wrong place. */}
                  <td className="mono" data-label="Ən yaxın">
                    {r.nearestRejectedMeters !== null ? `${r.nearestRejectedMeters} m` : '—'}
                  </td>
                  <td className="mono" data-label="Ortanca">
                    {r.medianRejectedMeters !== null ? `${r.medianRejectedMeters} m` : '—'}
                  </td>
                  <td className="mono" data-label="Ən uzaq qəbul">
                    {r.farthestAcceptedMeters !== null ? `${r.farthestAcceptedMeters} m` : '—'}
                  </td>
                  <td data-label="Nə etməli">
                    {r.verdict === 'Tight' ? (
                      <span className="badge b-late" title="İşçilər sərhədin dibindən rədd olunur — radius bir az dardır.">
                        radiusu genişləndir
                      </span>
                    ) : (
                      <span className="badge b-absent" title="Ən yaxın rədd belə çox uzaqdadır — dairə başqa yerdədir. Genişləndirmək evdən skan etməyə icazə vermək deməkdir.">
                        mərkəzi köçür
                      </span>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {report && report.rows.length === 0 && !loading && (
        <div className="muted" style={{ padding: 28, textAlign: 'center' }}>
          Uyğunsuzluq tapılmadı.
        </div>
      )}

      <div className="muted" style={{ fontSize: 11.5, marginTop: 16, lineHeight: 1.7 }}>
        <b>Necə oxunur.</b> «Ən yaxın rədd» — kiminsə rədd edildiyi ən yaxın məsafə, və hansı problem
        olduğunu məhz o deyir. Radiusun 1.5 mislindən yaxındırsa, adam filialdadır və dairə bir az
        dardır (telefonun GPS dəqiqliyi özü 20–50 m sapır) — <b>radiusu genişləndirin</b>. Ən yaxın
        rədd belə çox uzaqdadırsa, dairə səhv yerdədir: briqada parkın, körpünün və ya yolun başqa
        hissəsində işləyir — <b>mərkəzi ora köçürün</b>. Bu halda radiusu böyütmək düzəliş deyil,
        evdən skan etməyə icazə verməkdir.
        {' '}Filialı <Link to="/admin/locations">Filiallar</Link> ekranından dəyişirsiniz. Xətti iş
        yerlərində (yol, körpü) GPS divarını tamamilə söndürmək də olar — onda giriş qeydə alınır və
        işçinin harada dayandığı xəritədə görünür.
      </div>
    </div>
  )
}
