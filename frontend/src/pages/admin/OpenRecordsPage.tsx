import { useEffect, useState } from 'react'
import { EmployeeLink } from '../../components/EmployeeLink'
import { adminUpdateRecord, closeOpenDays, getOpenRecords, type OpenRecord } from '../../api/attendance'
import { IconCheck, IconClock, IconX } from '../../components/icons'
import { fmtDate, fmtTime, fromCompanyInputValue, toCompanyInputValue } from '../../lib/format'

// datetime-local <-> ISO, matching EmployeesPage. The input reads/writes the admin's local time; the
// wire is always UTC ISO.
function toLocalInput(d: Date): string {
  // The company's wall clock, not the device's — see toCompanyInputValue. This screen WRITES the hour
  // an employee is paid for, so a reader's timezone must never reach it.
  return toCompanyInputValue(d.toISOString())
}

// Default check-out: the check-in's own date at 18:00 local — a plausible end of a work day, which
// the admin can override. There is no per-employee schedule to derive a truer value from yet.
function defaultCheckout(checkInIso: string): string {
  const d = new Date(checkInIso)
  d.setHours(18, 0, 0, 0)
  return toLocalInput(d)
}

export function OpenRecordsPage() {
  const [rows, setRows] = useState<OpenRecord[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busyId, setBusyId] = useState<string | null>(null)
  // Per-row chosen check-out, keyed by recordId; seeded lazily from defaultCheckout.
  const [times, setTimes] = useState<Record<string, string>>({})
  // Which rows the batch will close. Empty by default and never "all open days" implicitly: this
  // writes hours people are paid for, so the admin ticks what they are willing to vouch for.
  const [picked, setPicked] = useState<Set<string>>(new Set())
  const [bulkBusy, setBulkBusy] = useState(false)
  const [done, setDone] = useState<string | null>(null)

  useEffect(() => {
    void load()
  }, [])

  async function load() {
    const { status, data } = await getOpenRecords()
    if (status === 200 && Array.isArray(data)) {
      setRows(data)
      setError(null)
    } else if (status === 403) {
      setError('İcazəniz yoxdur')
    } else {
      setError('Yüklənmədi')
    }
  }

  function toggle(id: string) {
    setPicked((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })
  }

  /**
   * Closes every ticked day at its own shift end — the server decides the hour, per employee, per
   * date. One at a time this screen is a chore nobody finishes: the list only grows and every row
   * costs the same three clicks.
   */
  async function closePicked() {
    const ids = [...picked]
    if (ids.length === 0) return
    if (!window.confirm(
      `${ids.length} gün bağlanacaq. Hər gün həmin işçinin öz növbəsinin bitmə saatı ilə yazılacaq. Davam edilsin?`,
    )) return

    setBulkBusy(true)
    setError(null)
    setDone(null)
    const { status, data } = await closeOpenDays(ids)
    setBulkBusy(false)

    if (status !== 200 || !data || 'error' in data) {
      setError('Bağlanmadı')
      return
    }
    setDone(
      data.skipped > 0
        ? `${data.closed} gün bağlandı · ${data.skipped} gün toxunulmadı (bugünkü və ya səlahiyyətdən kənar)`
        : `${data.closed} gün bağlandı`,
    )
    setPicked(new Set())
    await load()
  }

  function timeFor(r: OpenRecord): string {
    return times[r.recordId] ?? defaultCheckout(r.checkInAtUtc)
  }

  async function close(r: OpenRecord) {
    const local = timeFor(r)
    const iso = fromCompanyInputValue(local)
    if (new Date(iso) < new Date(r.checkInAtUtc)) {
      setError('Çıxış girişdən əvvəl ola bilməz')
      return
    }
    if (!window.confirm(`${r.employeeName} · ${fmtDate(r.attendanceDate)} — çıxış vaxtı ${fmtTime(iso)} kimi təyin edilsin?`))
      return
    setBusyId(r.recordId)
    setError(null)
    const { status } = await adminUpdateRecord(r.recordId, undefined, iso)
    if (status === 200) {
      setRows((prev) => (prev ?? []).filter((x) => x.recordId !== r.recordId))
    } else {
      setError('Bağlanmadı')
    }
    setBusyId(null)
  }

  const count = rows?.length ?? 0
  const empty = rows !== null && count === 0

  return (
    <div>
      <div style={{ marginBottom: 14 }}>
        <h1 style={{ fontSize: 18, fontWeight: 800, color: 'var(--c900)' }}>Çıxışı unudulan günlər</h1>
        <div className="muted" style={{ fontSize: 13 }}>
          Giriş edib çıxış etməyən günlər. Çıxış təyin edilməsə, həmin gün hesabatda 0 saat sayılır.
        </div>
      </div>

      {error && (
        <div className="fb fb-err" style={{ marginBottom: 12 }}>
          <IconX />
          <span>{error}</span>
        </div>
      )}

      {done && (
        <div className="fb fb-ok" style={{ marginBottom: 12 }}>
          <IconCheck />
          <span>{done}</span>
        </div>
      )}

      {count > 0 && (
        <div className="card card-pad" style={{ marginBottom: 14, borderColor: '#fde68a', background: '#fffbeb' }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 12, flexWrap: 'wrap' }}>
            <div style={{ flex: 1, minWidth: 220 }}>
              <div style={{ fontWeight: 700, color: '#92400e' }}>
                ⚠️ {count} gün çıxış gözləyir
              </div>
              <div style={{ fontSize: 13, color: 'var(--c600)', marginTop: 2 }}>
                Sətirləri seçib birdəfəlik bağlaya, ya da hər birinin vaxtını əl ilə yoxlayıb «Bağla»
                düyməsinə basa bilərsiniz. Toplu bağlamada hər gün <b>həmin işçinin öz növbəsinin
                bitmə saatı</b> ilə yazılır.
              </div>
            </div>
            <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
              <button
                className="btn btn-sm"
                onClick={() => setPicked(new Set(picked.size === count ? [] : (rows ?? []).map((r) => r.recordId)))}
                disabled={bulkBusy}
              >
                {picked.size === count ? 'Seçimi ləğv et' : `Hamısını seç (${count})`}
              </button>
              <button
                className="btn btn-primary btn-sm"
                onClick={() => void closePicked()}
                disabled={bulkBusy || picked.size === 0}
              >
                <IconCheck /> {bulkBusy ? 'Bağlanır…' : `Seçilmişləri bağla (${picked.size})`}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Who forgets the most — the repeat offenders are a habit problem, not a one-off. Ranked so the
          admin knows exactly who to talk to. Only shown when someone has more than a single open day. */}
      {(() => {
        const byEmployee = new Map<string, { id: string; name: string; location: string; n: number }>()
        for (const r of rows ?? []) {
          const e = byEmployee.get(r.employeeId) ?? { id: r.employeeId, name: r.employeeName, location: r.locationName, n: 0 }
          e.n++
          byEmployee.set(r.employeeId, e)
        }
        const top = Array.from(byEmployee.values()).filter((e) => e.n > 1).sort((a, b) => b.n - a.n).slice(0, 5)
        if (top.length === 0) return null
        return (
          <div className="card card-pad" style={{ marginBottom: 14 }}>
            <div className="card-title">Ən çox unudanlar</div>
            <div className="muted" style={{ fontSize: 12, marginTop: -10, marginBottom: 12 }}>
              Təkrar unudanlar — vərdiş məsələsidir, şəxsən danışmaq ən sürətli yoldur.
            </div>
            <div style={{ display: 'flex', flexDirection: 'column' }}>
              {top.map((e, i) => (
                <div
                  key={e.id}
                  style={{ display: 'flex', alignItems: 'center', gap: 12, padding: '10px 2px', borderBottom: i < top.length - 1 ? '1px solid var(--c50)' : 'none' }}
                >
                  <span
                    style={{ width: 26, height: 26, borderRadius: 8, background: 'var(--c100)', color: 'var(--c600)', fontWeight: 800, fontSize: 12, display: 'flex', alignItems: 'center', justifyContent: 'center', flexShrink: 0 }}
                  >
                    {i + 1}
                  </span>
                  <span style={{ flex: 1, fontWeight: 700, fontSize: 13.5, color: 'var(--c900)' }}>
                    <EmployeeLink id={e.id} name={e.name} />
                  </span>
                  <span className="muted" style={{ fontSize: 12 }}>{e.location}</span>
                  <span style={{ fontWeight: 800, color: 'var(--clay)', fontSize: 14 }}>{e.n} gün</span>
                </div>
              ))}
            </div>
          </div>
        )
      })()}

      <div className="tbl-wrap tbl-cards">
        <table>
          <thead>
            <tr>
              <th style={{ width: 34 }}>
                <input
                  type="checkbox"
                  aria-label="Hamısını seç"
                  checked={count > 0 && picked.size === count}
                  onChange={() => setPicked(new Set(picked.size === count ? [] : (rows ?? []).map((r) => r.recordId)))}
                />
              </th>
              <th>İşçi</th>
              <th>Tarix</th>
              <th>Giriş</th>
              <th>Çıxış vaxtı</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {(rows ?? []).map((r) => (
              <tr key={r.recordId} className={picked.has(r.recordId) ? 'is-picked' : undefined}>
                <td data-label="">
                  <input
                    type="checkbox"
                    aria-label={`${r.employeeName} — seç`}
                    checked={picked.has(r.recordId)}
                    onChange={() => toggle(r.recordId)}
                  />
                </td>
                <td>
                  <div style={{ fontWeight: 700, color: 'var(--c900)' }}><EmployeeLink id={r.employeeId} name={r.employeeName} /></div>
                  <div className="muted" style={{ fontSize: 12 }}>{r.locationName}</div>
                </td>
                <td data-label="Tarix" className="mono">{fmtDate(r.attendanceDate)}</td>
                <td data-label="Giriş" className="mono" style={{ display: 'flex', alignItems: 'center', gap: 4 }}>
                  <IconClock /> {fmtTime(r.checkInAtUtc)}
                </td>
                <td>
                  <input
                    className="inp"
                    type="datetime-local"
                    value={timeFor(r)}
                    onChange={(e) => setTimes((prev) => ({ ...prev, [r.recordId]: e.target.value }))}
                  />
                </td>
                <td data-label="">
                  <button className="btn btn-primary btn-sm" disabled={busyId === r.recordId} onClick={() => close(r)}>
                    <IconCheck /> Bağla
                  </button>
                </td>
              </tr>
            ))}
            {empty && !error && (
              <tr>
                <td colSpan={6} className="muted" style={{ textAlign: 'center', padding: 28 }}>
                  Çıxışı unudulan gün yoxdur 🎉
                </td>
              </tr>
            )}
            {rows === null && !error && (
              <tr>
                <td colSpan={6} className="muted" style={{ textAlign: 'center', padding: 28 }}>
                  Yüklənir…
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  )
}
