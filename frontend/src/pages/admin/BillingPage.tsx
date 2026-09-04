import { useEffect, useState } from 'react'
import { getMyBilling, type MyBilling } from '../../api/admin'
import { IconAlert, IconCheck, IconX } from '../../components/icons'

const MONTHS = [
  'Yanvar', 'Fevral', 'Mart', 'Aprel', 'May', 'İyun',
  'İyul', 'Avqust', 'Sentyabr', 'Oktyabr', 'Noyabr', 'Dekabr',
]

/** AZN with two decimals, in the local grouping. */
function azn(n: number): string {
  return n.toLocaleString('az-AZ', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
}

function fmtDate(iso: string | null | undefined): string {
  if (!iso) return '—'
  const d = new Date(iso)
  return `${d.getDate()} ${MONTHS[d.getMonth()].toLowerCase()} ${d.getFullYear()}`
}

/**
 * Abunəlik — what this company is paying for, and what it owes.
 *
 * Billing lived only in the operator console: the customer paying for the product could not see their
 * package, this month's amount, or whether last month was settled, and asked by telephone. This is the
 * read-only half of the same numbers — priced by the same formula on the same two counts, so the
 * screen and the invoice cannot disagree.
 *
 * Nothing here changes anything. Choosing a plan, agreeing a price and marking a bill paid stay with
 * the operator, the way they do in every product sold this way. What is new is that the customer can
 * check without asking.
 */
export function BillingPage() {
  const [data, setData] = useState<MyBilling | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    void getMyBilling().then(({ status, data: d }) => {
      if (status === 200 && d && 'monthly' in d) setData(d)
      else if (status === 403) setError('Bu bölmə yalnız admin üçündür')
      else setError('Məlumat yüklənmədi')
    })
  }, [])

  if (error) {
    return (
      <div className="fb fb-err">
        <IconX />
        <span>{error}</span>
      </div>
    )
  }

  if (!data) return <div className="muted">Yüklənir…</div>

  const m = data.monthly
  const now = new Date()

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
      {/* The demo banner is the first thing on the page while it applies, because it is the only
          thing on this screen with a deadline attached. */}
      {data.onTrial && (
        <div className="fb fb-info">
          <IconCheck />
          <span>
            <b>Demo versiya</b> — {fmtDate(data.trialEndsAtUtc)} tarixinədək pulsuz istifadə edirsiniz
            {data.trialDaysLeft > 0 && <> ({data.trialDaysLeft} gün qalıb)</>}. Demo bitəndən sonra
            aylıq məbləğ <b>{azn(m.amount)} ₼</b> olacaq. Bu müddətdə heç bir məhdudiyyət yoxdur.
          </span>
        </div>
      )}

      {data.trialEnded && (
        <div className="fb fb-warn">
          <IconAlert />
          <span>
            Demo müddətiniz {fmtDate(data.trialEndsAtUtc)} tarixində bitib. Sistem tam işləməyə davam
            edir — abunəliklə bağlı sualınız varsa bizimlə əlaqə saxlayın.
          </span>
        </div>
      )}

      <div style={{ display: 'grid', gap: 16, gridTemplateColumns: 'repeat(auto-fit,minmax(260px,1fr))' }}>
        <div className="card card-pad">
          <div className="card-title">Paket</div>
          <div style={{ display: 'flex', alignItems: 'baseline', gap: 10 }}>
            <span style={{ fontSize: 26, fontWeight: 800 }}>{data.plan ?? data.packageByHeadcount}</span>
            {data.onTrial && <span className="badge b-sick">Demo</span>}
          </div>
          {/* The package follows head-count, so a company that grows past a band sees why the price
              moved instead of discovering it on an invoice. */}
          {!data.plan && data.packageByHeadcount !== '—' && (
            <div className="muted" style={{ fontSize: 12, marginTop: 6 }}>
              İşçi sayına görə təyin olunur
            </div>
          )}
        </div>

        <div className="card card-pad">
          <div className="card-title">
            {data.onTrial ? 'Demo bitəndən sonra — aylıq' : `${MONTHS[now.getMonth()]} — aylıq`}
          </div>
          <div style={{ fontSize: 26, fontWeight: 800 }}>{azn(m.amount)} ₼</div>
          {m.isNegotiated ? (
            <div className="muted" style={{ fontSize: 12, marginTop: 6 }}>Sizinlə razılaşdırılmış qiymət</div>
          ) : (
            // "Why is it 248 ₼" is the question a bill gets asked. Answering it here is cheaper than
            // answering it on the telephone.
            <div className="muted" style={{ fontSize: 12, marginTop: 6, lineHeight: 1.6 }}>
              {data.employees} işçi × {azn(m.ratePerEmployee)} ₼ = {azn(m.employeeTotal)} ₼
              <br />
              {data.locations} filial × {azn(m.locationFee)} ₼ = {azn(m.locationTotal)} ₼
            </div>
          )}
        </div>

        <div className="card card-pad">
          <div className="card-title">İstifadə</div>
          <Usage label="İşçi" used={data.employees} max={data.maxEmployees} />
          <div style={{ height: 12 }} />
          <Usage label="Filial" used={data.locations} max={data.maxLocations} />
        </div>
      </div>

      <div className="card card-pad">
        <div className="card-title">Ödəniş tarixçəsi</div>
        {data.invoices.length === 0 ? (
          <div className="muted" style={{ fontSize: 13 }}>
            {data.onTrial
              ? 'Demo müddətindəsiniz — hələ ödəniş yoxdur.'
              : 'Hələ qeyd olunmuş ödəniş yoxdur.'}
          </div>
        ) : (
          <div className="tbl-wrap tbl-center-nums tbl-cards">
            <table>
              <thead>
                <tr>
                  <th>Dövr</th>
                  <th className="num">İşçi</th>
                  <th className="num">Məbləğ</th>
                  <th>Vəziyyət</th>
                  <th>Ödənilib</th>
                  <th>Qeyd</th>
                </tr>
              </thead>
              <tbody>
                {data.invoices.map((i) => (
                  <tr key={`${i.year}-${i.month}`}>
                    <td>{MONTHS[i.month - 1]} {i.year}</td>
                    <td className="num">{i.employeeCount}</td>
                    <td className="num mono">{azn(i.amount)} ₼</td>
                    <td>
                      {i.isPaid ? (
                        <span className="badge b-present">Ödənilib</span>
                      ) : (
                        <span className="badge b-late">Gözləyir</span>
                      )}
                    </td>
                    <td>{fmtDate(i.paidAtUtc)}</td>
                    <td className="muted">{i.note || '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      <div className="muted" style={{ fontSize: 12, lineHeight: 1.7 }}>
        Məbləğ hər ay aktiv işçi və filial sayına görə hesablanır — işçi çıxsa, növbəti ay avtomatik
        azalır. Paket dəyişikliyi, ödəniş və hesab-faktura üçün bizimlə əlaqə saxlayın.
      </div>
    </div>
  )
}

/**
 * Used against the limit. The bar only appears when there IS a limit — a company with none would
 * otherwise get a permanently empty track that suggests a ceiling nobody set.
 */
function Usage({ label, used, max }: { label: string; used: number; max: number | null }) {
  const pct = max && max > 0 ? Math.min(100, Math.round((used / max) * 100)) : null
  // Amber from four-fifths, because the useful moment to notice a limit is before it is reached.
  const tone = pct === null ? 'var(--blue)' : pct >= 100 ? 'var(--clay)' : pct >= 80 ? 'var(--amber)' : 'var(--leaf)'

  return (
    <div>
      <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 13, marginBottom: 5 }}>
        <span>{label}</span>
        <span style={{ fontWeight: 700 }}>
          {used}
          {max ? <span className="muted"> / {max}</span> : ''}
        </span>
      </div>
      {pct !== null && (
        <div style={{ height: 6, borderRadius: 999, background: 'var(--c100)', overflow: 'hidden' }}>
          <div style={{ width: `${pct}%`, height: '100%', background: tone }} />
        </div>
      )}
    </div>
  )
}
