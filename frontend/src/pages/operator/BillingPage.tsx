import { useEffect, useMemo, useState } from 'react'
import { getBilling, markBilling, type BillingResponse, type BillingRow } from '../../api/admin'
import { ConsoleToolbar, EmptyState, TableSkeleton, TenantAvatar, matches, useTableSort } from './console'
import { parseMoney, formatMoney as money } from '../../lib/money'
import { useCan } from './OperatorContext'

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
  const canBill = useCan('Billing')
  const [query, setQuery] = useState('')
  const [only, setOnly] = useState('all')
  // Biggest invoice first: the money is what this page is read for.
  const { sort, Th, key: sortKey, dir: sortDir } = useTableSort<BillingRow>('amount', 'desc')

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

  const shown = useMemo(() => {
    const rows = (data?.rows ?? []).filter(
      (r) => only === 'all' || (only === 'paid' ? r.isPaid : !r.isPaid),
    )
    return sort(rows.filter((r) => matches(query, r.displayName)))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [data, query, only, sortKey, sortDir])

  const FILTERS = [
    { key: 'all', label: 'Hamısı', count: t?.totalCount ?? 0 },
    { key: 'unpaid', label: 'Gözləyir', count: (t?.totalCount ?? 0) - (t?.paidCount ?? 0) },
    { key: 'paid', label: 'Ödənilib', count: t?.paidCount ?? 0 },
  ]

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
        <ConsoleToolbar
          query={query}
          onQuery={setQuery}
          placeholder="Şirkət adı üzrə axtar…"
          filters={FILTERS}
          activeFilter={only}
          onFilter={setOnly}
        />
        <table className="tbl">
          <thead>
            <tr>
              <Th field="displayName">Şirkət</Th>
              <Th field="employeeCount" className="num">İşçi</Th>
              <Th field="amount" className="num">Aylıq məbləğ</Th>
              <th>Status</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {loading && <TableSkeleton rows={5} cols={5} />}
            {!loading && shown.length === 0 && (
              <tr><td colSpan={5} style={{ padding: 0 }}>
                <EmptyState
                  icon={data && data.rows.length > 0 ? '🔍' : '💳'}
                  title={data && data.rows.length > 0 ? 'Nəticə yoxdur' : 'Bu ay üçün faktura yoxdur'}
                  note={data && data.rows.length > 0
                    ? 'Axtarışı və ya süzgəci dəyişin.'
                    : 'Aktiv şirkət olan kimi aylıq məbləğlər burada hesablanacaq.'}
                />
              </td></tr>
            )}
            {!loading && shown.map((r) => (
              <tr key={r.tenantId}>
                <td>
                  <div className="con-named">
                    <TenantAvatar name={r.displayName} />
                    <div className="con-named-t">
                      <b>
                        {r.displayName}
                        {!r.isActive && (
                          <span className="tag" style={{ marginLeft: 6, background: 'var(--tag-neutral, rgba(0,0,0,0.05))', color: 'var(--c400)' }}>
                            deaktiv
                          </span>
                        )}
                      </b>
                      <span>
                        {r.plan ?? '— plansız'}
                        {r.priceOverride != null && <> · <span title="Fərdi qiymət">fərdi</span></>}
                      </span>
                    </div>
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
                    <span className="tag" style={{ background: 'var(--tag-neutral, rgba(0,0,0,0.05))', color: 'var(--c400)' }}>
                      Gözləyir
                    </span>
                  )}
                </td>
                <td style={{ textAlign: 'right', whiteSpace: 'nowrap' }}>
                  {canBill && (
                    <button
                      className={`btn btn-sm${r.isPaid ? '' : ' btn-primary'}`}
                      disabled={busyId === r.tenantId}
                      onClick={() => void toggle(r)}
                    >
                      {busyId === r.tenantId ? '…' : r.isPaid ? 'Geri al' : 'Ödənildi'}
                    </button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <div className="muted" style={{ fontSize: 12, marginTop: 10 }}>
        Məbləğ: fərdi qiymət təyin olunubsa o, əks halda dərc olunmuş paket tarifi (işçi sayına görə: 1–10 → 4₼,
        11–50 → 3.5₼, 51+ → 3₼ hər işçi) + hər aktiv filial 5₼/ay. «Ödənildi» qeyd edərkən məbləği dəyişə bilərsiniz.
      </div>
    </div>
  )
}
