import { useEffect, useState } from 'react'
import {
  approveMissedCheckout,
  getMissedCheckoutPending,
  rejectMissedCheckout,
  type MissedCheckoutPending,
} from '../../api/admin'
import { correctionError, useCorrectionReason } from '../../components/CorrectionReason'
import { IconCheck, IconX } from '../../components/icons'
import { fmtDayMonth, fmtTime } from '../../lib/format'
import { useHolds } from '../../auth/AccessContext'

export function MissedCheckoutPage() {
  // Deciding a missed check-out writes the day — «attendance.correct».
  const mayCorrect = useHolds('attendance.correct', true)
  const [rows, setRows] = useState<MissedCheckoutPending[]>([])
  const [loading, setLoading] = useState(true)
  const [busyId, setBusyId] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const reason = useCorrectionReason()

  useEffect(() => {
    void load()
  }, [])

  async function load() {
    setLoading(true)
    const r = await getMissedCheckoutPending()
    if (r.status === 200 && Array.isArray(r.data)) setRows(r.data)
    setLoading(false)
  }

  async function act(row: MissedCheckoutPending, kind: 'approve' | 'reject') {
    // Deciding writes (or refuses) a check-out, which is what the day pays — so it says why, like any
    // other correction. The employee's own reason is beside it in the audit line.
    const why = await reason.ask(
      kind === 'approve'
        ? {
            title: `${row.employeeName} — çıxışı ${fmtTime(row.requestedCheckOutAtUtc)} kimi təsdiqlə`,
            detail: `${fmtDayMonth(row.attendanceDate)} · işçinin səbəbi: ${row.reason}`,
            confirmLabel: 'Təsdiqlə',
            presets: ['İşçinin dediyi saat təsdiqləndi', 'Rəhbərin təsdiqi ilə', 'Telefon və ya internet problemi — skan edə bilmədi'],
          }
        : {
            title: `${row.employeeName} — tələbi rədd et`,
            detail: `${fmtDayMonth(row.attendanceDate)} · gün açıq qalır, admin özü bağlayacaq.`,
            confirmLabel: 'Rədd et',
            danger: true,
            presets: ['Göstərilən saat düz deyil', 'Gün artıq bağlanıb', 'Təkrar tələb'],
          },
    )
    if (why === null) return
    setBusyId(row.id)
    setError(null)
    const r = kind === 'approve' ? await approveMissedCheckout(row.id, why) : await rejectMissedCheckout(row.id, why)
    setBusyId(null)
    if (r.status === 200) {
      setRows((rs) => rs.filter((x) => x.id !== row.id))
      return
    }
    setError(correctionError(r.data, r.status === 403 ? 'Bu işçinin tələbinə qərar verə bilməzsiniz.' : 'Alınmadı — yenidən cəhd edin.'))
  }

  return (
    <div>
      <div className="muted" style={{ fontSize: 13, marginBottom: 14 }}>
        İşçilər çıxışı unudanda getdikləri saatı bildirir — burada təsdiqləyin. «Bu ay» sütunu tez-tez
        unudanları göstərir.
      </div>

      {error && <div className="fb fb-err" style={{ marginBottom: 14 }}><IconX /><span>{error}</span></div>}
      {reason.dialog}

      {loading ? (
        <div className="muted">Yüklənir…</div>
      ) : rows.length === 0 ? (
        <div className="card card-pad muted">Gözləyən tələb yoxdur ✓</div>
      ) : (
        <div className="tbl-wrap tbl-cards">
          <table>
            <thead>
              <tr>
                <th>İşçi</th>
                <th>Gün</th>
                <th>Getdiyi saat</th>
                <th>Səbəb</th>
                <th>Bu ay</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {rows.map((r) => (
                <tr key={r.id}>
                  <td style={{ fontWeight: 700, color: 'var(--c900)' }}>
                    {r.employeeName}
                    <div className="muted" style={{ fontSize: 12, fontWeight: 400 }}>{r.locationName}</div>
                  </td>
                  <td data-label="Gün">{fmtDayMonth(r.attendanceDate)}</td>
                  <td data-label="Getdiyi saat" className="mono" style={{ fontWeight: 700 }}>{fmtTime(r.requestedCheckOutAtUtc)}</td>
                  <td data-label="Səbəb" style={{ maxWidth: 220 }}>{r.reason}</td>
                  <td>
                    <span
                      style={{
                        display: 'inline-block',
                        minWidth: 26,
                        textAlign: 'center',
                        padding: '2px 8px',
                        borderRadius: 999,
                        fontWeight: 800,
                        fontSize: 13,
                        color: r.monthlyCount >= 3 ? '#b91c1c' : r.monthlyCount >= 2 ? '#b45309' : '#475569',
                        background: r.monthlyCount >= 3 ? '#fee2e2' : r.monthlyCount >= 2 ? '#fef3c7' : '#f1f5f9',
                      }}
                    >
                      {r.monthlyCount}
                    </span>
                  </td>
                  <td data-label="">
                    {mayCorrect && <>
                    <button
                      className="btn btn-sm"
                      disabled={busyId === r.id}
                      onClick={() => act(r, 'approve')}
                      style={{ background: '#16a34a', color: '#fff', marginRight: 6 }}
                    >
                      <IconCheck /> Təsdiqlə
                    </button>
                    <button
                      className="btn btn-sm"
                      disabled={busyId === r.id}
                      onClick={() => act(r, 'reject')}
                      style={{ color: '#b91c1c' }}
                    >
                      <IconX /> Rədd
                    </button>
                    </>}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}
