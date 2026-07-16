import { useEffect, useState } from 'react'
import {
  approveMissedCheckout,
  getMissedCheckoutPending,
  rejectMissedCheckout,
  type MissedCheckoutPending,
} from '../../api/admin'
import { IconCheck, IconX } from '../../components/icons'
import { fmtDayMonth, fmtTime } from '../../lib/format'

export function MissedCheckoutPage() {
  const [rows, setRows] = useState<MissedCheckoutPending[]>([])
  const [loading, setLoading] = useState(true)
  const [busyId, setBusyId] = useState<string | null>(null)

  useEffect(() => {
    void load()
  }, [])

  async function load() {
    setLoading(true)
    const r = await getMissedCheckoutPending()
    if (r.status === 200 && Array.isArray(r.data)) setRows(r.data)
    setLoading(false)
  }

  async function act(id: string, kind: 'approve' | 'reject') {
    setBusyId(id)
    const r = kind === 'approve' ? await approveMissedCheckout(id) : await rejectMissedCheckout(id)
    setBusyId(null)
    if (r.status === 200) setRows((rs) => rs.filter((x) => x.id !== id))
  }

  return (
    <div>
      <div className="muted" style={{ fontSize: 13, marginBottom: 14 }}>
        İşçilər çıxışı unudanda getdikləri saatı bildirir — burada təsdiqləyin. «Bu ay» sütunu tez-tez
        unudanları göstərir.
      </div>

      {loading ? (
        <div className="muted">Yüklənir…</div>
      ) : rows.length === 0 ? (
        <div className="card card-pad muted">Gözləyən tələb yoxdur ✓</div>
      ) : (
        <div className="tbl-wrap">
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
                  <td>{fmtDayMonth(r.attendanceDate)}</td>
                  <td className="mono" style={{ fontWeight: 700 }}>{fmtTime(r.requestedCheckOutAtUtc)}</td>
                  <td style={{ maxWidth: 220 }}>{r.reason}</td>
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
                  <td style={{ textAlign: 'right', whiteSpace: 'nowrap' }}>
                    <button
                      className="btn btn-sm"
                      disabled={busyId === r.id}
                      onClick={() => act(r.id, 'approve')}
                      style={{ background: '#16a34a', color: '#fff', marginRight: 6 }}
                    >
                      <IconCheck /> Təsdiqlə
                    </button>
                    <button
                      className="btn btn-sm"
                      disabled={busyId === r.id}
                      onClick={() => act(r.id, 'reject')}
                      style={{ color: '#b91c1c' }}
                    >
                      <IconX /> Rədd
                    </button>
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
