import { useEffect, useMemo, useState } from 'react'
import {
  ASSET_STATUS_LABEL,
  ASSET_TYPES,
  ASSET_TYPE_LABEL,
  assignAsset,
  createAsset,
  deleteAsset,
  getAssetSummary,
  getAssets,
  returnAsset,
  updateAsset,
  type Asset,
  type AssetInput,
  type AssetStatus,
  type AssetSummary,
  type AssetType,
} from '../../api/assets'
import { getAdminLocations, getEmployees, type AdminEmployee, type AdminLocation } from '../../api/admin'
import { IconCheck, IconX } from '../../components/icons'

/**
 * The company's computer equipment and who holds it.
 *
 * The register answers two questions and is laid out around them: "what does the company own" (the
 * counts and the list) and "who has inventory number 000431" (the search box, which the backend
 * resolves against inventory number, name, serial, brand and model at once — nobody remembers which
 * field they wrote the number in).
 *
 * Assigning is its own action rather than a dropdown in the edit form. Handing a laptop to someone is
 * a different act from fixing a typo in its serial number, and one form that does both is how a
 * mistyped edit silently reassigns a device.
 */

const ERRORS: Record<string, string> = {
  InventoryNumberRequired: 'İnventar nömrəsi lazımdır',
  NameRequired: 'Ad lazımdır',
  InvalidType: 'Növ seçilməyib',
  InventoryNumberExists: 'Bu inventar nömrəsi artıq var',
  LocationNotFound: 'Filial tapılmadı',
  AssetNotFound: 'Avadanlıq tapılmadı',
  AssetWrittenOff: 'Silinmiş avadanlıq təhkim edilə bilməz',
  AssetNotAssigned: 'Bu avadanlıq heç kimə təhkim olunmayıb',
  AssetAssigned: 'Əvvəlcə avadanlığı geri qaytarın',
  AssignInstead: '«Təhkim olunub» statusu təhkim düyməsi ilə verilir',
  EmployeeNotFound: 'İşçi tapılmadı',
  EmployeeInactive: 'Deaktiv işçiyə təhkim etmək olmaz',
}

const STATUS_CLASS: Record<AssetStatus, string> = {
  Assigned: 'badge b-present',
  InStock: 'badge b-permission',
  InRepair: 'badge b-late',
  WrittenOff: 'badge b-absent',
}

const EMPTY_FORM: AssetInput = {
  inventoryNumber: '',
  type: 'Laptop',
  name: '',
  brand: null,
  model: null,
  serialNumber: null,
  purchaseDate: null,
  purchasePrice: null,
  status: 'InStock',
  locationId: null,
  notes: null,
}

