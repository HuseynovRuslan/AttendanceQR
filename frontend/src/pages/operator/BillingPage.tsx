import { useEffect, useState } from 'react'
import { getBilling, markBilling, type BillingResponse, type BillingRow } from '../../api/admin'
import { parseMoney, formatMoney as money } from '../../lib/money'

const MONTHS_AZ = [
  'Yanvar', 'Fevral', 'Mart', 'Aprel', 'May', 'İyun',
  'İyul', 'Avqust', 'Sentyabr', 'Oktyabr', 'Noyabr', 'Dekabr',
]

function Stat({ label, value, tone }: { label: string; value: string; tone?: 'ok' | 'warn' }) {
  const color = tone === 'ok' ? 'var(--leaf-d)' : tone === 'warn' ? 'var(--clay)' : 'var(--c900)'
  return (
    <div className="card card-pad" style={{ flex: 1, minWidth: 150 }}>
      <div className="muted" style={{ fontSize: 12, marginBottom: 4 }}>{label}</div>
      <div style={{ fontSize: 22, fontWeight: 800, color, fontVariantNumeric: 'tabular-nums' }}>{value}</div>
    </div>
  )
}

export function BillingPage() {
  const now = new Date()
  const [year, setYear] = useState(now.getUTCFullYear())
  const [month, setMonth] = useState(now.getUTCMonth() + 1)
  const [data, setData] = useState<BillingResponse | null>(null)
  const [loading, setLoading] = useState(true)
  const [busyId, setBusyId] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  async function load() {
    setLoading(true)
    try {
      const res = await getBilling(year, month)
      if (res.status === 200 && res.data && 'rows' in res.data) {
        setData(res.data)
        setError(null)
      } else if (res.status === 403) {
        setError('İcazəniz yoxdur')
      } else {
        setError('Yüklənmədi')
      }
    } catch {
      setError('Yüklənmədi')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    void load()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [year, month])

  function shift(delta: number) {
    let m = month + delta
    let y = year
    if (m < 1) { m = 12; y -= 1 } else if (m > 12) { m = 1; y += 1 }
    setMonth(m)
    setYear(y)
  }

  async function toggle(r: BillingRow) {
    if (!r.isPaid) {
      const input = window.prompt(
        `«${r.displayName}» — ${MONTHS_AZ[month - 1]} ${year} üçün ödənilən məbləğ (₼):`,
        String(r.amount),
      )
      if (input === null) return
      // parseMoney rejects empty (would otherwise be 0 → "paid for 0 ₼"), NaN and ambiguous "1.250".
      const amt = parseMoney(input)
      if (amt === null) { setError('Məbləğ yanlışdır — məs. 1250 və ya 1250.50'); return }
      setBusyId(r.tenantId); setError(null)
      const { status } = await markBilling(r.tenantId, { year, month, isPaid: true, amount: amt })
      setBusyId(null)
      if (status === 200) await load(); else setError('Alınmadı')
    } else {
      if (!window.confirm(`«${r.displayName}» — ödəniş qeydini geri alaq?`)) return
      setBusyId(r.tenantId); setError(null)
      const { status } = await markBilling(r.tenantId, { year, month, isPaid: false })
      setBusyId(null)
      if (status === 200) await load(); else setError('Alınmadı')
    }
  }

  const t = data?.totals

  return (
    <div>
      {/* Month selector */}
      <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 16 }}>
        <button className="btn btn-sm" onClick={() => shift(-1)} aria-label="Əvvəlki ay">←</button>
        <div style={{ fontWeight: 800, fontSize: 16, minWidth: 150, textAlign: 'center' }}>
          {MONTHS_AZ[month - 1]} {year}
        </div>
        <button className="btn btn-sm" onClick={() => shift(1)} aria-label="Sonrakı ay">→</button>
      </div>

      {error && <div className="fb fb-err" style={{ marginBottom: 14 }}>{error}</div>}

      {/* Totals */}
      <div style={{ display: 'flex', gap: 12, marginBottom: 18, flexWrap: 'wrap' }}>
        <Stat label="Gözlənilən (MRR)" value={money(t?.billed ?? 0)} />
        <Stat label="Yığılıb" value={money(t?.collected ?? 0)} tone="ok" />
        <Stat label="Gözləyir" value={money(t?.outstanding ?? 0)} tone={t && t.outstanding > 0 ? 'warn' : undefined} />
        <Stat label="Ödəniş" value={`${t?.paidCount ?? 0} / ${t?.totalCount ?? 0}`} />
      </div>

      <div className="card">
        <table className="tbl">
          <thead>
            <tr>
              <th>Şirkət</th>
              <th className="num">İşçi</th>
              <th className="num">Aylıq məbləğ</th>
              <th>Status</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {loading && <tr><td colSpan={5} className="muted" style={{ padding: 18 }}>Yüklənir…</td></tr>}
            {!loading && data && data.rows.length === 0 && (
              <tr><td colSpan={5} className="muted" style={{ padding: 18 }}>Aktiv şirkət yoxdur</td></tr>
            )}
            {!loading && data?.rows.map((r) => (
              <tr key={r.tenantId}>
                <td>
                  <div style={{ fontWeight: 700 }}>
                    {r.displayName}
                    {!r.isActive && (
                      <span className="tag" style={{ marginLeft: 6, background: 'rgba(0,0,0,0.05)', color: 'var(--c400)' }}>
                        deaktiv
                      </span>
                    )}
                  </div>
                  <div style={{ fontSize: 11, color: 'var(--c400)' }}>
                    {r.plan ?? '— plansız'}
                    {r.priceOverride != null && <> · <span title="Fərdi qiymət">fərdi</span></>}
                  </div>
                </td>
                <td className="num" style={{ fontVariantNumeric: 'tabular-nums' }}>{r.employeeCount}</td>
                <td className="num" style={{ fontVariantNumeric: 'tabular-nums', fontWeight: 700 }}>{money(r.amount)}</td>
                <td>
                  {r.isPaid ? (
                    <span className="tag" style={{ background: 'var(--leaf-bg)', color: 'var(--leaf-d)' }}>
                      ✓ Ödənilib
                    </span>
                  ) : (
                    <span className="tag" style={{ background: 'rgba(0,0,0,0.05)', color: 'var(--c400)' }}>
                      Gözləyir
                    </span>
                  )}
                </td>
                <td style={{ textAlign: 'right', whiteSpace: 'nowrap' }}>
                  <button
                    className={`btn btn-sm${r.isPaid ? '' : ' btn-primary'}`}
                    disabled={busyId === r.tenantId}
                    onClick={() => void toggle(r)}
                  >
                    {busyId === r.tenantId ? '…' : r.isPaid ? 'Geri al' : 'Ödənildi'}
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <div className="muted" style={{ fontSize: 12, marginTop: 10 }}>
        Məbləğ: fərdi qiymət təyin olunubsa o, əks halda pilləli tarif (işçi sayına görə: 1–30 → 4₼, 31–100 → 3.5₼,
        101–500 → 3₼). «Ödənildi» qeyd edərkən məbləği dəyişə bilərsiniz.
      </div>
    </div>
  )
}
