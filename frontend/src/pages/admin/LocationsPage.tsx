import { useEffect, useState, type FormEvent } from 'react'
import {
  createLocation,
  deleteLocation,
  getAdminLocations,
  updateLocation,
  type AdminLocation,
  type LocationInput,
} from '../../api/admin'
import { IconCheck, IconMapPin, IconQr, IconTrash, IconX } from '../../components/icons'

type FormState = {
  name: string
  latitude: string
  longitude: string
  radiusMeters: string
  shiftStart: string
  shiftEnd: string
  lateThresholdMinutes: string
}

const EMPTY: FormState = {
  name: '',
  latitude: '40.4093',
  longitude: '49.8671',
  radiusMeters: '150',
  shiftStart: '09:00',
  shiftEnd: '18:00',
  lateThresholdMinutes: '15',
}

const ERRORS: Record<string, string> = {
  NameRequired: 'Ad tələb olunur',
  LatitudeOutOfRange: 'Enlik yanlışdır (-90 … 90)',
  LongitudeOutOfRange: 'Uzunluq yanlışdır (-180 … 180)',
  RadiusMustBePositive: 'Radius müsbət olmalıdır',
  LateThresholdNegative: 'Gecikmə həddi mənfi ola bilməz',
  ShiftStartInvalid: 'Növbə başlama vaxtı yanlışdır',
  ShiftEndInvalid: 'Növbə bitmə vaxtı yanlışdır',
  LocationInUse: 'Bu lokasiya işçilər/qeydlər tərəfindən istifadə olunur — silinə bilməz',
  LocationNotFound: 'Lokasiya tapılmadı',
}

