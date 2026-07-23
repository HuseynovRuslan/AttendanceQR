import { useEffect, useState, type FormEvent } from 'react'
import { addNonWorkingDay, deleteNonWorkingDay, getNonWorkingDays, type NonWorkingDay } from '../../api/calendar'
import { getAdminLocations, type AdminLocation } from '../../api/admin'
import { IconCheck, IconTrash, IconX } from '../../components/icons'
import { fmtDate } from '../../lib/format'

const ERRORS: Record<string, string> = {
  DescriptionRequired: 'Təsvir tələb olunur',
  LocationNotFound: 'Lokasiya tapılmadı',
}

export function NonWorkingDaysPage() {
  const [rows, setRows] = useState<NonWorkingDay[]>([])
  const [locations, setLocations] = useState<AdminLocation[]>([])
  const [date, setDate] = useState('')
  const [description, setDescription] = useState('')
  const [locationId, setLocationId] = useState<string>('')
  const [error, setError] = useState<string | null>(null)
  const [ok, setOk] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const [deletingId, setDeletingId] = useState<string | null>(null)

  async function refresh() {
    const [daysRes, locsRes] = await Promise.all([getNonWorkingDays(), getAdminLocations()])
    if (daysRes.status === 200 && Array.isArray(daysRes.data)) setRows(daysRes.data)
    if (locsRes.status === 200 && Array.isArray(locsRes.data)) setLocations(locsRes.data)
  }

  useEffect(() => {
    void refresh()
  }, [])

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    setOk(null)
    setSaving(true)
    const { status, data } = await addNonWorkingDay(date, description.trim(), locationId || null)
    setSaving(false)

    if (status === 200) {
      setOk('Qeyri-iş günü əlavə olundu')
      setDate('')
      setDescription('')
      setLocationId('')
      await refresh()
    } else if (data && typeof data === 'object' && 'error' in data) {
      setError(ERRORS[(data as { error: string }).error] ?? 'Yadda saxlanmadı')
    } else {
      setError('Yadda saxlanmadı')
    }
  }

  async function onDelete(d: NonWorkingDay) {
    if (!window.confirm(`"${d.description}" (${fmtDate(d.date)}) silinsin?`)) return
    setError(null)
    setOk(null)
    setDeletingId(d.id)
    const { status } = await deleteNonWorkingDay(d.id)
    setDeletingId(null)
    if (status === 200) await refresh()
    else setError('Silinmədi')
  }

  return (
    <div>
      <div className="fb" style={{ marginBottom: 16, background: 'var(--c50, #f6f8f4)', color: 'var(--c500)' }}>
        <span>
          Bu siyahıdakı tarixlərdə heç kim <b>"Qayıb"</b> sayılmır — status <b>"İstirahət"</b> olur.
          Lokasiya seçilməzsə (Hamısı), bütün lokasiyalara aiddir.
        </span>
      </div>

      <form onSubmit={onSubmit} className="card card-pad" style={{ marginBottom: 16, maxWidth: 640 }}>
        <div style={{ fontWeight: 700, color: 'var(--c900)', marginBottom: 14 }}>Yeni qeyri-iş günü</div>

        {error && (
          <div className="fb fb-err" style={{ marginBottom: 14 }}>
            <IconX />
            <span>{error}</span>
          </div>
        )}
        {ok && (
          <div className="fb fb-ok" style={{ marginBottom: 14 }}>
            <IconCheck />
            <span>{ok}</span>
          </div>
        )}

        <div className="form-row cols2">
          <div>
            <label className="form-label">Tarix</label>
            <input className="inp" type="date" required value={date} onChange={(e) => setDate(e.target.value)} />
          </div>
          <div>
            <label className="form-label">Lokasiya</label>
            <select className="inp" value={locationId} onChange={(e) => setLocationId(e.target.value)}>
              <option value="">Hamısı</option>
              {locations.map((l) => (
                <option key={l.id} value={l.id}>{l.name}</option>
              ))}
            </select>
          </div>
        </div>

        <div className="form-row">
          <label className="form-label">Təsvir</label>
          <input
            className="inp"
            required
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            placeholder="məs. Novruz bayramı"
          />
        </div>

        <button type="submit" className="btn btn-primary" disabled={saving}>
          <IconCheck />
          {saving ? 'Yadda saxlanır…' : 'Əlavə et'}
        </button>
      </form>

      <div className="tbl-wrap tbl-cards">
        <table>
          <thead>
            <tr>
              <th>Tarix</th>
              <th>Təsvir</th>
              <th>Lokasiya</th>
              <th style={{ textAlign: 'right' }}>Əməliyyat</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((d) => (
              <tr key={d.id}>
                <td data-label="Tarix" className="mono">{fmtDate(d.date)}</td>
                <td data-label="Təsvir">{d.description}</td>
                <td data-label="Lokasiya">{d.locationName ?? 'Hamısı'}</td>
                <td data-label="">
                  <button className="btn btn-danger btn-sm" disabled={deletingId === d.id} onClick={() => onDelete(d)}>
                    <IconTrash /> Sil
                  </button>
                </td>
              </tr>
            ))}
            {rows.length === 0 && (
              <tr>
                <td colSpan={4} className="muted" style={{ textAlign: 'center', padding: 28 }}>
                  Hələ qeyri-iş günü yoxdur
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  )
}

