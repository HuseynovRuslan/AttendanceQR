import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { getShiftMismatch, type ShiftMismatchReport } from '../../api/admin'
import { recomputeDays } from '../../api/attendance'
import { useAuth } from '../../auth/AuthContext'
import { fmtDate } from '../../lib/format'

/**
 * «Növbə uyğunsuzluğu» — people whose arrivals and whose schedule disagree.
 *
 * Built after a night worker was found on a day shift and lost a night's pay to it. Nothing had
 * failed: every screen faithfully applied the hours it was given, and the hours were wrong. Because
 * everything downstream reads the shift, everything downstream was wrong at once — the check-out
 * reminder fired at the wrong hour, the home screen told him he had not checked in, and the night he
 * worked was scored as zero. No error was raised anywhere, because from the system's point of view
 * nothing went wrong.
 *
 * So this screen exists to go looking. It is the one place a wrong schedule becomes visible before it
 * costs somebody a day.
 *
 * It accuses nobody. A mismatch is a question about the SCHEDULE — "is this person's shift right?" —
 * and someone covering another crew for a fortnight will appear here while behaving perfectly. That
 * is why nothing here blocks a scan or touches pay.
 */
const HOURS = (t: string) => t.slice(0, 5)

export function ShiftMismatchPage() {
  const { role } = useAuth()
  const isAdmin = role === 'Admin'
  const [days, setDays] = useState(21)
  const [report, setReport] = useState<ShiftMismatchReport | null>(null)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    setLoading(true)
    void getShiftMismatch(days).then((r) => {
      setLoading(false)
      if (r.status === 200 && r.data && 'rows' in r.data) { setReport(r.data); setError('') }
      else setError('Məlumat alınmadı. Çıxıb yenidən girin.')
    })
  }, [days])

  return (
    <div>
      <div className="chip-row" style={{ marginBottom: 14 }}>
        {[14, 21, 30, 60].map((d) => (
          <span key={d} className={`chip${days === d ? ' active' : ''}`} onClick={() => setDays(d)}>
            son {d} gün
          </span>
        ))}
      </div>

      {error && <div className="muted" style={{ marginBottom: 12 }}>{error}</div>}

      {report && (
        <div className="muted" style={{ fontSize: 12, marginBottom: 14, lineHeight: 1.6 }}>
          {report.checked} işçinin girişləri yoxlandı.{' '}
          {report.rows.length === 0
            ? 'Hamısının növbəsi faktiki iş saatına uyğundur.'
            : `${report.rows.length} nəfərin iş saatı təyin olunmuş növbə ilə uyğun gəlmir.`}
          {report.rows.some((r) => r.splitNightDays > 0) && (
            <>
              {' '}Aralarında <b>gecə işləyib gündüz növbəsində qalanlar</b> var: onların günü səhər
              tezdən gecə yarısına qədər bir «növbə» kimi yazılır, çünki səhər çıxışı yeni gün açır.
              Bu, həm saatları yanlış göstərir, həm də istirahət günlərini «Qayıb» edir.
            </>
          )}
          {' '}Bu, işçi haqqında iddia deyil — <b>növbənin düz olub-olmadığı sualıdır</b>. Başqa briqadanı
          əvəz edən adam da burada görünür və heç bir səhv etmir.
        </div>
      )}

      {loading && <div className="muted">Yüklənir…</div>}

      {report && report.rows.length > 0 && (
        <div className="tbl-wrap tbl-cards tbl-dense">
          <table>
            <thead>
              <tr>
                <th>İşçi</th>
                <th>Filial</th>
                <th>Təyin olunmuş növbə</th>
                <th>Faktiki giriş</th>
                <th>Fərq</th>
                <th>Uyğunsuz</th>
                <th>Əlamət</th>
              </tr>
            </thead>
            <tbody>
              {report.rows.map((r) => (
                <tr key={r.employeeId}>
                  <td data-label="İşçi" style={{ fontWeight: 700, color: 'var(--c900)' }}>
                    <Link to={`/admin/employees/${r.employeeId}`}>{r.fullName}</Link>
                    {r.position && <div className="muted" style={{ fontWeight: 500, fontSize: 11 }}>{r.position}</div>}
                  </td>
                  <td data-label="Filial">{r.locationName}</td>
                  <td className="mono" data-label="Növbə">
                    {r.shiftLabel}
                    {/* Whether the hours came from a named shift or fell back to the branch matters:
                        "no shift at all" is a different fix from "the wrong shift". */}
                    <div className="muted" style={{ fontSize: 11 }}>
                      {r.scheduleName ?? '⚠ növbə təyin olunmayıb — filialın saatı'}
                    </div>
                  </td>
                  <td className="mono" data-label="Faktiki">
                    {HOURS(r.earliestIn)} – {HOURS(r.latestIn)}
                  </td>
                  <td data-label="Fərq">
                    <span className="tag" style={{ background: 'var(--amber-bg)', color: 'var(--amber)', fontWeight: 700 }}>
                      {r.worstGapHours} saat
                    </span>
                  </td>
                  <td className="mono" data-label="Uyğunsuz">{r.offScans}/{r.scans}</td>
                  {/* The shape that arrival times cannot show. Named plainly, because the fix is
                      different: this person needs a NIGHT shift, not a corrected day one. */}
                  <td data-label="Əlamət">
                    {r.splitNightDays > 0 ? (
                      <span
                        className="badge b-absent"
                        title="Bu qədər gün səhər tezdən gecəyə qədər bir gün kimi yazılıb — gecə növbəsi gündüz növbəsində qalanda belə olur."
                      >
                        {r.splitNightDays} gün gecə→gündüz
                      </span>
                    ) : (
                      <span className="muted" style={{ fontSize: 12 }}>giriş saatı</span>
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
        <b>Necə oxunur.</b> «Fərq» — girişin növbə başlanğıcından ən çox uzaqlaşdığı saatdır, saat
        əqrəbi üzrə ən qısa yolla ölçülür (23:00 ilə 01:00 arası iki saatdır, iyirmi iki yox — yoxsa
        bütün gecə növbələri səhvən burada görünərdi). Siyahıya yalnız girişlərinin <b>əksəriyyəti</b>
        {' '}kənarda olanlar düşür: ayda bir dəfə gecə əvəz edən adam burada qalmır.
        {' '}Düzəltmək üçün <Link to="/admin/schedules">Növbələr</Link> ekranından uyğun növbəni təyin edin.
        {' '}<b>«Gecə→gündüz»</b> isə ayrı əlamətdir: giriş saatına baxmaqla görünmür, çünki belə
        işçinin qeyddəki «girişi» əslində onun səhər çıxışıdır. Ona gecə növbəsi (bitmə saatı
        başlama saatından kiçik, məs. 20:00–08:00) təyin edin.
      </div>

      {/* Correcting a shift changes nothing anybody can see until the days are rebuilt: past days
          are READ from the stored summaries, and the nightly job only fills days it has never seen.
          The button lives here because this is the screen where the shift gets corrected. */}
      {isAdmin && <RecomputeCard />}
    </div>
  )
}

function RecomputeCard() {
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const [busy, setBusy] = useState(false)
  const [done, setDone] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const ERRORS: Record<string, string> = {
    NothingToRecompute: 'Bu aralıqda hesablanacaq gün yoxdur (bugünkü gün onsuz da canlıdır).',
    RangeTooLong: 'Aralıq 92 gündən çox ola bilməz.',
  }

  async function run() {
    if (!from || !to) return
    setBusy(true); setDone(null); setError(null)
    const { status, data } = await recomputeDays(from, to)
    setBusy(false)
    if (status === 200 && data && 'days' in data) {
      setDone(`${data.days} gün yenidən hesablandı (${fmtDate(data.from)} – ${fmtDate(data.to)}).`)
      return
    }
    const code = data && typeof data === 'object' && 'error' in data ? (data as { error: string }).error : ''
    setError(ERRORS[code] ?? 'Hesablanmadı.')
  }

  return (
    <div className="card card-pad" style={{ marginTop: 18 }}>
      <div className="card-title">Günləri yenidən hesabla</div>
      <div className="muted" style={{ fontSize: 12.5, marginTop: -10, marginBottom: 14, lineHeight: 1.6 }}>
        Növbəni dəyişdikdən və ya keçmiş qeydi düzəltdikdən sonra <b>keçmiş günlər öz-özünə
        yenilənmir</b> — hesabat və tabel hazır cədvəldən oxuyur. Aralığı seçib bir dəfə işə salın.
        Bugünkü gün toxunulmur (o, onsuz da canlı hesablanır).
      </div>

      {error && <div className="fb fb-err" style={{ marginBottom: 10 }}><span>{error}</span></div>}
      {done && <div className="fb fb-ok" style={{ marginBottom: 10 }}><span>{done}</span></div>}

      <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap', alignItems: 'end' }}>
        <label className="form-label" style={{ margin: 0 }}>
          Başlanğıc
          <input className="inp" type="date" value={from} onChange={(e) => setFrom(e.target.value)}
                 style={{ height: 38, padding: '0 12px', fontSize: 13, marginTop: 4 }} />
        </label>
        <label className="form-label" style={{ margin: 0 }}>
          Son
          <input className="inp" type="date" value={to} onChange={(e) => setTo(e.target.value)}
                 style={{ height: 38, padding: '0 12px', fontSize: 13, marginTop: 4 }} />
        </label>
        <button className="btn btn-primary" disabled={busy || !from || !to} onClick={() => void run()}
                style={{ height: 38, padding: '0 16px', fontSize: 13 }}>
          {busy ? 'Hesablanır…' : 'Yenidən hesabla'}
        </button>
      </div>
    </div>
  )
}