export function AssetsPage() {
  const [rows, setRows] = useState<Asset[]>([])
  const [summary, setSummary] = useState<AssetSummary | null>(null)
  const [employees, setEmployees] = useState<AdminEmployee[]>([])
  const [locations, setLocations] = useState<AdminLocation[]>([])
  const [loaded, setLoaded] = useState(false)
  const [busy, setBusy] = useState(false)
  const [msg, setMsg] = useState<string | null>(null)
  const [err, setErr] = useState<string | null>(null)

  // Filters. `search` is what is typed; `q` is what has been sent — debounced so a full list isn't
  // refetched on every keystroke.
  const [search, setSearch] = useState('')
  const [q, setQ] = useState('')
  const [type, setType] = useState<AssetType | ''>('')
  const [status, setStatus] = useState<AssetStatus | ''>('')
  const [holder, setHolder] = useState('')

  const [showForm, setShowForm] = useState(false)
  const [editingId, setEditingId] = useState<string | null>(null)
  const [form, setForm] = useState<AssetInput>(EMPTY_FORM)

  // Which row's "assign to…" picker is open, and who is picked in it.
  const [assigning, setAssigning] = useState<string | null>(null)
  const [assignTo, setAssignTo] = useState('')

  useEffect(() => {
    const t = setTimeout(() => setQ(search.trim()), 300)
    return () => clearTimeout(t)
  }, [search])

  async function load() {
    const [list, stats] = await Promise.all([
      getAssets({ q, type, status, employeeId: holder || undefined }),
      getAssetSummary(),
    ])
    if (list.status === 200 && Array.isArray(list.data)) setRows(list.data)
    if (stats.status === 200 && stats.data) setSummary(stats.data)
    setLoaded(true)
  }

  useEffect(() => { void load() }, [q, type, status, holder])

  useEffect(() => {
    void getEmployees().then((r) => {
      if (r.status === 200 && Array.isArray(r.data)) setEmployees(r.data)
    })
    void getAdminLocations().then((r) => {
      if (r.status === 200 && Array.isArray(r.data)) setLocations(r.data)
    })
  }, [])

  // Deactivated staff still appear in the filter — equipment does not come back on its own when an
  // account is switched off, and those are exactly the rows someone needs to chase.
  const activeEmployees = useMemo(
    () => employees.filter((e) => e.isActive).sort((a, b) => a.fullName.localeCompare(b.fullName, 'az')),
    [employees],
  )
  const holderOptions = useMemo(
    () => [...employees].sort((a, b) => a.fullName.localeCompare(b.fullName, 'az')),
    [employees],
  )

  function set<K extends keyof AssetInput>(key: K, value: AssetInput[K]) {
    setForm((f) => ({ ...f, [key]: value }))
  }

  function fail(data: unknown, fallback: string) {
    const code = data && typeof data === 'object' && 'error' in data ? String((data as { error: string }).error) : ''
    setErr(ERRORS[code] ?? fallback)
  }

  function openNew() {
    setForm(EMPTY_FORM)
    setEditingId(null)
    setShowForm(true)
    setMsg(null)
    setErr(null)
  }

  function openEdit(row: Asset) {
    setForm({
      inventoryNumber: row.inventoryNumber,
      type: row.type,
      name: row.name,
      brand: row.brand,
      model: row.model,
      serialNumber: row.serialNumber,
      purchaseDate: row.purchaseDate,
      purchasePrice: row.purchasePrice,
      status: row.status,
      locationId: row.locationId,
      notes: row.notes,
    })
    setEditingId(row.id)
    setShowForm(true)
    setMsg(null)
    setErr(null)
  }

  async function submit(e: React.FormEvent) {
    e.preventDefault()
    setBusy(true)
    setErr(null)
    const { status: code, data } = editingId ? await updateAsset(editingId, form) : await createAsset(form)
    setBusy(false)
    if (code === 200) {
      setShowForm(false)
      setEditingId(null)
      setMsg(editingId ? 'Avadanlıq yeniləndi' : `«${form.name}» əlavə edildi`)
      void load()
    } else {
      fail(data, 'Yadda saxlanmadı')
    }
  }

  async function assign(row: Asset) {
    if (!assignTo) return
    setBusy(true)
    setErr(null)
    const { status: code, data } = await assignAsset(row.id, assignTo)
    setBusy(false)
    if (code === 200) {
      const who = employees.find((e) => e.id === assignTo)?.fullName ?? 'işçi'
      setAssigning(null)
      setAssignTo('')
      setMsg(`«${row.name}» ${who} adına təhkim edildi`)
      void load()
    } else {
      fail(data, 'Təhkim edilmədi')
    }
  }

  async function giveBack(row: Asset) {
    if (!window.confirm(`«${row.name}» ${row.assignedEmployeeName ?? 'işçi'}-dən geri alınsın?`)) return
    setBusy(true)
    setErr(null)
    const { status: code, data } = await returnAsset(row.id)
    setBusy(false)
    if (code === 200) {
      setMsg(`«${row.name}» anbara qaytarıldı`)
      void load()
    } else {
      fail(data, 'Qaytarılmadı')
    }
  }

  async function remove(row: Asset) {
    if (!window.confirm(
      `«${row.name}» (${row.inventoryNumber}) siyahıdan tamamilə silinsin?\n\nİstifadədən çıxan avadanlıq üçün silmək əvəzinə statusu «Silinib» edin — belədə qeyd siyahıda qalır.`,
    )) return
    setBusy(true)
    setErr(null)
    const { status: code, data } = await deleteAsset(row.id)
    setBusy(false)
    if (code === 200) {
      setMsg(`«${row.name}» silindi`)
      void load()
    } else {
      fail(data, 'Silinmədi')
    }
  }

  const filtered = q || type || status || holder

  return (
    <div>
      <div className="stat-grid">
        <div className="stat-card blue">
          <div className="stat-lbl">Ümumi</div>
          <div className="stat-val">{summary?.total ?? '—'}</div>
          <div className="stat-sub">qeydiyyatdakı avadanlıq</div>
        </div>
        <div className="stat-card leaf">
          <div className="stat-lbl">Təhkim olunub</div>
          <div className="stat-val">{summary?.assigned ?? '—'}</div>
          <div className="stat-sub">işçilərin üzərində</div>
        </div>
        <div className="stat-card">
          <div className="stat-lbl">Anbarda</div>
          <div className="stat-val">{summary?.inStock ?? '—'}</div>
          <div className="stat-sub">boşdur, verilə bilər</div>
        </div>
        <div className="stat-card amber">
          <div className="stat-lbl">Təmirdə</div>
          <div className="stat-val">{summary?.inRepair ?? '—'}</div>
          <div className="stat-sub">istifadədə deyil</div>
        </div>
        <div className="stat-card clay">
          <div className="stat-lbl">Silinib</div>
          <div className="stat-val">{summary?.writtenOff ?? '—'}</div>
          <div className="stat-sub">balansdan çıxarılıb</div>
        </div>
      </div>

      <div className="card card-pad" style={{ marginBottom: 16 }}>
        <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', alignItems: 'center' }}>
          <input
            className="inp"
            style={{ flex: '1 1 240px', minWidth: 200 }}
            placeholder="İnventar N, ad, seriya N, marka…"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
          <select className="inp" style={{ maxWidth: 200 }} value={type} onChange={(e) => setType(e.target.value as AssetType | '')}>
            <option value="">Bütün növlər</option>
            {ASSET_TYPES.map((t) => <option key={t} value={t}>{ASSET_TYPE_LABEL[t]}</option>)}
          </select>
          <select className="inp" style={{ maxWidth: 180 }} value={status} onChange={(e) => setStatus(e.target.value as AssetStatus | '')}>
            <option value="">Bütün statuslar</option>
            {(Object.keys(ASSET_STATUS_LABEL) as AssetStatus[]).map((s) => (
              <option key={s} value={s}>{ASSET_STATUS_LABEL[s]}</option>
            ))}
          </select>
          <select className="inp" style={{ maxWidth: 220 }} value={holder} onChange={(e) => setHolder(e.target.value)}>
            <option value="">Bütün işçilər</option>
            {holderOptions.map((e) => (
              <option key={e.id} value={e.id}>{e.fullName}{e.isActive ? '' : ' (deaktiv)'}</option>
            ))}
          </select>
          <button className="btn btn-primary" onClick={openNew}>Yeni avadanlıq</button>
        </div>
        {msg && <div className="fb fb-ok" style={{ marginTop: 12 }}><IconCheck /><span>{msg}</span></div>}
        {err && <div className="fb fb-err" style={{ marginTop: 12 }}><IconX /><span>{err}</span></div>}
      </div>

      {showForm && (
        <form onSubmit={submit} className="card card-pad" style={{ marginBottom: 16, maxWidth: 760 }}>
          <div style={{ fontWeight: 700, color: 'var(--c900)', marginBottom: 14 }}>
            {editingId ? 'Avadanlığı redaktə et' : 'Yeni avadanlıq'}
          </div>

          <div className="form-row cols2">
            <div>
              <label className="form-label">İnventar nömrəsi</label>
              <input
                className="inp"
                required
                placeholder="məs. INV-000431"
                value={form.inventoryNumber}
                onChange={(e) => set('inventoryNumber', e.target.value)}
              />
              <div className="muted" style={{ fontSize: 11, marginTop: 4 }}>
                Avadanlığın üzərindəki etiketdə yazılan nömrə. Şirkət daxilində təkrarlana bilməz.
              </div>
            </div>
            <div>
              <label className="form-label">Növ</label>
              <select className="inp" value={form.type} onChange={(e) => set('type', e.target.value as AssetType)}>
                {ASSET_TYPES.map((t) => <option key={t} value={t}>{ASSET_TYPE_LABEL[t]}</option>)}
              </select>
            </div>
          </div>

          <div className="form-row cols2">
            <div>
              <label className="form-label">Ad</label>
              <input
                className="inp"
                required
                placeholder="məs. Dell Latitude 5420"
                value={form.name}
                onChange={(e) => set('name', e.target.value)}
              />
            </div>
            <div>
              <label className="form-label">Seriya nömrəsi</label>
              <input
                className="inp"
                value={form.serialNumber ?? ''}
                onChange={(e) => set('serialNumber', e.target.value || null)}
              />
            </div>
          </div>

          <div className="form-row cols2">
            <div>
              <label className="form-label">Marka</label>
              <input className="inp" value={form.brand ?? ''} onChange={(e) => set('brand', e.target.value || null)} />
            </div>
            <div>
              <label className="form-label">Model</label>
              <input className="inp" value={form.model ?? ''} onChange={(e) => set('model', e.target.value || null)} />
            </div>
          </div>

          <div className="form-row cols2">
            <div>
              <label className="form-label">Alış tarixi</label>
              <input
                className="inp"
                type="date"
                value={form.purchaseDate ?? ''}
                onChange={(e) => set('purchaseDate', e.target.value || null)}
              />
            </div>
            <div>
              <label className="form-label">Alış qiyməti (AZN)</label>
              <input
                className="inp"
                type="number"
                min="0"
                step="0.01"
                value={form.purchasePrice ?? ''}
                onChange={(e) => set('purchasePrice', e.target.value === '' ? null : Number(e.target.value))}
              />
            </div>
          </div>

          <div className="form-row cols2">
            <div>
              <label className="form-label">Filial</label>
              <select
                className="inp"
                value={form.locationId ?? ''}
                onChange={(e) => set('locationId', e.target.value || null)}
              >
                <option value="">Təyin edilməyib</option>
                {locations.map((l) => <option key={l.id} value={l.id}>{l.name}</option>)}
              </select>
            </div>
            <div>
              <label className="form-label">Status</label>
              {/* "Təhkim olunub" is not offered: this form has no employee in it, and a status that
                  claims a holder the card does not name is exactly the lie the register must not tell.
                  Assignment happens from the list, with the assign button. */}
              <select
                className="inp"
                value={form.status === 'Assigned' ? 'Assigned' : form.status}
                onChange={(e) => set('status', e.target.value as AssetStatus)}
              >
                {form.status === 'Assigned' && <option value="Assigned">Təhkim olunub</option>}
                <option value="InStock">Anbarda</option>
                <option value="InRepair">Təmirdə</option>
                <option value="WrittenOff">Silinib</option>
              </select>
              {form.status === 'Assigned' && (
                <div className="muted" style={{ fontSize: 11, marginTop: 4 }}>
                  Başqa status seçsəniz, avadanlıq işçidən geri alınacaq.
                </div>
              )}
            </div>
          </div>

          <div className="form-row">
            <div>
              <label className="form-label">Qeyd</label>
              <input
                className="inp"
                placeholder="məs. zəmanət 2027-ci ilə qədər"
                value={form.notes ?? ''}
                onChange={(e) => set('notes', e.target.value || null)}
              />
            </div>
          </div>

          <div style={{ display: 'flex', gap: 8, marginTop: 4 }}>
            <button className="btn btn-primary" type="submit" disabled={busy}>
              {editingId ? 'Yadda saxla' : 'Əlavə et'}
            </button>
            <button className="btn" type="button" onClick={() => { setShowForm(false); setEditingId(null) }}>
              Ləğv
            </button>
          </div>
        </form>
      )}

      <div className="tbl-wrap">
        <table>
          <thead>
            <tr>
              <th>İnventar N</th>
              <th>Avadanlıq</th>
              <th>Növ</th>
              <th>Seriya N</th>
              <th>Filial</th>
              <th>Kimə təhkim olunub</th>
              <th>Status</th>
              <th style={{ textAlign: 'right' }}>Əməliyyat</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={row.id}>
                <td style={{ fontWeight: 700, whiteSpace: 'nowrap' }}>{row.inventoryNumber}</td>
                <td>
                  <div style={{ fontWeight: 600 }}>{row.name}</div>
                  {(row.brand || row.model) && (
                    <div className="muted" style={{ fontSize: 11 }}>
                      {[row.brand, row.model].filter(Boolean).join(' ')}
                    </div>
                  )}
                </td>
                <td className="muted">{ASSET_TYPE_LABEL[row.type]}</td>
                <td className="muted">{row.serialNumber ?? '—'}</td>
                <td className="muted">{row.locationName ?? '—'}</td>
                <td>
                  {row.assignedEmployeeName ? (
                    <>
                      <div style={{ fontWeight: 600 }}>{row.assignedEmployeeName}</div>
                      {row.assignedAtUtc && (
                        <div className="muted" style={{ fontSize: 11 }}>
                          {new Date(row.assignedAtUtc).toLocaleDateString('az-AZ')}
                        </div>
                      )}
                    </>
                  ) : <span className="muted">—</span>}
                </td>
                <td><span className={STATUS_CLASS[row.status]}>{ASSET_STATUS_LABEL[row.status]}</span></td>
                <td style={{ textAlign: 'right', whiteSpace: 'nowrap' }}>
                  {assigning === row.id ? (
                    <div style={{ display: 'flex', gap: 6, justifyContent: 'flex-end', alignItems: 'center' }}>
                      <select
                        className="inp"
                        style={{ maxWidth: 200 }}
                        value={assignTo}
                        onChange={(e) => setAssignTo(e.target.value)}
                      >
                        <option value="">Kimə verilir?</option>
                        {activeEmployees.map((e) => <option key={e.id} value={e.id}>{e.fullName}</option>)}
                      </select>
                      <button className="btn btn-sm btn-primary" disabled={busy || !assignTo} onClick={() => void assign(row)}>
                        Təhkim et
                      </button>
                      <button className="btn btn-sm" onClick={() => { setAssigning(null); setAssignTo('') }}>Ləğv</button>
                    </div>
                  ) : (
                    <div style={{ display: 'flex', gap: 6, justifyContent: 'flex-end' }}>
                      {row.assignedEmployeeId ? (
                        <button className="btn btn-sm" disabled={busy} onClick={() => void giveBack(row)}>Geri al</button>
                      ) : row.status !== 'WrittenOff' && (
                        <button className="btn btn-sm" onClick={() => { setAssigning(row.id); setAssignTo(''); setErr(null) }}>
                          Təhkim et
                        </button>
                      )}
                      <button className="btn btn-sm" onClick={() => openEdit(row)}>Redaktə</button>
                      <button className="btn btn-sm btn-danger" disabled={busy} onClick={() => void remove(row)}>Sil</button>
                    </div>
                  )}
                </td>
              </tr>
            ))}
            {loaded && rows.length === 0 && (
              <tr>
                <td colSpan={8} className="muted" style={{ textAlign: 'center', padding: 28 }}>
                  {filtered ? 'Bu şərtlərə uyğun avadanlıq tapılmadı.' : 'Hələ avadanlıq əlavə edilməyib.'}
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      <div className="muted" style={{ fontSize: 12, marginTop: 12 }}>
        Üzərində avadanlıq olan işçi silinə bilmir — əvvəlcə avadanlıq geri alınmalıdır. Belədə heç bir
        texnika sahibsiz qalmır.
      </div>
    </div>
  )
}
