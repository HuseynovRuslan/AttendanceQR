import { useEffect, useMemo, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
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
import { hasKind, kitLabel, readKit, type KitKind } from '../../lib/equipmentKit'
import {
  IconBriefcase,
  IconCheck,
  IconDesktop,
  IconDownload,
  IconGrid,
  IconLaptop,
  IconMonitor,
  IconPower,
  IconPrinter,
  IconScanner,
  IconTable,
  IconUser,
  IconX,
} from '../../components/icons'

/**
 * The IT equipment register — who holds what.
 *
 * It opens as a card per person, because the question anyone actually arrives with is about a
 * person: what has this one got, who has a spare monitor, what does a leaver have to hand back.
 *
 * The register is imported from "İT AVADANLIQLARININ SİYAHISI" and the table view still mirrors that
 * file column for column. That view has a real job — being compared against the file after a
 * re-import — but it was also the ONLY way to read the register, which made a nine-column
 * spreadsheet, four of whose columns are paragraphs of prose, the everyday screen. Two jobs, two
 * views; the reconciliation one is a click away instead of the front door.
 *
 * The chips on a card are DERIVED from that prose and never stored (see lib/equipmentKit), and they
 * quote a count only where the register wrote one — a number on this screen is a number somebody
 * will order against.
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

const KIT_ICON: Record<KitKind, typeof IconDesktop> = {
  desktop: IconDesktop,
  laptop: IconLaptop,
  monitor: IconMonitor,
  printer: IconPrinter,
  scanner: IconScanner,
  ups: IconPower,
  other: IconBriefcase,
}

/** Filter chips. `unlinked` is not a kind of kit — it is the register's staleness against the staff list. */
const FILTERS: { key: KitKind | 'all' | 'unlinked'; label: string }[] = [
  { key: 'all', label: 'Hamısı' },
  { key: 'desktop', label: 'Sistem bloku' },
  { key: 'laptop', label: 'Noutbuk' },
  { key: 'monitor', label: 'Monitor' },
  { key: 'printer', label: 'Printer' },
  { key: 'unlinked', label: 'Bağlanmayıb' },
]

/** Two letters for the avatar. Azerbaijani casing, so «i» becomes «İ» and not «I». */
function initials(name: string): string {
  const parts = name.trim().split(/\s+/).filter(Boolean)
  const picked = parts.length >= 2 ? [parts[0]!, parts[1]!] : parts.slice(0, 1)
  return picked.map((p) => p.charAt(0).toLocaleUpperCase('az')).join('')
}

/** Keeps the newlines the register uses to list a second machine under the first. */
function Lines({ text }: { text: string | null }) {
  if (!text) return <span className="muted">—</span>
  return <span style={{ whiteSpace: 'pre-line' }}>{text}</span>
}

function KitChips({ row }: { row: EquipmentRecord }) {
  const kit = readKit(row)
  // Spans, not divs: this renders inside the card's <button>, which may only hold phrasing
  // content. The layout comes from CSS either way.
  if (kit.length === 0) return <span className="eq-empty-kit">Avadanlıq yazılmayıb</span>
  return (
    <span className="eq-kit">
      {kit.map((item) => {
        const Icon = KIT_ICON[item.kind]
        return (
          <span key={item.kind} className={`eq-kit-chip k-${item.kind}`}>
            <Icon />
            {kitLabel(item)}
          </span>
        )
      })}
    </span>
  )
}

/** One block of the detail panel: a heading, and the register's own words underneath it. */
function Spec({ title, value }: { title: string; value: string | null }) {
  const written = value?.trim()
  return (
    <div className="eq-spec">
      <div className="eq-spec-t">{title}</div>
      <div className={`eq-spec-v${written ? '' : ' none'}`}>{written || 'yazılmayıb'}</div>
    </div>
  )
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
  const [view, setView] = useState<'cards' | 'table'>('cards')
  const [filter, setFilter] = useState<KitKind | 'all' | 'unlinked'>('all')
  const [area, setArea] = useState('')
  const [openId, setOpenId] = useState<string | null>(null)

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

  // Esc closes the panel. A drawer that only shuts via its own × is a drawer people leave open.
  useEffect(() => {
    if (!openId) return
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') setOpenId(null) }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [openId])

  const employeeOptions = useMemo(
    () => [...employees].sort((a, b) => a.fullName.localeCompare(b.fullName, 'az')),
    [employees],
  )

  const areas = useMemo(
    () => [...new Set(rows.map((r) => r.area?.trim()).filter((a): a is string => !!a))]
      .sort((a, b) => a.localeCompare(b, 'az')),
    [rows],
  )

  const visible = useMemo(
    () => rows.filter((r) => {
      if (area && r.area?.trim() !== area) return false
      if (filter === 'all') return true
      if (filter === 'unlinked') return !r.employeeId
      return hasKind(r, filter)
    }),
    [rows, filter, area],
  )

  // The panel reads from `rows`, not from a copy taken when it opened — otherwise a save leaves the
  // old text sitting on screen next to a "yeniləndi" message.
  const shown = openId ? rows.find((r) => r.id === openId) ?? null : null

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
    setOpenId(null)
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
    // The form is at the top of the page; from a card near the bottom it would otherwise open
    // off-screen and read as "the Redaktə button does nothing".
    setOpenId(null)
    setMsg(null)
    setErr(null)
    setImported(null)
    window.scrollTo({ top: 0, behavior: 'smooth' })
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
    if (!window.confirm(`«${row.fullName}» qeydi silinsin?`)) return
    setBusy(true)
    setErr(null)
    const { status, data } = await deleteEquipment(row.id)
    setBusy(false)
    if (status === 200) {
      setOpenId(null)
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
          <div className="stat-lbl">Nəfər</div>
          <div className="stat-val">{summary?.total ?? '—'}</div>
          <div className="stat-sub">siyahıdakı adam sayı</div>
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
        <div className="eq-toolbar">
          <input
            className="inp"
            style={{ flex: '1 1 260px', minWidth: 200 }}
            placeholder="Ad, vəzifə, ərazi, avadanlıq, «RTX 4090»…"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
          <div className="eq-view" role="group" aria-label="Görünüş">
            <button type="button" className={view === 'cards' ? 'active' : ''} onClick={() => setView('cards')}>
              <IconGrid /> Kart
            </button>
            <button type="button" className={view === 'table' ? 'active' : ''} onClick={() => setView('table')}>
              <IconTable /> Cədvəl
            </button>
          </div>
          <button className="btn btn-primary" onClick={openNew}>Yeni qeyd</button>
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

        <div className="divider" />

        <div className="chip-row" style={{ marginBottom: 0 }}>
          {FILTERS.map((f) => (
            <button
              key={f.key}
              type="button"
              className={`chip${filter === f.key ? ' active' : ''}`}
              onClick={() => setFilter(f.key)}
            >
              {f.label}
            </button>
          ))}
          {areas.length > 1 && (
            <select
              className="inp"
              style={{ width: 'auto', minWidth: 150, padding: '6px 10px', fontSize: 12 }}
              value={area}
              onChange={(e) => setArea(e.target.value)}
            >
              <option value="">Bütün ərazilər</option>
              {areas.map((a) => <option key={a} value={a}>{a}</option>)}
            </select>
          )}
          <span className="muted" style={{ fontSize: 12, marginLeft: 'auto', alignSelf: 'center' }}>
            {visible.length} / {rows.length}
          </span>
        </div>

        <div className="muted" style={{ fontSize: 12, marginTop: 10 }}>
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
            {editingId ? 'Qeydi redaktə et' : 'Yeni qeyd'}
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

      {view === 'cards' ? (
        <div className="eq-grid">
          {visible.map((row) => (
            <button
              key={row.id}
              type="button"
              className={`eq-card${row.employeeId ? '' : ' unlinked'}`}
              onClick={() => setOpenId(row.id)}
            >
              <span className="eq-card-head">
                <span className="eq-av">{initials(row.fullName)}</span>
                <span className="eq-id">
                  <span className="eq-nm">{row.fullName}</span>
                  <span className="eq-meta">
                    {[row.position, row.area].filter(Boolean).join(' · ') || 'vəzifə yazılmayıb'}
                  </span>
                </span>
                <span className="eq-no">{row.rowNo}</span>
              </span>
              <KitChips row={row} />
              {!row.employeeId && <span className="tag eq-flag">işçi siyahısında yoxdur</span>}
            </button>
          ))}
          {loaded && visible.length === 0 && (
            <div className="eq-none">
              {rows.length === 0
                ? (q ? 'Bu şərtlərə uyğun qeyd tapılmadı.' : 'Siyahı boşdur — «Excel-dən idxal» ilə mövcud faylı yükləyin.')
                : 'Bu süzgəcə uyğun qeyd yoxdur.'}
            </div>
          )}
        </div>
      ) : (
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
              {visible.map((row) => (
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
              {loaded && visible.length === 0 && (
                <tr>
                  <td colSpan={9} className="muted" style={{ textAlign: 'center', padding: 28 }}>
                    {rows.length === 0
                      ? (q ? 'Bu şərtlərə uyğun sətir tapılmadı.' : 'Siyahı boşdur — «Excel-dən idxal» ilə mövcud faylı yükləyin.')
                      : 'Bu süzgəcə uyğun sətir yoxdur.'}
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      )}

      {shown && (
        <>
          <div className="eq-backdrop" onClick={() => setOpenId(null)} />
          <aside className="eq-drawer" role="dialog" aria-label={shown.fullName}>
            <div className="eq-drawer-head">
              <span className="eq-av">{initials(shown.fullName)}</span>
              <div style={{ flex: 1, minWidth: 0 }}>
                <div className="eq-nm">{shown.fullName}</div>
                <div className="eq-meta">
                  {[shown.position, shown.area].filter(Boolean).join(' · ') || 'vəzifə yazılmayıb'}
                </div>
                {shown.employeeId ? (
                  <Link to={`/admin/employees/${shown.employeeId}`} className="btn btn-sm" style={{ marginTop: 10 }}>
                    <IconUser /> İşçi profili
                  </Link>
                ) : (
                  <span className="tag eq-flag" style={{ marginTop: 10 }}>işçi siyahısında yoxdur</span>
                )}
              </div>
              <button className="eq-close" onClick={() => setOpenId(null)} aria-label="Bağla"><IconX /></button>
            </div>

            <div className="eq-drawer-body">
              <KitChips row={shown} />
              <div style={{ height: 18 }} />
              <Spec title="Avadanlıq" value={shown.equipment} />
              <Spec title="Sistem bloku" value={shown.systemUnit} />
              <Spec title="Monitor" value={shown.monitor} />
              <Spec title="Digər avadanlıq" value={shown.otherEquipment} />
              <div className="muted" style={{ fontSize: 11, marginTop: 18 }}>
                Sıra № {shown.rowNo} · yeniləndi {new Date(shown.updatedAtUtc).toLocaleDateString('az')}
              </div>
            </div>

            <div className="eq-drawer-foot">
              <button className="btn btn-primary" onClick={() => openEdit(shown)}>Redaktə</button>
              <button className="btn btn-danger" disabled={busy} onClick={() => void remove(shown)}>Sil</button>
            </div>
          </aside>
        </>
      )}
    </div>
  )
}
