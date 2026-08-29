import { useEffect, useMemo, useRef, useState } from 'react'
import {
  createEquipment,
  deleteEquipment,
  getEquipment,
  getEquipmentSummary,
  importEquipment,
  updateEquipment,
  type EquipmentInput,
  type EquipmentRecord,
  type EquipmentSummary,
  type ImportResult,
} from '../../api/equipment'
import { getEmployees, type AdminEmployee } from '../../api/admin'
import { IconCheck, IconDownload, IconX } from '../../components/icons'

/**
 * The IT equipment register.
 *
 * The table is the company's own spreadsheet, column for column: Sıra № · Soyadı, adı, atasının adı ·
 * Vəzifəsi · İşlədiyi ərazi · Avadanlıq · Sistem bloku · Monitor · Digər avadanlıq. That is deliberate
 * — the list is maintained in Excel and compared against it, so a screen with a different shape would
 * be something to reconcile rather than something to read.
 *
 * The equipment columns hold several lines each; they are rendered with the newlines intact, because
 * "i5 11-ci nəsil / 16 GB" and the second machine under it are two facts, not one sentence.
 */

const ERRORS: Record<string, string> = {
  FullNameRequired: 'Ad, soyad lazımdır',
  RowNoExists: 'Bu sıra nömrəsi artıq var',
  EmployeeNotFound: 'İşçi tapılmadı',
  RecordNotFound: 'Qeyd tapılmadı',
  NoFile: 'Fayl seçilməyib',
  EmptyFile: 'Fayl boşdur',
  UnreadableFile: 'Fayl oxunmadı — .xlsx formatında olmalıdır',
  HeaderNotFound: 'Cədvəlin başlıq sətri tapılmadı — «Soyadı, adı, atasının adı» və «Avadanlıq» sütunları olmalıdır',
}

const EMPTY_FORM: EquipmentInput = {
  rowNo: null,
  fullName: '',
  position: null,
  area: null,
  equipment: null,
  systemUnit: null,
  monitor: null,
  otherEquipment: null,
  employeeId: null,
}

/** Keeps the newlines the register uses to list a second machine under the first. */
function Lines({ text }: { text: string | null }) {
  if (!text) return <span className="muted">—</span>
  return <span style={{ whiteSpace: 'pre-line' }}>{text}</span>
}

