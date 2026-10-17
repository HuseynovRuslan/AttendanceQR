import { useEffect, useState } from 'react'
import { useAccess } from '../../auth/AccessContext'
import { useAuth } from '../../auth/AuthContext'
import { EmployeeLink } from '../../components/EmployeeLink'
import { correctionError, useCorrectionReason } from '../../components/CorrectionReason'
import { IconCheck, IconX } from '../../components/icons'
import {
  decideSelfCorrection,
  getSelfCorrections,
  setSelfCorrectionApproval,
  type SelfCorrectionItem,
  type SelfCorrectionList,
} from '../../api/selfCorrections'
import { fmtDate, fmtLongDateOfInstant, fmtTime } from '../../lib/format'
import { listFailureText } from '../../lib/listFailure'

const KIND: Record<string, string> = {
  Update: 'Vaxt düzəlişi',
  Create: 'Günü əl ilə yazıb',
  ClearCheckout: 'Çıxışı silib',
  CloseOpen: 'Açıq günü bağlayıb',
  MissedCheckout: 'Unudulmuş çıxışını təsdiqləyib',
}

const times = (inUtc: string | null, outUtc: string | null) => `${fmtTime(inUtc)}–${fmtTime(outUtc)}`

/**
 * «Öz düzəlişləri» — every correction somebody made to their OWN day.
 *
 * Nobody corrects the day they are paid for, except where the company has nobody else who could — the
 * single-admin rule of 2026-10-16. Those corrections go ahead in the open, and this is where «in the open» is:
 * who, which day, the times before and after, the reason, and — where the company asks for a second
 * approval — the waiting ones, with the buttons for whoever may answer them. Nothing here can be edited or
 * deleted: the record of what somebody did to their own pay is not theirs to tidy.
 */
