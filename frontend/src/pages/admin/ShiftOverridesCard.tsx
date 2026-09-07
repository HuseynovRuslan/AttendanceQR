import { useEffect, useState } from 'react'
import {
  getSchedules,
  getShiftOverrides,
  removeShiftOverride,
  setShiftOverride,
  type Schedule,
  type ShiftOverrideRow,
} from '../../api/admin'
import { fmtDate } from '../../lib/format'
import { IconCalendar, IconCheck, IconX } from '../../components/icons'

/**
 * «Növbə əvəzləmələri» — the days this person worked somebody else's shift.
 *
 * A shift belongs to a PERSON, which is right on the ninety-nine days they work their own hours and
 * wrong on the hundredth. Nəcəfov Vüqar is on «FM 2-ci növbə 13:00–23:00»; on Saturday 5 September he
 * covered the night guard, arrived 21:33 and left 06:51. The system judged that night against his own
 * DAY shift, so the rule that closes a night with a morning scan never ran: his Saturday stayed open
 * at zero hours and the 06:51 exit opened a fresh check-in on his rest day. One night, nine hours and
 * a rest day gone.
 *
 * Naming a SHIFT rather than typing hours is the whole design. Hours typed onto a day are a copy, and
 * a copy drifts from the rota — the disease the shift catalogue was built to cure. «That night he was
 * on Gecə A» is both what happened and what the rota says, and every rule downstream (the overnight
 * pivot, the working-day mask, the late threshold) then applies with no special case at all.
 */
