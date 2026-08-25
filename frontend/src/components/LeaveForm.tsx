import { useMemo, useState, type FormEvent } from 'react'
import { IconCheck, IconX } from './icons'

/**
 * Filing a leave, permission or rest day — the one form both the admin and the manager use.
 *
 * It was a single-employee form, and the shape of the real work is not that. Two thirds of every leave
 * ever filed in this product is an "İstirahət" of one or two days, and a rest day is something a crew
 * takes together: marking a shift's Sunday meant the same four fields retyped once per person, twenty
 * times. So the employee field is a list you tick, with a search box and a select-all over whatever is
 * currently filtered — pick the shift, tick them all, one save.
 *
 * The other defaults come from the same 200 records. "İstirahət" leads the type list because it is 68%
 * of them. The dates start on today and the form opens in single-day mode, because the average leave
 * is 1.8 days long and typing two identical dates for one day off is the commonest thing anybody did
 * here. The note is folded away: it was filled in 4 times out of 200.
 *
 * Both callers pass their own submit, because the admin and the manager post to different endpoints
 * with different scopes — but they must not drift apart again, which is why the form itself is here.
 */
export type LeaveTypeKey = 'Rest' | 'Permission' | 'Vacation' | 'Sick' | 'Unpaid' | 'BusinessTrip'

/** Ordered by how often each is actually used, not by the enum. */
export const LEAVE_TYPES: { key: LeaveTypeKey; label: string; hint: string }[] = [
  { key: 'Rest', label: 'İstirahət', hint: 'növbə istirahəti — gün iş günü sayılmır' },
  { key: 'BusinessTrip', label: 'Ezamiyyət', hint: 'sahədədir — qayıb sayılmır, maaşdan tutulmur' },
  { key: 'Vacation', label: 'Məzuniyyət', hint: 'illik məzuniyyət' },
  { key: 'Permission', label: 'İcazə', hint: 'qısamüddətli icazə' },
  { key: 'Sick', label: 'Xəstəlik', hint: 'xəstəlik vərəqəsi' },
  { key: 'Unpaid', label: 'Ödənişsiz', hint: 'ödənişsiz məzuniyyət' },
]

export interface LeavePerson {
  id: string
  fullName: string
  locationName?: string | null
}

export interface LeaveSubmitResult {
  created: { employeeId: string; fullName: string }[]
  skipped: { employeeId: string; fullName: string; reason: string; conflictType?: string; conflictFrom?: string; conflictTo?: string }[]
}

function isoToday(offsetDays = 0): string {
  const d = new Date()
  d.setDate(d.getDate() + offsetDays)
  return d.toISOString().slice(0, 10)
}