export function LocationsPage() {
  const [rows, setRows] = useState<AdminLocation[]>([])
  const [form, setForm] = useState<FormState>(EMPTY)
  const [editingId, setEditingId] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [ok, setOk] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const [deletingId, setDeletingId] = useState<string | null>(null)
  const [copiedId, setCopiedId] = useState<string | null>(null)

  async function refresh() {
    const { status, data } = await getAdminLocations()
    if (status === 200 && Array.isArray(data)) setRows(data)
    else if (status === 403) setError('İcazəniz yoxdur')
  }

  useEffect(() => {
    void refresh()
  }, [])

  function set<K extends keyof FormState>(key: K, value: string) {
    setForm((f) => ({ ...f, [key]: value }))
  }

  function startCreate() {
    setEditingId(null)
    setForm(EMPTY)
    setError(null)
    setOk(null)
  }

  function startEdit(l: AdminLocation) {
    setEditingId(l.id)
    setForm({
      name: l.name,
      latitude: String(l.latitude),
      longitude: String(l.longitude),
      radiusMeters: String(l.radiusMeters),
      shiftStart: l.shiftStart,
      shiftEnd: l.shiftEnd,
      lateThresholdMinutes: String(l.lateThresholdMinutes),
    })
    setError(null)
    setOk(null)
  }

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    setOk(null)
    setSaving(true)
    const payload: LocationInput = {
      name: form.name.trim(),
      latitude: Number(form.latitude),
      longitude: Number(form.longitude),
      radiusMeters: Number(form.radiusMeters),
      shiftStart: form.shiftStart,
      shiftEnd: form.shiftEnd,
      lateThresholdMinutes: Number(form.lateThresholdMinutes),
    }
    const { status, data } = editingId
      ? await updateLocation(editingId, payload)
      : await createLocation(payload)
    setSaving(false)

    if (status === 200) {
      setOk(editingId ? 'Yeniləndi' : 'Lokasiya əlavə olundu')
      startCreate()
      await refresh()
    } else if (data && typeof data === 'object' && 'error' in data) {
      setError(ERRORS[(data as { error: string }).error] ?? 'Yadda saxlanmadı')
    } else {
      setError('Yadda saxlanmadı')
    }
  }

  async function onDelete(l: AdminLocation) {
    if (!window.confirm(`"${l.name}" lokasiyası silinsin?`)) return
    setError(null)
    setOk(null)
    setDeletingId(l.id)
    const { status, data } = await deleteLocation(l.id)
    setDeletingId(null)
    if (status === 200) {
      if (editingId === l.id) startCreate()
      await refresh()
    } else if (data && typeof data === 'object' && 'error' in data) {
      setError(ERRORS[(data as { error: string }).error] ?? 'Silinmədi')
    } else {
      setError('Silinmədi')
    }
  }

  async function copyKioskLink(l: AdminLocation) {
    const url = `${window.location.origin}/kiosk/${l.id}`
    try {
      await navigator.clipboard.writeText(url)
      setCopiedId(l.id)
      setTimeout(() => setCopiedId((c) => (c === l.id ? null : c)), 2000)
    } catch {
      // Clipboard blocked (e.g. non-secure context) — show the link so it can be copied by hand.
      window.prompt('Kiosk linki (kopyalayın):', url)
    }
  }

  return (
    <div>
      <div className="fb" style={{ marginBottom: 16, background: 'var(--c50, #f6f8f4)', color: 'var(--c500)' }}>
        <IconQr />
        <span>
          <b>Kiosk</b> — lokasiyada fırlanan QR göstərən ekran (giriş tələb etmir). Cədvəldəki{' '}
          <b>Kiosk</b> düyməsi ilə açıb tablet/monitorda tam ekran işlədin; işçilər öz telefonu ilə skan edir.
        </span>
      </div>

      <form onSubmit={onSubmit} className="card card-pad" style={{ marginBottom: 16, maxWidth: 760 }}>
        <div style={{ fontWeight: 700, color: 'var(--c900)', marginBottom: 14, display: 'flex', alignItems: 'center', gap: 8 }}>
          <IconMapPin />
          {editingId ? 'Lokasiyanı redaktə et' : 'Yeni lokasiya'}
        </div>

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

        <div className="form-row">
          <label className="form-label">Ad</label>
          <input className="inp" required value={form.name} onChange={(e) => set('name', e.target.value)} placeholder="məs. Baş ofis" />
        </div>

        <div className="form-row cols2">
          <div>
            <label className="form-label">Enlik (latitude)</label>
            <input className="inp" type="number" step="any" required value={form.latitude} onChange={(e) => set('latitude', e.target.value)} />
          </div>
          <div>
            <label className="form-label">Uzunluq (longitude)</label>
            <input className="inp" type="number" step="any" required value={form.longitude} onChange={(e) => set('longitude', e.target.value)} />
          </div>
        </div>

        <div className="form-row cols2">
          <div>
            <label className="form-label">Radius (metr)</label>
            <input className="inp" type="number" min="1" required value={form.radiusMeters} onChange={(e) => set('radiusMeters', e.target.value)} />
          </div>
          <div>
            <label className="form-label">Gecikmə həddi (dəqiqə)</label>
            <input className="inp" type="number" min="0" required value={form.lateThresholdMinutes} onChange={(e) => set('lateThresholdMinutes', e.target.value)} />
          </div>
        </div>

        <div className="form-row cols2">
          <div>
            <label className="form-label">Növbə başlama</label>
            <input className="inp" type="time" required value={form.shiftStart} onChange={(e) => set('shiftStart', e.target.value)} />
          </div>
          <div>
            <label className="form-label">Növbə bitmə</label>
            <input className="inp" type="time" required value={form.shiftEnd} onChange={(e) => set('shiftEnd', e.target.value)} />
          </div>
        </div>

        <div style={{ display: 'flex', gap: 8 }}>
          <button type="submit" className="btn btn-primary" disabled={saving}>
            <IconCheck />
            {saving ? 'Yadda saxlanır…' : editingId ? 'Yadda saxla' : 'Əlavə et'}
          </button>
          {editingId && (
            <button type="button" className="btn" onClick={startCreate} disabled={saving}>
              Ləğv et
            </button>
          )}
        </div>
      </form>

      <div className="tbl-wrap">
        <table>
          <thead>
            <tr>
              <th>Ad</th>
              <th>Koordinat</th>
              <th className="num">Radius</th>
              <th>Növbə</th>
              <th className="num">Gecikmə</th>
              <th style={{ textAlign: 'right' }}>Əməliyyat</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((l) => (
              <tr key={l.id}>
                <td style={{ fontWeight: 700, color: 'var(--c900)' }}>{l.name}</td>
                <td className="mono">{l.latitude.toFixed(4)}, {l.longitude.toFixed(4)}</td>
                <td className="num mono">{l.radiusMeters} m</td>
                <td className="mono">{l.shiftStart}–{l.shiftEnd}</td>
                <td className="num mono">{l.lateThresholdMinutes} dəq</td>
                <td>
                  <div style={{ display: 'flex', gap: 8, justifyContent: 'flex-end' }}>
                    <a
                      className="btn btn-sm"
                      href={`/kiosk/${l.id}`}
                      target="_blank"
                      rel="noopener noreferrer"
                      title="Bu lokasiyanın kiosk QR ekranını yeni tabda aç"
                    >
                      <IconQr /> Kiosk aç
                    </a>
                    <button
                      className="btn btn-sm"
                      onClick={() => copyKioskLink(l)}
                      title="Kiosk linkini kopyala"
                    >
                      {copiedId === l.id ? 'Kopyalandı ✓' : 'Linki kopyala'}
                    </button>
                    <button className="btn btn-sm" onClick={() => startEdit(l)}>Redaktə</button>
                    <button className="btn btn-danger btn-sm" disabled={deletingId === l.id} onClick={() => onDelete(l)}>
                      <IconTrash /> Sil
                    </button>
                  </div>
                </td>
              </tr>
            ))}
            {rows.length === 0 && (
              <tr>
                <td colSpan={6} className="muted" style={{ textAlign: 'center', padding: 28 }}>
                  Hələ lokasiya yoxdur — yuxarıdan əlavə edin
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  )
}
