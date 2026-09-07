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
import { IconCheck, IconX } from '../../components/icons'

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

  return (
    <div className="card card-pad">
      <div className="card-title">Növbə əvəzləmələri</div>
      <div className="muted" style={{ fontSize: 12, marginTop: -10, marginBottom: 14 }}>
        İşçi bir gün başqasının növbəsində işləyibsə, həmin günü burada qeyd edin. O gün seçilmiş
        növbəyə görə hesablanır — gecə növbəsidirsə, səhər vurulan çıxış həmin gecəni bağlayır.
        Ertəsi gün işçi öz növbəsinə qayıdır.
      </div>

      {error && <div className="fb fb-err" style={{ marginBottom: 10 }}><IconX /><span>{error}</span></div>}
      {ok && <div className="fb fb-ok" style={{ marginBottom: 10 }}><IconCheck /><span>{ok}</span></div>}

      <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', alignItems: 'flex-end', marginBottom: 14 }}>
        <label style={{ fontSize: 12 }}>
          Tarix
          <input className="inp" type="date" value={date} onChange={(e) => setDate(e.target.value)} style={{ marginTop: 2 }} />
        </label>
        <label style={{ fontSize: 12, minWidth: 190 }}>
          Növbə
          <select className="inp" value={scheduleId} onChange={(e) => setScheduleId(e.target.value)} style={{ marginTop: 2 }}>
            <option value="">Seçin…</option>
            {schedules.map((s) => (
              <option key={s.id} value={s.id}>{s.name} ({s.shiftStart}–{s.shiftEnd})</option>
            ))}
          </select>
        </label>
        <label style={{ fontSize: 12, flex: 1, minWidth: 160 }}>
          Səbəb (istəyə bağlı)
          <input
            className="inp"
            value={note}
            onChange={(e) => setNote(e.target.value)}
            placeholder="mühafizəçini əvəz etdi"
            style={{ marginTop: 2 }}
          />
        </label>
        <button className="btn btn-primary btn-sm" onClick={() => void add()} disabled={busy || !date || !scheduleId}>
          <IconCheck /> Əlavə et
        </button>
      </div>

      {rows === null ? (
        <div className="muted" style={{ fontSize: 13 }}>Yüklənir…</div>
      ) : rows.length === 0 ? (
        <div className="muted" style={{ fontSize: 13 }}>Hələ əvəzləmə yoxdur.</div>
      ) : (
        <div style={{ display: 'flex', flexDirection: 'column' }}>
          {rows.map((r, i) => (
            <div
              key={r.id}
              style={{
                display: 'flex', alignItems: 'center', gap: 12, padding: '9px 2px',
                borderBottom: i < rows.length - 1 ? '1px solid var(--c50)' : 'none',
              }}
            >
              <span className="mono" style={{ fontSize: 13, minWidth: 92 }}>{fmtDate(r.date)}</span>
              <span style={{ flex: 1, minWidth: 0 }}>
                <b style={{ fontSize: 13.5, color: 'var(--c900)' }}>{r.scheduleName}</b>
                <span className="muted" style={{ fontSize: 12 }}> · {r.shiftStart}–{r.shiftEnd}</span>
                {r.note && <div className="muted" style={{ fontSize: 12, fontStyle: 'italic' }}>{r.note}</div>}
              </span>
              <button className="btn btn-sm" onClick={() => void remove(r)} disabled={busy}>Sil</button>
            </div>
          ))}
        </div>
      )}
    </div>
  )
}