export function LeaveForm({
  people,
  onSubmit,
  busy,
}: {
  people: LeavePerson[]
  /** Resolves with what the server did, or throws/returns null on a hard failure. */
  onSubmit: (input: {
    employeeIds: string[]
    fromDate: string
    toDate: string
    type: LeaveTypeKey
    note: string
  }) => Promise<LeaveSubmitResult | null>
  busy?: boolean
}) {
  const [picked, setPicked] = useState<string[]>([])
  const [search, setSearch] = useState('')
  const [oneDay, setOneDay] = useState(true)
  const [fromDate, setFromDate] = useState(isoToday())
  const [toDate, setToDate] = useState(isoToday())
  const [type, setType] = useState<LeaveTypeKey>('Rest')
  const [note, setNote] = useState('')
  const [showNote, setShowNote] = useState(false)
  const [result, setResult] = useState<LeaveSubmitResult | null>(null)
  const [error, setError] = useState<string | null>(null)

  const q = search.trim().toLowerCase()
  const visible = useMemo(
    () => (q ? people.filter((p) => p.fullName.toLowerCase().includes(q)) : people),
    [people, q],
  )
  const allVisiblePicked = visible.length > 0 && visible.every((p) => picked.includes(p.id))

  function setStart(value: string) {
    setFromDate(value)
    // Single-day is the common case, so the end follows the start rather than being typed twice.
    if (oneDay || value > toDate) setToDate(value)
  }

  function quick(offsetDays: number) {
    const d = isoToday(offsetDays)
    setFromDate(d)
    setToDate(d)
    setOneDay(true)
  }

  async function submit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    setResult(null)
    if (picked.length === 0) {
      setError('Ən azı bir işçi seçin')
      return
    }
    const res = await onSubmit({ employeeIds: picked, fromDate, toDate, type, note })
    if (!res) {
      setError('Yadda saxlanmadı')
      return
    }
    setResult(res)
    if (res.created.length > 0) {
      setPicked([])
      setNote('')
    }
  }

  const typeInfo = LEAVE_TYPES.find((t) => t.key === type)

  return (
    <form className="card card-pad" style={{ marginBottom: 16 }} onSubmit={submit}>
      <div className="card-title">Məzuniyyət / icazə əlavə et</div>

      {error && (
        <div className="fb fb-err" style={{ marginBottom: 12 }}>
          <IconX />
          <span>{error}</span>
        </div>
      )}

      {result && (
        <div style={{ marginBottom: 12 }}>
          {result.created.length > 0 && (
            <div className="fb fb-ok">
              <IconCheck />
              <span>{result.created.length} nəfərə yazıldı</span>
            </div>
          )}
          {/* Named, not counted. "3 nəfər yazılmadı" sends somebody back to compare two lists by
              hand; the name and the dates they are already off are the whole answer. */}
          {result.skipped.length > 0 && (
            <div className="fb fb-err" style={{ marginTop: 8, alignItems: 'flex-start' }}>
              <IconX />
              <div>
                {result.skipped.map((s) => (
                  <div key={s.employeeId} style={{ fontSize: 13 }}>
                    <b>{s.fullName || 'İşçi'}</b>
                    {s.reason === 'Overlaps'
                      ? ` — bu tarixlərdə artıq ${
                          LEAVE_TYPES.find((t) => t.key === s.conflictType)?.label ?? s.conflictType
                        } var (${s.conflictFrom} – ${s.conflictTo})`
                      : s.reason === 'EmployeeNotManaged'
                        ? ' — sizin filialınızda deyil'
                        : ' — əlavə edilmədi'}
                  </div>
                ))}
              </div>
            </div>
          )}
        </div>
      )}

      {/* Who */}
      <div style={{ marginBottom: 12 }}>
        <label className="form-label">
          Kimlər üçün {picked.length > 0 && <span style={{ color: 'var(--leaf-d)' }}>· {picked.length} seçilib</span>}
        </label>
        <div style={{ display: 'flex', gap: 8, marginBottom: 6 }}>
          <input
            className="inp"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Ad ilə axtar…"
          />
          <button
            type="button"
            className="btn btn-sm"
            style={{ whiteSpace: 'nowrap' }}
            onClick={() =>
              setPicked((cur) =>
                allVisiblePicked
                  ? cur.filter((id) => !visible.some((p) => p.id === id))
                  : [...new Set([...cur, ...visible.map((p) => p.id)])],
              )
            }
          >
            {allVisiblePicked ? 'Seçimi ləğv et' : `Hamısını seç (${visible.length})`}
          </button>
        </div>
        <div
          style={{
            border: '1px solid var(--c200)',
            borderRadius: 10,
            padding: '8px 12px',
            maxHeight: 190,
            overflowY: 'auto',
            display: 'flex',
            flexDirection: 'column',
            gap: 4,
          }}
        >
          {visible.map((p) => (
            <label key={p.id} style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 13, cursor: 'pointer' }}>
              <input
                type="checkbox"
                checked={picked.includes(p.id)}
                onChange={(e) =>
                  setPicked((cur) => (e.target.checked ? [...cur, p.id] : cur.filter((id) => id !== p.id)))
                }
              />
              <span>{p.fullName}</span>
              {p.locationName && (
                <span className="muted" style={{ fontSize: 11 }}>· {p.locationName}</span>
              )}
            </label>
          ))}
          {visible.length === 0 && <span className="muted" style={{ fontSize: 12 }}>Uyğun işçi yoxdur</span>}
        </div>
      </div>

      {/* When */}
      <div style={{ marginBottom: 12 }}>
        <label className="form-label">Nə vaxt</label>
        <div style={{ display: 'flex', gap: 6, marginBottom: 8, flexWrap: 'wrap' }}>
          <button type="button" className="btn btn-sm" onClick={() => quick(0)}>Bu gün</button>
          <button type="button" className="btn btn-sm" onClick={() => quick(1)}>Sabah</button>
          <label style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 13, marginLeft: 4 }}>
            <input
              type="checkbox"
              checked={oneDay}
              onChange={(e) => {
                setOneDay(e.target.checked)
                if (e.target.checked) setToDate(fromDate)
              }}
            />
            Bir gün
          </label>
        </div>
        <div className="form-row cols2" style={{ marginBottom: 0 }}>
          <div>
            <label className="form-label">Başlanğıc</label>
            <input className="inp" type="date" required value={fromDate} onChange={(e) => setStart(e.target.value)} />
          </div>
          {!oneDay && (
            <div>
              <label className="form-label">Bitmə</label>
              <input
                className="inp"
                type="date"
                required
                min={fromDate}
                value={toDate}
                onChange={(e) => setToDate(e.target.value)}
              />
            </div>
          )}
        </div>
      </div>

      {/* What */}
      <div style={{ marginBottom: 12 }}>
        <label className="form-label">Növ</label>
        <select className="inp" value={type} onChange={(e) => setType(e.target.value as LeaveTypeKey)}>
          {LEAVE_TYPES.map((t) => (
            <option key={t.key} value={t.key}>{t.label}</option>
          ))}
        </select>
        {typeInfo && (
          <div className="muted" style={{ fontSize: 12, marginTop: 4 }}>{typeInfo.hint}</div>
        )}
      </div>

      {/* The note, folded away — filled in 4 times out of 200. */}
      {showNote ? (
        <div style={{ marginBottom: 12 }}>
          <label className="form-label">Qeyd</label>
          <input className="inp" value={note} onChange={(e) => setNote(e.target.value)} placeholder="məs. Ailə vəziyyəti" />
        </div>
      ) : (
        <button
          type="button"
          onClick={() => setShowNote(true)}
          style={{
            background: 'none', border: 'none', color: 'var(--c500)', fontSize: 12,
            cursor: 'pointer', textDecoration: 'underline', textUnderlineOffset: 2,
            padding: 0, marginBottom: 12, display: 'block',
          }}
        >
          ＋ Qeyd əlavə et
        </button>
      )}

      <button type="submit" className="btn btn-primary" disabled={busy || picked.length === 0}>
        <IconCheck />
        {busy ? 'Yadda saxlanır…' : picked.length > 1 ? `${picked.length} nəfərə yaz` : 'Əlavə et'}
      </button>
    </form>
  )
}
