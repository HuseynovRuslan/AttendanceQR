// Hand-rolled, dependency-free SVG charts — no charting library, keeps bundle size down. Two
// series (check-ins / check-outs) rendered consistently across both chart types.
const WEEKDAY_LABELS = ['Bazar', 'B.e', 'Ç.a', 'Çər', 'C.a', 'Cümə', 'Şən']

export function ChartLegend() {
  return (
    <div style={{ display: 'flex', gap: 16, fontSize: 12, color: 'var(--c500)', marginBottom: 8 }}>
      <span style={{ display: 'flex', alignItems: 'center', gap: 4 }}>
        <span style={{ width: 10, height: 10, borderRadius: 2, background: 'var(--leaf)', display: 'inline-block' }} />
        Girişlər
      </span>
      <span style={{ display: 'flex', alignItems: 'center', gap: 4 }}>
        <span style={{ width: 10, height: 10, borderRadius: 2, background: 'var(--blue)', display: 'inline-block' }} />
        Çıxışlar
      </span>
    </div>
  )
}

function fmtShortDate(dateStr: string): string {
  const [, m, d] = dateStr.split('-')
  return `${d}.${m}`
}

interface TrendPoint {
  date: string
  checkIns: number
  checkOuts: number
}

export function TrendChart({ points }: { points: TrendPoint[] }) {
  const W = 640
  const H = 200
  const PAD = 32

  if (points.length === 0) return <p className="muted" style={{ fontSize: 13 }}>Məlumat yoxdur</p>

  const maxVal = Math.max(1, ...points.map((p) => Math.max(p.checkIns, p.checkOuts)))
  const stepX = points.length > 1 ? (W - PAD * 2) / (points.length - 1) : 0
  const scaleY = (v: number) => H - PAD - (v / maxVal) * (H - PAD * 2)
  const lineFor = (key: 'checkIns' | 'checkOuts') =>
    points.map((p, i) => `${PAD + i * stepX},${scaleY(p[key])}`).join(' ')
  const labelEvery = Math.max(1, Math.ceil(points.length / 7))

  return (
    <svg viewBox={`0 0 ${W} ${H + 24}`} style={{ width: '100%', height: 'auto', display: 'block' }}>
      {[0, 0.25, 0.5, 0.75, 1].map((f) => (
        <line key={f} x1={PAD} x2={W - PAD} y1={scaleY(maxVal * f)} y2={scaleY(maxVal * f)} stroke="var(--c100)" strokeWidth={1} />
      ))}
      <polyline points={lineFor('checkIns')} fill="none" stroke="var(--leaf)" strokeWidth={2} />
      <polyline points={lineFor('checkOuts')} fill="none" stroke="var(--blue)" strokeWidth={2} />
      {points.map((p, i) => (
        <g key={p.date}>
          <circle cx={PAD + i * stepX} cy={scaleY(p.checkIns)} r={3} fill="var(--leaf)">
            <title>{`${p.date}: ${p.checkIns} giriş`}</title>
          </circle>
          <circle cx={PAD + i * stepX} cy={scaleY(p.checkOuts)} r={3} fill="var(--blue)">
            <title>{`${p.date}: ${p.checkOuts} çıxış`}</title>
          </circle>
          {i % labelEvery === 0 && (
            <text x={PAD + i * stepX} y={H + 16} fontSize={10} fill="var(--c400)" textAnchor="middle">
              {fmtShortDate(p.date)}
            </text>
          )}
        </g>
      ))}
    </svg>
  )
}

interface WeekdayBar {
  dayOfWeek: number
  checkIns: number
  checkOuts: number
}

export function WeekdayBarChart({ points }: { points: WeekdayBar[] }) {
  const W = 640
  const H = 200
  const PAD = 32

  const byDay = new Map(points.map((p) => [p.dayOfWeek, p]))
  const full = Array.from({ length: 7 }, (_, i) => byDay.get(i) ?? { dayOfWeek: i, checkIns: 0, checkOuts: 0 })
  const maxVal = Math.max(1, ...full.map((p) => Math.max(p.checkIns, p.checkOuts)))
  const groupW = (W - PAD * 2) / 7
  const barW = groupW * 0.32
  const scaleH = (v: number) => (v / maxVal) * (H - PAD * 2)

  return (
    <svg viewBox={`0 0 ${W} ${H + 24}`} style={{ width: '100%', height: 'auto', display: 'block' }}>
      {full.map((p, i) => {
        const cx = PAD + i * groupW + groupW / 2
        const h1 = scaleH(p.checkIns)
        const h2 = scaleH(p.checkOuts)
        return (
          <g key={p.dayOfWeek}>
            <rect x={cx - barW - 2} y={H - PAD - h1} width={barW} height={h1} fill="var(--leaf)" rx={2}>
              <title>{`${WEEKDAY_LABELS[p.dayOfWeek]}: ${p.checkIns} giriş`}</title>
            </rect>
            <rect x={cx + 2} y={H - PAD - h2} width={barW} height={h2} fill="var(--blue)" rx={2}>
              <title>{`${WEEKDAY_LABELS[p.dayOfWeek]}: ${p.checkOuts} çıxış`}</title>
            </rect>
            <text x={cx} y={H + 16} fontSize={10} fill="var(--c400)" textAnchor="middle">
              {WEEKDAY_LABELS[p.dayOfWeek]}
            </text>
          </g>
        )
      })}
    </svg>
  )
}
