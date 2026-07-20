import { useEffect, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import {
  createLocation,
  createSchedule,
  deleteLocation,
  getAdminLocations,
  getSchedules,
  setLocationActive,
  updateLocation,
  type AdminLocation,
  type LocationInput,
  type Schedule,
} from '../../api/admin'
import { IconCheck, IconMapPin, IconQr, IconTrash, IconX } from '../../components/icons'
import { LocationMapPicker } from '../../components/LocationMapPicker'

type FormState = {
  name: string
  latitude: string
  longitude: string
  radiusMeters: string
  shiftStart: string
  shiftEnd: string
  lateThresholdMinutes: string
  workDaysMask: number
}

// Index = JS Date.getDay() / .NET DayOfWeek (Sunday=0 ... Saturday=6) — same convention as the
// WorkDaysMask bit position, so no offset translation is needed anywhere.
const WEEKDAY_LABELS = ['Bazar', 'B.e', 'Ç.a', 'Çər', 'C.a', 'Cümə', 'Şən']
const DEFAULT_WORK_DAYS_MASK = 126 // every day except Sunday

const EMPTY: FormState = {
  name: '',
  latitude: '40.4093',
  longitude: '49.8671',
  radiusMeters: '150',
  shiftStart: '09:00',
  shiftEnd: '18:00',
  lateThresholdMinutes: '15',
  workDaysMask: DEFAULT_WORK_DAYS_MASK,
}

const ERRORS: Record<string, string> = {
  NameRequired: 'Ad tələb olunur',
  LatitudeOutOfRange: 'Enlik yanlışdır (-90 … 90)',
  LongitudeOutOfRange: 'Uzunluq yanlışdır (-180 … 180)',
  RadiusMustBePositive: 'Radius müsbət olmalıdır',
  LateThresholdNegative: 'Gecikmə həddi mənfi ola bilməz',
  ShiftStartInvalid: 'Növbə başlama vaxtı yanlışdır',
  ShiftEndInvalid: 'Növbə bitmə vaxtı yanlışdır',
  LocationInUse: 'Bu filial istifadə olunur — silinə bilməz',
  LocationNotFound: 'Lokasiya tapılmadı',
  WorkDaysMaskInvalid: 'İş günləri seçimi yanlışdır',
}

/** Why this branch will not delete, and what to do about it — the two causes have different answers.
 *  Staff can be moved and the branch then deletes; history cannot, so that branch is deactivated
 *  instead. Saying only "it is in use" left an admin hunting for staff they could not see. */
function inUseMessage(name: string, employees: number, history: number): string {
  if (history > 0) {
    return `«${name}» filialında ${history} davamiyyət qeydi var — silinsə tarixçə itərdi. ` +
      'Əvəzinə «Dayandır» ilə deaktiv edin: kiosk QR bağlanır, məlumat qalır.'
  }
  return `«${name}» filialında ${employees} işçi var. Əvvəlcə onları başqa filiala köçürün ` +
    '(İşçilər → redaktə → Filial), sonra silin. Özünüz də o filialda ola bilərsiniz.'
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
  const [togglingId, setTogglingId] = useState<string | null>(null)
  // The form (with the big map) is hidden by default so the list is what you see first; "Əlavə et" or
  // a row's "Redaktə" opens it.
  const [showForm, setShowForm] = useState(false)

  // Schedule (qrafik) library — reusable shift templates. Picking one fills the shift fields; the
  // location still stores its own copy, so schedules are a convenience, not a live link.
  const [schedules, setSchedules] = useState<Schedule[]>([])

  async function refresh() {
    const { status, data } = await getAdminLocations()
    if (status === 200 && Array.isArray(data)) setRows(data)
    else if (status === 403) setError('İcazəniz yoxdur')
  }

  async function loadSchedules() {
    const { status, data } = await getSchedules()
    if (status === 200 && Array.isArray(data)) setSchedules(data)
  }

  useEffect(() => {
    void refresh()
    void loadSchedules()
  }, [])

  function applySchedule(s: Schedule) {
    setForm((f) => ({
      ...f,
      shiftStart: s.shiftStart,
      shiftEnd: s.shiftEnd,
      lateThresholdMinutes: String(s.lateThresholdMinutes),
      workDaysMask: s.workDaysMask,
    }))
  }

  async function saveCurrentAsSchedule() {
    const name = window.prompt('Bu qrafikin adı (məs. "Gecə növbəsi"):')?.trim()
    if (!name) return
    const { status, data } = await createSchedule({
      name,
      shiftStart: form.shiftStart,
      shiftEnd: form.shiftEnd,
      lateThresholdMinutes: Number(form.lateThresholdMinutes) || 15,
      workDaysMask: form.workDaysMask,
    })
    if (status === 200 && data && !('error' in data)) {
      await loadSchedules()
      setOk(`"${name}" qrafiki yadda saxlanıldı`)
    } else {
      setError('Qrafik saxlanılmadı')
    }
  }

  function set<K extends keyof FormState>(key: K, value: FormState[K]) {
    setForm((f) => ({ ...f, [key]: value }))
  }

  function toggleWorkDay(dayIndex: number) {
    setForm((f) => ({ ...f, workDaysMask: f.workDaysMask ^ (1 << dayIndex) }))
  }

  function startCreate() {
    setEditingId(null)
    setForm(EMPTY)
    setError(null)
    setOk(null)
    setShowForm(true)
  }

  function closeForm() {
    setEditingId(null)
    setForm(EMPTY)
    setShowForm(false)
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
      workDaysMask: l.workDaysMask,
    })
    setError(null)
    setOk(null)
    setShowForm(true)
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
      workDaysMask: form.workDaysMask,
    }
    const { status, data } = editingId
      ? await updateLocation(editingId, payload)
      : await createLocation(payload)
    setSaving(false)

    if (status === 200) {
      setOk(editingId ? 'Yeniləndi' : 'Lokasiya əlavə olundu')
      closeForm()
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
      if (editingId === l.id) closeForm()
      await refresh()
    } else if (data && typeof data === 'object' && 'error' in data) {
      const d = data as { error: string; employeeCount?: number; historyCount?: number }
      setError(d.error === 'LocationInUse' ? inUseMessage(l.name, d.employeeCount ?? 0, d.historyCount ?? 0) : ERRORS[d.error] ?? 'Silinmədi')
    } else {
      setError('Silinmədi')
    }
  }

  async function toggleActive(l: AdminLocation) {
    setError(null)
    setOk(null)
    setTogglingId(l.id)
    const { status } = await setLocationActive(l.id, !l.isActive)
    setTogglingId(null)
    if (status === 200) {
      setOk(l.isActive ? 'Lokasiya deaktiv edildi' : 'Lokasiya aktiv edildi')
      await refresh()
    } else {
      setError('Status dəyişmədi')
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

  const mapLat = Number(form.latitude)
  const mapLng = Number(form.longitude)

  return (
    <div>
      <div className="fb" style={{ marginBottom: 16, background: 'var(--c50, #f6f8f4)', color: 'var(--c500)' }}>
        <IconQr />
        <span>
          <b>Kiosk</b> — lokasiyada fırlanan QR göstərən ekran (giriş tələb etmir). Cədvəldəki{' '}
          <b>Kiosk</b> düyməsi ilə açıb tablet/monitorda tam ekran işlədin; işçilər öz telefonu ilə skan edir.
        </span>
      </div>

      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 12, gap: 12, flexWrap: 'wrap' }}>
        <div className="card-title" style={{ marginBottom: 0 }}>Lokasiyalar</div>
        <button className="btn btn-primary" onClick={showForm ? closeForm : startCreate}>
          {showForm ? 'Ləğv et' : '＋ Lokasiya əlavə et'}
        </button>
      </div>

      {showForm && (
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

        <div className="form-row">
          <label className="form-label">Yer və radius</label>
          <p className="muted" style={{ fontSize: 12, marginTop: -4, marginBottom: 8 }}>
            Xəritədə iş yerinin üstünə klikləyin — koordinat avtomatik dolur. Dairə "check-in qəbul
            olunan" sahəni göstərir. İstəsəniz koordinatı aşağıda əl ilə də yaza bilərsiniz.
          </p>
          {Number.isFinite(mapLat) && Number.isFinite(mapLng) && (
            <LocationMapPicker
              latitude={mapLat}
              longitude={mapLng}
              radiusMeters={Number(form.radiusMeters) || 0}
              onPick={(lat, lng) => {
                set('latitude', lat.toFixed(6))
                set('longitude', lng.toFixed(6))
              }}
            />
          )}
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

        {/* Schedule picker — pick a saved template to fill the hours, or fine-tune below and save the
            result as a new schedule. Templates only; the location keeps its own copy of the times. */}
        <div style={{ marginBottom: 12 }}>
          <label className="form-label">Qrafik</label>
          <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', alignItems: 'center' }}>
            <select
              className="inp"
              style={{ flex: '1 1 220px' }}
              value=""
              onChange={(e) => {
                const s = schedules.find((x) => x.id === e.target.value)
                if (s) applySchedule(s)
              }}
            >
              <option value="">Qrafik seçin (saatları doldurur)…</option>
              {schedules.map((s) => (
                <option key={s.id} value={s.id}>
                  {s.isOvernight ? '🌙 ' : ''}{s.name} ({s.shiftStart}–{s.shiftEnd})
                </option>
              ))}
            </select>
            <button type="button" className="btn btn-sm" onClick={() => void saveCurrentAsSchedule()}>
              ＋ Aşağıdakını qrafik kimi saxla
            </button>
          </div>
          <div className="muted" style={{ fontSize: 11, marginTop: 4 }}>
            Qrafik seçmək saatları doldurur; istəsəniz aşağıda dəyişə bilərsiniz. Qrafiki dəyişmək köhnə
            lokasiyalara təsir etmir.
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
        {/* Overnight shift = end earlier than start; the system detects and handles it automatically. */}
        {form.shiftEnd < form.shiftStart && (
          <div className="hint" style={{ color: 'var(--leaf-d)', fontSize: 12, marginTop: -6 }}>
            🌙 Gecə növbəsi: bitmə başlanğıcdan tezdir, yəni növbə gecə yarısını keçir (məs. 22:00–06:00).
            Səhər çıxışı avtomatik dünənki növbəni bağlayacaq.
          </div>
        )}

        <div className="form-row">
          <label className="form-label">İş günləri</label>
          <p className="muted" style={{ fontSize: 12, marginTop: -4, marginBottom: 8 }}>
            İşarələnməmiş günlərdə giriş olmasa "Qayıb" yox, "İstirahət" göstərilir.
          </p>
          <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
            {WEEKDAY_LABELS.map((label, i) => {
              const active = (form.workDaysMask & (1 << i)) !== 0
              return (
                <button
                  key={i}
                  type="button"
                  onClick={() => toggleWorkDay(i)}
                  className="btn btn-sm"
                  style={active ? { background: 'var(--leaf)', borderColor: 'var(--leaf)', color: '#fff' } : undefined}
                >
                  {label}
                </button>
              )
            })}
          </div>
        </div>

        <div style={{ display: 'flex', gap: 8 }}>
          <button type="submit" className="btn btn-primary" disabled={saving}>
            <IconCheck />
            {saving ? 'Yadda saxlanır…' : editingId ? 'Yadda saxla' : 'Əlavə et'}
          </button>
          <button type="button" className="btn" onClick={closeForm} disabled={saving}>
            Ləğv et
          </button>
        </div>
      </form>
      )}

      <div className="tbl-wrap">
        <table>
          <thead>
            <tr>
              <th>Ad</th>
              <th>Koordinat</th>
              <th className="num">Radius</th>
              <th>Növbə</th>
              <th className="num">Gecikmə</th>
              <th>Status</th>
              <th style={{ textAlign: 'right' }}>Əməliyyat</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((l) => (
              <tr key={l.id} style={{ opacity: l.isActive ? 1 : 0.55 }}>
                <td style={{ fontWeight: 700, color: 'var(--c900)' }}>{l.name}</td>
                <td className="mono">{l.latitude.toFixed(4)}, {l.longitude.toFixed(4)}</td>
                <td className="num mono">{l.radiusMeters} m</td>
                <td className="mono">{l.shiftStart}–{l.shiftEnd}</td>
                <td className="num mono">{l.lateThresholdMinutes} dəq</td>
                <td>
                  <span
                    style={{
                      display: 'inline-block',
                      padding: '2px 10px',
                      borderRadius: 999,
                      fontSize: 12,
                      fontWeight: 700,
                      color: l.isActive ? '#2e7d32' : '#9a3412',
                      background: l.isActive ? 'rgba(124,179,66,0.15)' : 'rgba(154,52,18,0.12)',
                    }}
                  >
                    {l.isActive ? 'Aktiv' : 'Deaktiv'}
                  </span>
                </td>
                <td>
                  <div style={{ display: 'flex', gap: 8, justifyContent: 'flex-end', flexWrap: 'wrap' }}>
                    {l.isActive && (
                      <>
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
                        <Link
                          className="btn btn-sm"
                          to={`/admin/locations/${l.id}/print-qr`}
                          title="Çap üçün sabit QR (PNG/PDF)"
                        >
                          <IconQr /> Çap üçün QR
                        </Link>
                      </>
                    )}
                    <button
                      className="btn btn-sm"
                      disabled={togglingId === l.id}
                      onClick={() => toggleActive(l)}
                      title={l.isActive ? 'Kiosku dayandır (məlumat silinmir)' : 'Yenidən aktiv et'}
                    >
                      {l.isActive ? 'Deaktiv et' : 'Aktiv et'}
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
                <td colSpan={7} className="muted" style={{ textAlign: 'center', padding: 28 }}>
                  Hələ lokasiya yoxdur — «Lokasiya əlavə et» ilə başlayın
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  )
}