export function SelfCorrectionsPage() {
  const { role } = useAuth()
  const { access } = useAccess()
  const { ask, dialog } = useCorrectionReason()
  const [data, setData] = useState<SelfCorrectionList | null>(null)
  const [failed, setFailed] = useState<number | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState<string | null>(null)
  // The setting is the company's: today's admin, or the Company administrator once the company is Enforced.
  const mayChangeSetting = access?.mode === 'Enforced' ? !!access.companyAdmin : role === 'Admin'

  async function load() {
    const r = await getSelfCorrections()
    if (r.status === 200 && r.data && 'items' in r.data) {
      setData(r.data)
      setFailed(null)
    } else {
      setFailed(r.status)
    }
  }

  useEffect(() => { void load() }, [])

  async function decide(item: SelfCorrectionItem, approve: boolean) {
    const reason = await ask({
      title: approve ? `${item.employeeName}: düzəliş təsdiqlənsin?` : `${item.employeeName}: düzəliş rədd edilsin?`,
      detail: approve
        ? `${fmtDate(item.date)} · ${times(item.checkInBeforeUtc, item.checkOutBeforeUtc)} → ${times(item.checkInAfterUtc, item.checkOutAfterUtc)} tətbiq olunacaq.`
        : 'Gün olduğu kimi qalır.',
      confirmLabel: approve ? 'Təsdiqlə' : 'Rədd et',
      danger: !approve,
      presets: approve ? ['Doğrudur, yoxlamışam'] : ['Sübut yoxdur', 'Vaxt səhvdir'],
    })
    if (!reason) return
    setBusy(item.id)
    setError(null)
    const r = await decideSelfCorrection(item.id, approve, reason)
    setBusy(null)
    if (r.status === 200) await load()
    else setError(correctionError(r.data, 'Alınmadı — yenidən cəhd edin.'))
  }

  async function toggleApproval(on: boolean) {
    setError(null)
    const r = await setSelfCorrectionApproval(on)
    if (r.status === 200) await load()
    else setError(correctionError(r.data, 'Ayar dəyişmədi.'))
  }

  return (
    <div>
      {dialog}
      <div className="att-head">
        <div>
          <h1 className="att-title">Öz düzəlişləri</h1>
          <div className="att-sub">
            Öz gününə edilən düzəlişlər. Yalnız şirkətdə həmin günü düzəldə biləcək başqa şəxs olmayanda mümkündür,
            səbəb mütləqdir, əvvəlki vaxtlar burada həmişəlik qalır.
          </div>
        </div>
      </div>

      {data && mayChangeSetting && (
        <div className="card card-pad" style={{ marginBottom: 14, maxWidth: 720 }}>
          <div className="card-title">İkinci təsdiq</div>
          <p className="muted" style={{ fontSize: 13, lineHeight: 1.6, margin: '6px 0 10px' }}>
            Açıq olanda öz düzəlişi dərhal tətbiq olunmur — «Öz düzəlişini təsdiqləmək» hüququ olan başqa şəxs
            səbəblə təsdiqləyənə qədər gözləyir. Belə şəxs yoxdursa, ayar açılmır.
          </p>
          <label style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 13 }}>
            <input
              type="checkbox"
              checked={data.needsApproval}
              disabled={!data.needsApproval && !data.approverAvailable}
              onChange={(e) => void toggleApproval(e.target.checked)}
            />
            Öz düzəlişi ikinci təsdiq istəsin
          </label>
          {!data.needsApproval && !data.approverAvailable && (
            <div className="muted" style={{ fontSize: 12, marginTop: 6 }}>
              Hələ təsdiq verə biləcək şəxs yoxdur — «Səlahiyyətlər»də birinə «Öz düzəlişini təsdiqləmək» hüququnu verin.
            </div>
          )}
        </div>
      )}

      {error && <div className="fb fb-err" style={{ marginBottom: 12 }}><IconX /><span>{error}</span></div>}

      <div className="card" style={{ overflowX: 'auto' }}>
        <table className="tbl">
          <thead>
            <tr>
              <th>Gün</th>
              <th>İşçi</th>
              <th>Nə edilib</th>
              <th>Əvvəl → sonra</th>
              <th>Səbəb</th>
              <th>Nə vaxt</th>
              <th>Vəziyyət</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {failed !== null && (
              <tr><td colSpan={8} className="muted" style={{ textAlign: 'center', padding: 24 }}>{listFailureText(failed)}</td></tr>
            )}
            {failed === null && data && data.items.length === 0 && (
              <tr><td colSpan={8} className="muted" style={{ textAlign: 'center', padding: 24 }}>Son 90 gündə öz düzəlişi olmayıb</td></tr>
            )}
            {failed === null && !data && (
              <tr><td colSpan={8} className="muted" style={{ textAlign: 'center', padding: 24 }}>Yüklənir…</td></tr>
            )}
            {data?.items.map((item) => (
              <tr key={item.id}>
                <td data-label="Gün" className="mono">{fmtDate(item.date)}</td>
                <td data-label="İşçi" style={{ fontWeight: 700 }}><EmployeeLink id={item.employeeId} name={item.employeeName} /></td>
                <td data-label="Nə edilib">{KIND[item.kind] ?? item.kind}</td>
                <td data-label="Əvvəl → sonra" className="mono">
                  {times(item.checkInBeforeUtc, item.checkOutBeforeUtc)} → {times(item.checkInAfterUtc, item.checkOutAfterUtc)}
                </td>
                <td data-label="Səbəb" style={{ maxWidth: 260 }}>{item.reason}</td>
                <td data-label="Nə vaxt" className="mono">{fmtLongDateOfInstant(item.atUtc)} {fmtTime(item.atUtc)}</td>
                <td data-label="Vəziyyət">
                  {item.awaitsApproval ? (
                    <span className="badge b-pending">Təsdiq gözləyir</span>
                  ) : item.decision ? (
                    <span title={item.decision.reason} className={`badge ${item.decision.approved ? 'b-present' : 'b-absent'}`}>
                      {item.decision.approved ? 'Təsdiqləndi' : 'Rədd edildi'} · {item.decision.byName}
                    </span>
                  ) : (
                    <span className="badge b-present">Tətbiq olunub</span>
                  )}
                </td>
                <td data-label="">
                  {item.mayDecide && (
                    <div style={{ display: 'flex', gap: 6, justifyContent: 'flex-end' }}>
                      <button className="btn btn-sm btn-primary" disabled={busy === item.id} onClick={() => void decide(item, true)}>
                        <IconCheck /> Təsdiqlə
                      </button>
                      <button className="btn btn-sm" disabled={busy === item.id} onClick={() => void decide(item, false)}>
                        <IconX /> Rədd et
                      </button>
                    </div>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  )
}