export function EquipmentPage() {
  const [rows, setRows] = useState<EquipmentRecord[]>([])
  const [summary, setSummary] = useState<EquipmentSummary | null>(null)
  const [employees, setEmployees] = useState<AdminEmployee[]>([])
  const [loaded, setLoaded] = useState(false)
  const [busy, setBusy] = useState(false)
  const [msg, setMsg] = useState<string | null>(null)
  const [err, setErr] = useState<string | null>(null)
  const [imported, setImported] = useState<ImportResult | null>(null)

  const [search, setSearch] = useState('')
  const [q, setQ] = useState('')

  const [showForm, setShowForm] = useState(false)
  const [editingId, setEditingId] = useState<string | null>(null)
  const [form, setForm] = useState<EquipmentInput>(EMPTY_FORM)

  const fileInput = useRef<HTMLInputElement>(null)

  useEffect(() => {
    const t = setTimeout(() => setQ(search.trim()), 300)
    return () => clearTimeout(t)
  }, [search])

  async function load() {
    const [list, stats] = await Promise.all([getEquipment(q), getEquipmentSummary()])
    if (list.status === 200 && Array.isArray(list.data)) setRows(list.data)
    if (stats.status === 200 && stats.data) setSummary(stats.data)
    setLoaded(true)
  }

  useEffect(() => { void load() }, [q])

  useEffect(() => {
    void getEmployees().then((r) => {
      if (r.status === 200 && Array.isArray(r.data)) setEmployees(r.data)
    })
  }, [])

  const employeeOptions = useMemo(
    () => [...employees].sort((a, b) => a.fullName.localeCompare(b.fullName, 'az')),
    [employees],
  )

  function set<K extends keyof EquipmentInput>(key: K, value: EquipmentInput[K]) {
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
    setImported(null)
  }

  function openEdit(row: EquipmentRecord) {
    setForm({
      rowNo: row.rowNo,
      fullName: row.fullName,
      position: row.position,
      area: row.area,
      equipment: row.equipment,
      systemUnit: row.systemUnit,
      monitor: row.monitor,
      otherEquipment: row.otherEquipment,
      employeeId: row.employeeId,
    })
    setEditingId(row.id)
    setShowForm(true)
    setMsg(null)
    setErr(null)
    setImported(null)
  }

  async function submit(e: React.FormEvent) {
    e.preventDefault()
    setBusy(true)
    setErr(null)
    const { status, data } = editingId ? await updateEquipment(editingId, form) : await createEquipment(form)
    setBusy(false)
    if (status === 200) {
      setShowForm(false)
      setEditingId(null)
      setMsg(editingId ? 'Qeyd yeniləndi' : `«${form.fullName}» əlavə edildi`)
      void load()
    } else {
      fail(data, 'Yadda saxlanmadı')
    }
  }

  async function remove(row: EquipmentRecord) {
    if (!window.confirm(`${row.rowNo}. «${row.fullName}» sətri silinsin?`)) return
    setBusy(true)
    setErr(null)
    const { status, data } = await deleteEquipment(row.id)
    setBusy(false)
    if (status === 200) {
      setMsg(`«${row.fullName}» silindi`)
      void load()
    } else {
      fail(data, 'Silinmədi')
    }
  }

  async function onFile(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0]
    e.target.value = '' // so choosing the same file twice fires again
    if (!file) return

    setBusy(true)
    setErr(null)
    setMsg(null)
    setImported(null)
    const { status, data } = await importEquipment(file)
    setBusy(false)
    if (status === 200 && data && 'added' in data) {
      setImported(data)
      void load()
    } else if (status === 0) {
      setErr('Fayl göndərilmədi — əlaqəni yoxlayın')
    } else {
      fail(data, 'İdxal alınmadı')
    }
  }

  return (
    <div>
      <div className="stat-grid">
        <div className="stat-card blue">
          <div className="stat-lbl">Sətir</div>
          <div className="stat-val">{summary?.total ?? '—'}</div>
          <div className="stat-sub">siyahıdakı işçi sayı</div>
        </div>
        <div className="stat-card leaf">
          <div className="stat-lbl">İşçi ilə bağlı</div>
          <div className="stat-val">{summary?.linked ?? '—'}</div>
          <div className="stat-sub">profilində də görünür</div>
        </div>
        <div className="stat-card amber">
          <div className="stat-lbl">Bağlanmayıb</div>
          <div className="stat-val">{summary?.unlinked ?? '—'}</div>
          <div className="stat-sub">adı işçi siyahısında yoxdur</div>
        </div>
        <div className="stat-card">
          <div className="stat-lbl">Sistem bloku</div>
          <div className="stat-val">{summary?.withDesktop ?? '—'}</div>
          <div className="stat-sub">masaüstü kompüteri olan</div>
        </div>
        <div className="stat-card purple">
          <div className="stat-lbl">Ərazi</div>
          <div className="stat-val">{summary?.areas ?? '—'}</div>
          <div className="stat-sub">ofis və sahə sayı</div>
        </div>
      </div>

      <div className="card card-pad" style={{ marginBottom: 16 }}>
        <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', alignItems: 'center' }}>
          <input
            className="inp"
            style={{ flex: '1 1 260px', minWidth: 200 }}
            placeholder="Ad, vəzifə, ərazi, avadanlıq, «RTX 4090»…"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
          <button className="btn btn-primary" onClick={openNew}>Yeni sətir</button>
          <button className="btn" disabled={busy} onClick={() => fileInput.current?.click()}>
            <IconDownload /> Excel-dən idxal
          </button>
          <input
            ref={fileInput}
            type="file"
            accept=".xlsx"
            style={{ display: 'none' }}
            onChange={(e) => void onFile(e)}
          />
        </div>
        <div className="muted" style={{ fontSize: 12, marginTop: 8 }}>
          İdxal «Sıra №» üzrə işləyir: eyni faylı yenidən yükləsəniz sətirlər təzələnir, təkrarlanmır.
          Faylda olmayan sətirlər silinmir.
        </div>
        {msg && <div className="fb fb-ok" style={{ marginTop: 12 }}><IconCheck /><span>{msg}</span></div>}
        {err && <div className="fb fb-err" style={{ marginTop: 12 }}><IconX /><span>{err}</span></div>}
        {imported && (
          <div className="fb fb-ok" style={{ marginTop: 12 }}>
            <IconCheck />
            <span>
              <b>{imported.added}</b> yeni sətir əlavə edildi, <b>{imported.updated}</b> sətir yeniləndi.
              {' '}<b>{imported.linked}</b> ad işçi siyahısı ilə uyğunlaşdı.
              {imported.unmatched.length > 0 && (
                <>
                  <div style={{ marginTop: 6 }}>
                    Uyğunlaşmayan {imported.unmatched.length} ad (sətir yenə də siyahıdadır, işçi ilə
                    əlaqəni əl ilə qura bilərsiniz):
                  </div>
                  <div className="muted" style={{ fontSize: 12, marginTop: 2 }}>
                    {imported.unmatched.join(' · ')}
                  </div>
                </>
              )}
            </span>
          </div>
        )}
      </div>

      {showForm && (
        <form onSubmit={submit} className="card card-pad" style={{ marginBottom: 16, maxWidth: 860 }}>
          <div style={{ fontWeight: 700, color: 'var(--c900)', marginBottom: 14 }}>
            {editingId ? 'Sətri redaktə et' : 'Yeni sətir'}
          </div>

          <div className="form-row cols2">
            <div>
              <label className="form-label">Sıra №</label>
              <input
                className="inp"
                type="number"
                min="1"
                placeholder={editingId ? '' : 'boş buraxsanız sona əlavə olunur'}
                value={form.rowNo ?? ''}
                onChange={(e) => set('rowNo', e.target.value === '' ? null : Number(e.target.value))}
              />
            </div>
            <div>
              <label className="form-label">Soyadı, adı, atasının adı</label>
              <input className="inp" required value={form.fullName} onChange={(e) => set('fullName', e.target.value)} />
            </div>
          </div>

          <div className="form-row cols2">
            <div>
              <label className="form-label">Vəzifəsi</label>
              <input className="inp" value={form.position ?? ''} onChange={(e) => set('position', e.target.value || null)} />
            </div>
            <div>
              <label className="form-label">İşlədiyi ərazi</label>
              <input className="inp" value={form.area ?? ''} onChange={(e) => set('area', e.target.value || null)} />
            </div>
          </div>

          <div className="form-row">
            <div>
              <label className="form-label">Avadanlıq</label>
              <textarea
                className="inp"
                rows={3}
                value={form.equipment ?? ''}
                onChange={(e) => set('equipment', e.target.value || null)}
              />
            </div>
          </div>

          <div className="form-row cols2">
            <div>
              <label className="form-label">Sistem bloku</label>
              <textarea
                className="inp"
                rows={3}
                value={form.systemUnit ?? ''}
                onChange={(e) => set('systemUnit', e.target.value || null)}
              />
            </div>
            <div>
              <label className="form-label">Monitor</label>
              <textarea
                className="inp"
                rows={3}
                value={form.monitor ?? ''}
                onChange={(e) => set('monitor', e.target.value || null)}
              />
            </div>
          </div>

          <div className="form-row cols2">
            <div>
              <label className="form-label">Digər avadanlıq</label>
              <textarea
                className="inp"
                rows={3}
                value={form.otherEquipment ?? ''}
                onChange={(e) => set('otherEquipment', e.target.value || null)}
              />
            </div>
            <div>
              <label className="form-label">İşçi ilə əlaqə</label>
              {/* The import links by an exact name match only. Anything it could not place is linked
                  here by hand — a near-miss guess would hang one person's laptops on another. */}
              <select
                className="inp"
                value={form.employeeId ?? ''}
                onChange={(e) => set('employeeId', e.target.value || null)}
              >
                <option value="">Bağlanmayıb</option>
                {employeeOptions.map((e) => (
                  <option key={e.id} value={e.id}>{e.fullName}{e.isActive ? '' : ' (deaktiv)'}</option>
                ))}
              </select>
              <div className="muted" style={{ fontSize: 11, marginTop: 4 }}>
                Bağlasanız, bu avadanlıq işçinin profilində də görünəcək.
              </div>
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
              <th style={{ width: 60 }}>Sıra №</th>
              <th>Soyadı, adı, atasının adı</th>
              <th>Vəzifəsi</th>
              <th>İşlədiyi ərazi</th>
              <th>Avadanlıq</th>
              <th>Sistem bloku</th>
              <th>Monitor</th>
              <th>Digər avadanlıq</th>
              <th style={{ textAlign: 'right' }}>Əməliyyat</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={row.id}>
                <td className="num" style={{ fontWeight: 700 }}>{row.rowNo}</td>
                <td style={{ fontWeight: 600, minWidth: 180 }}>
                  {row.fullName}
                  {!row.employeeId && (
                    <div className="muted" style={{ fontSize: 11 }}>işçi siyahısında yoxdur</div>
                  )}
                </td>
                <td className="muted" style={{ minWidth: 150 }}>{row.position ?? '—'}</td>
                <td className="muted" style={{ minWidth: 130 }}>{row.area ?? '—'}</td>
                <td style={{ minWidth: 220 }}><Lines text={row.equipment} /></td>
                <td style={{ minWidth: 200 }}><Lines text={row.systemUnit} /></td>
                <td style={{ minWidth: 130 }}><Lines text={row.monitor} /></td>
                <td style={{ minWidth: 180 }}><Lines text={row.otherEquipment} /></td>
                <td style={{ textAlign: 'right', whiteSpace: 'nowrap' }}>
                  <div style={{ display: 'flex', gap: 6, justifyContent: 'flex-end' }}>
                    <button className="btn btn-sm" onClick={() => openEdit(row)}>Redaktə</button>
                    <button className="btn btn-sm btn-danger" disabled={busy} onClick={() => void remove(row)}>Sil</button>
                  </div>
                </td>
              </tr>
            ))}
            {loaded && rows.length === 0 && (
              <tr>
                <td colSpan={9} className="muted" style={{ textAlign: 'center', padding: 28 }}>
                  {q
                    ? 'Bu şərtlərə uyğun sətir tapılmadı.'
                    : 'Siyahı boşdur — «Excel-dən idxal» ilə mövcud faylı yükləyin.'}
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  )
}
