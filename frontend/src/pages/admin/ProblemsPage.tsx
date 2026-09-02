import { useEffect, useState } from 'react'
import { EmployeeLink } from '../../components/EmployeeLink'
import { getStuckDevices, getProblems, type ProblemRow, type ProblemsReport , type StuckDeviceRow } from '../../api/admin'
import { ProblemsMap, parseRejectPoints } from './ProblemsMap'
import { IconX } from '../../components/icons'
import { fmtTime } from '../../lib/format'

/** Reason → human label + colour. `blocking` = the employee genuinely could NOT check in/out. */
const REASON: Record<string, { label: string; cls: string; blocking?: boolean }> = {
  DeviceMismatch: { label: 'Cihaz uyğun deyil', cls: 'bg-red-100 text-red-700', blocking: true },
  NoDeviceBound: { label: 'Cihaz bağlanmayıb', cls: 'bg-red-100 text-red-700', blocking: true },
  EmployeeNotFoundOrInactive: { label: 'Hesab aktiv deyil', cls: 'bg-red-100 text-red-700', blocking: true },
  LocationNotFound: { label: 'Lokasiya tapılmadı', cls: 'bg-red-100 text-red-700', blocking: true },
  LocationInactive: { label: 'Lokasiya deaktiv', cls: 'bg-red-100 text-red-700', blocking: true },
  OutsideRadius: { label: 'İş yerindən kənarda', cls: 'bg-amber-100 text-amber-700', blocking: true },
  TokenExpired: { label: 'QR köhnəlib', cls: 'bg-amber-100 text-amber-700' },
  TokenReused: { label: 'QR təkrar işlədilib', cls: 'bg-amber-100 text-amber-700' },
  TokenMalformed: { label: 'Yanlış QR', cls: 'bg-amber-100 text-amber-700' },
  SignatureInvalid: { label: 'Yanlış QR (imza)', cls: 'bg-amber-100 text-amber-700' },
  TooSoonToCheckOut: { label: 'Çox tez çıxış cəhdi', cls: 'bg-blue-100 text-blue-700' },
  // An offline scan that never became a record. BLOCKING on purpose: the employee tapped, was shown
  // a green "saved" card, and has nothing for that day — so unless someone enters that day by hand
  // they are marked Qayıb for a day they worked. The reason carries the lost DAY after the '|', which
  // this screen already splits off as the detail — without it the row is unactionable.
  OfflineRejected: { label: 'Oflayn skan qəbul edilmədi', cls: 'bg-red-100 text-red-700', blocking: true },
  OfflineExpired: { label: 'Oflayn skan gec göndərildi', cls: 'bg-red-100 text-red-700', blocking: true },
  // Reported by the phone itself — the scan never reached the server.
  GpsPermissionDenied: { label: 'Məkan icazəsi verilməyib', cls: 'bg-red-100 text-red-700', blocking: true },
  GpsUnavailable: { label: 'Telefonda məkan bağlıdır', cls: 'bg-red-100 text-red-700', blocking: true },
  GpsTimeout: { label: 'GPS siqnal tapmadı', cls: 'bg-amber-100 text-amber-700', blocking: true },
  GpsUnsupported: { label: 'Brauzer məkanı dəstəkləmir', cls: 'bg-red-100 text-red-700', blocking: true },
  // Refused at the moment a phone would have become a shared one. Both have an exact fix an admin can
  // apply, which is why they are not folded into the generic DeviceMismatch.
  SharedDeviceNotAllowed: { label: 'Ortaq cihaz icazəsi yoxdur', cls: 'bg-amber-100 text-amber-700', blocking: true },
  DeviceAccountLimit: { label: 'Cihazdakı hesab həddi dolub', cls: 'bg-amber-100 text-amber-700', blocking: true },
  // Measured, warned about, but never blocked — so it isn't a "could not scan" reason.
  GpsInaccurate: { label: 'GPS dəqiq deyil', cls: 'bg-amber-100 text-amber-700' },
  // Reported by the phone for a non-GPS failure — the scan never became a record.
  //
  // Why the camera failed, not just that it did. All of these arrived as CameraBlocked until the
  // labels were split, and the difference decides what the admin does: a denial is the employee's own
  // settings screen, "in use" is another app on the same phone, and "not found" means that phone will
  // not work at all — somebody has to record them from a colleague's handset today. CameraBlocked
  // itself is kept for rows written by an older copy of the app.
  CameraDenied: { label: 'Kamera icazəsi verilməyib', cls: 'bg-red-100 text-red-700', blocking: true },
  CameraInUse: { label: 'Kamera başqa proqramda açıqdır', cls: 'bg-amber-100 text-amber-700', blocking: true },
  CameraNotFound: { label: 'Cihazda kamera tapılmadı', cls: 'bg-red-100 text-red-700', blocking: true },
  CameraInsecure: { label: 'Təhlükəsiz bağlantı yoxdur', cls: 'bg-red-100 text-red-700', blocking: true },
  // Not the camera at all: the scanner itself could not be fetched, which is a connection problem
  // wearing a camera error's clothes.
  ScannerLoadFailed: { label: 'Skan proqramı yüklənmədi', cls: 'bg-amber-100 text-amber-700', blocking: true },
  CameraFailed: { label: 'Kamera açılmadı (səbəb bilinmir)', cls: 'bg-red-100 text-red-700', blocking: true },
  CameraBlocked: { label: 'Kamera açılmadı (köhnə versiya)', cls: 'bg-red-100 text-red-700', blocking: true },
  NetworkError: { label: 'İnternet çatmadı', cls: 'bg-red-100 text-red-700', blocking: true },
  ScanError: { label: 'Skan xətası', cls: 'bg-amber-100 text-amber-700', blocking: true },
  AlreadyCompleted: { label: 'Gün artıq tamamlanıb', cls: 'bg-slate-100 text-slate-600' },
  DuplicateCheckIn: { label: 'Təkrar giriş', cls: 'bg-slate-100 text-slate-600' },
}