export function ShiftOverridesCard({ employeeId, onChanged }: {
  employeeId: string
  /** The month figures above this card are computed from these days — refresh them after a change. */
  onChanged?: () => void
}) {
  const [rows, setRows] = useState<ShiftOverrideRow[] | null>(null)
  const [schedules, setSchedules] = useState<Schedule[]>([])
  const [date, setDate] = useState('')
  const [scheduleId, setScheduleId] = useState('')
  const [note, setNote] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [ok, setOk] = useState<string | null>(null)

  useEffect(() => {
    void load()
    void getSchedules().then((r) => {
      if (r.status === 200 && Array.isArray(r.data)) setSchedules(r.data)
    })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [employeeId])

  async function load() {
    const { status, data } = await getShiftOverrides(employeeId)
    setRows(status === 200 && Array.isArray(data) ? data : [])
  }

  async function add() {
    if (!date || !scheduleId) return
    setBusy(true); setError(null); setOk(null)
    const { status, data } = await setShiftOverride({ employeeId, date, scheduleId, note: note.trim() || null })
    setBusy(false)
    if (status !== 200 || (data && 'error' in data)) { setError('Yadda saxlanmadı'); return }
    setOk(`${fmtDate(date)} — əvəzləmə yazıldı`)
    setDate(''); setNote('')
    await load()
    onChanged?.()
  }

  async function remove(r: ShiftOverrideRow) {
    if (!window.confirm(`${fmtDate(r.date)} — «${r.scheduleName}» əvəzləməsi silinsin? Həmin gün işçinin öz növbəsinə qayıdacaq.`))
      return
    setBusy(true); setError(null); setOk(null)
    const { status } = await removeShiftOverride(r.id)
    setBusy(false)
    if (status !== 200) { setError('Silinmədi'); return }
    await load()
    onChanged?.()
  }

  // One height for every control in the row, whatever the browser gives a <select> or a date
  // picker by default — the four of them sat at three different heights before this.
  const control = { height: 38, padding: '0 12px', fontSize: 13, marginTop: 4 } as const

  return (
    <div className="card card-pad">
      {/* One sentence says what the card is for; the mechanics (why a night closes in the morning)
          live in the tooltip, for the one admin in ten who wants to know. */}
      <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 4 }}>
        <div className="card-title" style={{ margin: 0 }}>Növbə əvəzləmələri</div>
        {rows && rows.length > 0 && <span className="tag">{rows.length}</span>}
        <span
          className="muted"
          title="O gün seçilmiş növbəyə görə hesablanır — gecə növbəsidirsə, səhər vurulan çıxış həmin gecəni bağlayır."
          aria-label="İzah"
          style={{
            display: 'inline-flex', alignItems: 'center', justifyContent: 'center', width: 16, height: 16,
            borderRadius: '50%', border: '1px solid var(--c200)', fontSize: 10, fontWeight: 700, cursor: 'help',
          }}
        >
          i
        </span>
      </div>
      <div className="muted" style={{ fontSize: 12.5, marginBottom: 14, lineHeight: 1.5 }}>
        Müvəqqəti başqa növbədə işlədiyi günü qeyd edin. Ertəsi gün işçi avtomatik öz növbəsinə qayıdır.
      </div>

      {error && <div className="fb fb-err" style={{ marginBottom: 10 }}><IconX /><span>{error}</span></div>}
      {ok && <div className="fb fb-ok" style={{ marginBottom: 10 }}><IconCheck /><span>{ok}</span></div>}

      {/* One compact row: date · shift · reason · button. The reason column stretches, the button
          keeps its own width — on a narrow screen the row folds to one control per line. */}
      <div
        style={{
          display: 'grid', gridTemplateColumns: 'minmax(140px, 160px) minmax(200px, 1.2fr) minmax(160px, 1fr) auto',
          gap: 10, alignItems: 'end', marginBottom: 16,
        }}
        className="ovr-form"
      >
        <label className="form-label" style={{ margin: 0 }}>
          Tarix
          <input className="inp" type="date" value={date} onChange={(e) => setDate(e.target.value)} style={control} />
        </label>
        <label className="form-label" style={{ margin: 0 }}>
          Növbə
          <select className="inp" value={scheduleId} onChange={(e) => setScheduleId(e.target.value)} style={control}>
            <option value="">Seçin…</option>
            {schedules.map((s) => (
              <option key={s.id} value={s.id}>{s.name} · {s.shiftStart}–{s.shiftEnd}</option>
            ))}
          </select>
        </label>
        <label className="form-label" style={{ margin: 0 }}>
          Səbəb <span style={{ fontWeight: 500, color: 'var(--c400)' }}>(istəyə bağlı)</span>
          <input
            className="inp"
            value={note}
            onChange={(e) => setNote(e.target.value)}
            placeholder="mühafizəçini əvəz etdi"
            maxLength={200}
            style={control}
          />
        </label>
        <button
          className="btn btn-primary"
          onClick={() => void add()}
          disabled={busy || !date || !scheduleId}
          style={{ height: 38, padding: '0 16px', fontSize: 13 }}
        >
          <IconCheck /> Əlavə et
        </button>
      </div>

      {rows === null ? (
        <div className="muted" style={{ fontSize: 13 }}>Yüklənir…</div>
      ) : rows.length === 0 ? (
        <div
          style={{
            display: 'flex', alignItems: 'center', gap: 12, padding: '14px 16px', borderRadius: 12,
            background: 'var(--c50)', border: '1px dashed var(--c200)', color: 'var(--c500)', fontSize: 13,
          }}
        >
          <IconCalendar style={{ width: 18, height: 18, flexShrink: 0, color: 'var(--c400)' }} />
          <span>Hələ əvəzləmə yoxdur — işçi bütün günləri öz növbəsində hesablanır.</span>
        </div>
      ) : (
        <div style={{ border: '1px solid var(--c100)', borderRadius: 12, overflow: 'hidden' }}>
          {rows.map((r, i) => (
            <div
              key={r.id}
              style={{
                display: 'flex', alignItems: 'center', gap: 14, padding: '10px 14px',
                borderTop: i === 0 ? 'none' : '1px solid var(--c100)',
              }}
            >
              <span
                className="mono"
                style={{
                  fontSize: 12, fontWeight: 600, color: 'var(--c700)', background: 'var(--c100)',
                  padding: '3px 8px', borderRadius: 6, whiteSpace: 'nowrap', fontVariantNumeric: 'tabular-nums',
                }}
              >
                {fmtDate(r.date)}
              </span>
              <span style={{ flex: 1, minWidth: 0, display: 'flex', flexWrap: 'wrap', alignItems: 'baseline', columnGap: 10, rowGap: 2 }}>
                <b style={{ fontSize: 13.5, color: 'var(--c900)' }}>{r.scheduleName}</b>
                <span className="mono" style={{ fontSize: 12, color: 'var(--c500)' }}>{r.shiftStart}–{r.shiftEnd}</span>
                {r.note && (
                  <span className="muted" style={{ fontSize: 12, fontStyle: 'italic', flexBasis: '100%' }}>{r.note}</span>
                )}
              </span>
              <button className="btn btn-sm btn-ghost-danger" onClick={() => void remove(r)} disabled={busy}>
                Sil
              </button>
            </div>
          ))}
        </div>
      )}
    </div>
  )
}