const ACTION_AZ: Record<string, string> = {
  Scan: 'Skan',
  CheckOut: 'Çıxış',
  Device: 'Telefonda',
}

function meta(reason: string) {
  return REASON[reason] ?? { label: reason, cls: 'bg-slate-100 text-slate-600' }
}

function isoDay(d: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
}
const todayLocal = () => isoDay(new Date())
const daysAgo = (n: number) => {
  const d = new Date()
  d.setDate(d.getDate() - n)
  return isoDay(d)
}
const fmtDay = (iso: string) => iso.split('-').reverse().join('.')

/** A run of the same blocking problem for one person — the actionable unit. Someone rejected six
 *  times for "outside the radius" at one site is one thing to fix, not six lines to scroll past. */
type Recurring = {
  employeeId: string | null
  employeeName: string
  locationName: string
  reason: string
  count: number
  lastAtUtc: string
  days: Set<string> // distinct local days it happened on
}

export function ProblemsPage() {
  // Default: the last 7 days, so a problem from earlier in the week is visible instead of aging out.
  const [from, setFrom] = useState(daysAgo(6))
  const [to, setTo] = useState(todayLocal())
  const [report, setReport] = useState<ProblemsReport | null>(null)
  const [stuck, setStuck] = useState<StuckDeviceRow[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    void load(from, to)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [from, to])

  // The stuck-device register is date-independent ("stuck NOW"), so it loads once, not per range.
  useEffect(() => {
    void getStuckDevices().then((r) => {
      if (r.status === 200 && Array.isArray(r.data)) setStuck(r.data)
    })
  }, [])

  async function load(f: string, t: string) {
    setLoading(true)
    setError(null)
    const { status, data } = await getProblems(f, t)
    setLoading(false)
    if (status === 200 && data && 'rows' in data) setReport(data)
    else if (status === 403) setError('İcazəniz yoxdur')
    else setError('Məlumat yüklənmədi')
  }

  const rows = report?.rows ?? []

  // Recurring blocking problems, grouped by (employee + reason). This is the part that turns a wall
  // of rejections into "these people, at these sites, keep failing for this reason" — which is how a
  // whole location's geofence problem finally becomes one obvious line.
  const recurringMap = new Map<string, Recurring>()
  for (const r of rows) {
    if (!meta(r.reason).blocking) continue
    const key = `${r.employeeId ?? r.employeeName}|${r.reason}`
    const day = r.atUtc.slice(0, 10)
    const cur = recurringMap.get(key)
    if (cur) {
      cur.count++
      cur.days.add(day)
      if (r.atUtc > cur.lastAtUtc) cur.lastAtUtc = r.atUtc
    } else {
      recurringMap.set(key, {
        employeeId: r.employeeId,
        employeeName: r.employeeName,
        locationName: r.locationName,
        reason: r.reason,
        count: 1,
        lastAtUtc: r.atUtc,
        days: new Set([day]),
      })
    }
  }
  const recurring = Array.from(recurringMap.values()).sort((a, b) => b.count - a.count)

  // Sites with a cluster of blocking problems — surfaces "Azpetrol Baza" as the common thread.
  const byLocation = new Map<string, number>()
  for (const g of recurring) byLocation.set(g.locationName, (byLocation.get(g.locationName) ?? 0) + g.count)
  const topLocations = Array.from(byLocation, ([name, count]) => ({ name, count }))
    .filter((l) => l.count >= 3)
    .sort((a, b) => b.count - a.count)

  function setRange(days: number) {
    setFrom(daysAgo(days - 1))
    setTo(todayLocal())
  }

  return (
    <div>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-end', gap: 12, marginBottom: 14, flexWrap: 'wrap' }}>
        <div>
          <h1 style={{ fontSize: 18, fontWeight: 800, color: 'var(--c900)' }}>Problemlər</h1>
          <div className="muted" style={{ fontSize: 13 }}>Rədd edilmiş skanlar — kim skan edə bilmədi və niyə.</div>
        </div>
        <div style={{ display: 'flex', gap: 8, alignItems: 'flex-end', flexWrap: 'wrap' }}>
          <div>
            <label className="form-label">Başlanğıc</label>
            <input className="inp" type="date" value={from} max={to} onChange={(e) => setFrom(e.target.value)} />
          </div>
          <div>
            <label className="form-label">Son</label>
            <input className="inp" type="date" value={to} max={todayLocal()} onChange={(e) => setTo(e.target.value)} />
          </div>
        </div>
      </div>

      {/* Cihazı ilişənlər — telefonu skan etməkdən imtina edən və o vaxtdan bir dəfə də uğurlu skanı
          olmayanlar. Hər sətir bir səfərlik təmirdir: nömrə + platforma + səbəb. Siyahı boşdursa
          kart ümumiyyətlə görünmür — boş xəbərdarlıq oxunmayan xəbərdarlıqdır. */}
      {stuck.length > 0 && (
        <div className="card" style={{ marginBottom: 16, padding: 16, borderLeft: '3px solid var(--amber)' }}>
          <div style={{ fontWeight: 800, color: 'var(--c900)', marginBottom: 4 }}>
            📵 Cihazı ilişənlər — {stuck.length} nəfər
          </div>
          <div className="muted" style={{ fontSize: 12.5, marginBottom: 10 }}>
            Telefonu skan etməyə imkan vermir (GPS/kamera) və son xətadan bəri bir dəfə də uğurlu skanı yoxdur.
            Düzəlişi telefonun parametrlərindədir — nəzarətçiyə nömrəni və təlimatı göndərin.
          </div>
          <div className="tbl-wrap tbl-cards tbl-dense">
            <table>
              <thead><tr><th>İşçi</th><th>Filial</th><th>Nömrə</th><th>Telefon</th><th>Səbəb</th><th>Cəhd</th><th>Son uğurlu</th></tr></thead>
              <tbody>
                {stuck.map((r) => (
                  <tr key={r.employeeId}>
                    <td data-label="İşçi" style={{ fontWeight: 700, color: 'var(--c900)' }}>{r.fullName}</td>
                    <td data-label="Filial">{r.locationName}</td>
                    <td data-label="Nömrə" className="mono">{r.phoneNumber ?? '—'}</td>
                    <td data-label="Telefon">{r.platform === 'ios' ? ' iPhone' : r.platform === 'android' ? '🤖 Android' : '—'}</td>
                    <td data-label="Səbəb" style={{ fontSize: 12 }}>
                      {r.reasons.split(', ').map((c) => meta(c).label).join(' · ')}
                    </td>
                    <td data-label="Cəhd" className="num mono">{r.failureCount30d}</td>
                    <td data-label="Son uğurlu" className="mono" style={{ fontSize: 12 }}>
                      {r.lastSuccessfulScanAtUtc
                        ? new Date(r.lastSuccessfulScanAtUtc).toLocaleDateString('az-AZ', { day: '2-digit', month: '2-digit' })
                        : 'heç vaxt'}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <details style={{ marginTop: 10 }}>
            <summary className="muted" style={{ fontSize: 12.5, cursor: 'pointer' }}>Nəzarətçi üçün təlimat (kopyalayıb göndərin)</summary>
            <pre style={{ fontSize: 12, background: 'var(--c50)', borderRadius: 8, padding: 10, whiteSpace: 'pre-wrap', marginTop: 6 }}>{
`Android (Chrome): sayt açıq ikən ünvan sətrindəki kilid/ikon → İcazələr → «Sıfırla»,
SONRA Parametrlər → Tətbiqlər → Chrome → İcazələr → Məkan → açıq olduğunu yoxlayın (hər iki qapı lazımdır).
iPhone (Safari): səhifə açıq ikən «aA» düyməsi → Sayt Ayarları → Məkan → «Soruş» və ya «İcazə ver»
(bu, Ayarlar tətbiqindən qat-qat qısadır). Ayarlar yolu da işləyir: Məxfilik → Yer Xidmətləri → Safari.
Vacib: icazə soruşulanda «Bir dəfə» YOX — «İcazə ver / İstifadə zamanı» seçin.
«Tətbiqi silib yenidən qur» MƏSLƏHƏT DEYİL — Android-də icazə Chrome ilə ortaqdır, heç nə dəyişmir.
Sonda: tətbiqi tam bağlayıb açın, bir skan edin — alınırsa düzəldi.`
            }</pre>
          </details>
        </div>
      )}

      <div className="chip-row" style={{ marginBottom: 14 }}>
        <span className={`chip${from === todayLocal() ? ' active' : ''}`} onClick={() => setRange(1)}>Bu gün</span>
        <span className={`chip${from === daysAgo(6) && to === todayLocal() ? ' active' : ''}`} onClick={() => setRange(7)}>Son 7 gün</span>
        <span className={`chip${from === daysAgo(29) && to === todayLocal() ? ' active' : ''}`} onClick={() => setRange(30)}>Son 30 gün</span>
      </div>

      {error && (
        <div className="fb fb-err" style={{ marginBottom: 12 }}>
          <IconX />
          <span>{error}</span>
        </div>
      )}

      <div className="stat-grid" style={{ marginBottom: 14 }}>
        <div className="stat-card clay">
          <div className="stat-lbl">Problemli skan</div>
          <div className="stat-val">{report?.rejectedCount ?? '—'}</div>
          <div className="stat-sub">{fmtDay(from)} – {fmtDay(to)}</div>
        </div>
        <div className="stat-card leaf">
          <div className="stat-lbl">Uğurlu skan</div>
          <div className="stat-val">{report?.successCount ?? '—'}</div>
        </div>
      </div>

      {/* Shown whenever any site had an OutsideRadius rejection — the geofence circle is drawn even
          before any point has coordinates, so the map is visible (with a note) rather than absent.
          Points fill in as new rejections are captured. */}
      {report && (report.geofences.length > 0 || parseRejectPoints(report.rows).length > 0) && (
        <ProblemsMap rows={report.rows} geofences={report.geofences} />
      )}

      {/* Sites with a cluster — the "this whole location has a problem" signal. */}
      {topLocations.length > 0 && (
        <div className="card card-pad" style={{ marginBottom: 14, borderColor: '#fed7aa', background: '#fff7ed' }}>
          <div style={{ fontWeight: 700, color: '#9a3412', marginBottom: 6 }}>📍 Bu ərazilərdə çoxlu problem var</div>
          <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
            {topLocations.map((l) => (
              <span key={l.name} className="inline-flex items-center gap-1 rounded-full px-3 py-1 text-xs font-semibold bg-amber-100 text-amber-800">
                {l.name} · {l.count} problem
              </span>
            ))}
          </div>
          <div className="muted" style={{ fontSize: 12, marginTop: 8 }}>
            Bir ərazidə təkrarlanan «İş yerindən kənarda» adətən geofence radiusunun və ya poster
            yerinin problemidir — işçinin deyil.
          </div>
        </div>
      )}

      {/* Recurring blocking problems, most-frequent first — the actionable list. */}
      {recurring.length > 0 && (
        <div className="card card-pad" style={{ marginBottom: 14, borderColor: '#fecaca', background: '#fef2f2' }}>
          <div style={{ fontWeight: 700, color: '#991b1b', marginBottom: 10 }}>
            ⚠️ Təkrarlanan problemlər ({recurring.length})
          </div>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
            {recurring.map((g) => (
              <div key={`${g.employeeId ?? g.employeeName}-${g.reason}`}
                   style={{ display: 'flex', alignItems: 'baseline', gap: 8, flexWrap: 'wrap', fontSize: 13 }}>
                <b style={{ color: 'var(--c900)' }}><EmployeeLink id={g.employeeId} name={g.employeeName} /></b>
                <span className="muted">· {g.locationName} ·</span>
                <span className={`inline-flex items-center rounded-full px-2 py-0.5 text-xs font-semibold ${meta(g.reason).cls}`}>
                  {meta(g.reason).label}
                </span>
                <span style={{ fontWeight: 700, color: '#991b1b' }}>{g.count} dəfə</span>
                <span className="muted" style={{ fontSize: 12 }}>
                  ({g.days.size} gün · son {fmtTime(g.lastAtUtc)})
                </span>
              </div>
            ))}
          </div>
        </div>
      )}

      {report && report.summary.length > 0 && (
        <div className="chip-row">
          {report.summary.map((s) => (
            <span key={s.reason} className={`inline-flex items-center gap-1 rounded-full px-3 py-1 text-xs font-semibold ${meta(s.reason).cls}`}>
              {meta(s.reason).label} · {s.count}
            </span>
          ))}
        </div>
      )}

      <div className="tbl-wrap tbl-cards">
        <table>
          <thead>
            <tr>
              <th>Vaxt</th>
              <th>İşçi</th>
              <th>Ərazi</th>
              <th>Əməliyyat</th>
              <th>Səbəb</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((r: ProblemRow, i) => (
              <tr key={`${r.atUtc}-${i}`}>
                <td data-label="Vaxt" className="mono">{fmtDay(r.atUtc.slice(0, 10))} {fmtTime(r.atUtc)}</td>
                <td data-label="İşçi" style={{ fontWeight: 700, color: 'var(--c900)' }}><EmployeeLink id={r.employeeId} name={r.employeeName} /></td>
                <td data-label="Ərazi">{r.locationName}</td>
                <td data-label="Əməliyyat">{ACTION_AZ[r.action] ?? r.action}</td>
                <td>
                  <span className={`inline-flex items-center rounded-full px-2 py-0.5 text-xs font-semibold ${meta(r.reason).cls}`}>
                    {meta(r.reason).label}
                  </span>
                  {r.detail && (
                    <span className="muted" style={{ marginLeft: 6, fontSize: 12 }}>
                      {r.reason === 'GpsInaccurate' ? `±${r.detail} m` : r.detail}
                    </span>
                  )}
                </td>
              </tr>
            ))}
            {!loading && report && rows.length === 0 && !error && (
              <tr>
                <td colSpan={5} className="muted" style={{ textAlign: 'center', padding: 28 }}>
                  Bu aralıqda heç bir problem olmayıb 🎉
                </td>
              </tr>
            )}
            {loading && (
              <tr>
                <td colSpan={5} className="muted" style={{ textAlign: 'center', padding: 28 }}>
                  Yüklənir…
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  )
}
