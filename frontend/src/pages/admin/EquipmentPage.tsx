import { useEffect, useMemo, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import {
  createEquipment,
  deleteEquipment,
  getEquipment,
  importEquipment,
  updateEquipment,
  type EquipmentInput,
  type EquipmentRecord,
  type ImportResult,
} from '../../api/equipment'
import { getEmployees, type AdminEmployee } from '../../api/admin'
// Not toLocaleDateString('az'): that renders 2026-08-29 where the rest of the app writes 29.08.2026,
// and it reads the VIEWER'S timezone. A date-only rendering of an instant taken in the device's zone
// shows the wrong day either side of midnight — the bug this project has already been bitten by.
import { fmtDateOfInstant } from '../../lib/format'
import { countKit, groupByArea, hasKind, KIT_LABEL, kitLabel, ordinaryKinds, readKit, unlinkedIsExceptional, type KitKind } from '../../lib/equipmentKit'
import {
  IconAlert,
  IconBriefcase,
  IconCheck,
  IconDesktop,
  IconDots,
  IconGrid,
  IconLaptop,
  IconMapPin,
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

/**
 * Nothing is quieted here.
 *
 * The card grid hides a common kind's NAME because eighty cards repeat it. The drawer is one person,
 * opened deliberately to read what they have — and the table view exists to be checked line by line
 * against the original spreadsheet. Both are places where the words are the content, not the noise.
 */
const SPELL_EVERYTHING_OUT: Set<KitKind> = new Set()

const KIT_ICON: Record<KitKind, typeof IconDesktop> = {
  desktop: IconDesktop,
  laptop: IconLaptop,
  monitor: IconMonitor,
  printer: IconPrinter,
  scanner: IconScanner,
  ups: IconPower,
  other: IconBriefcase,
}

/**
 * A stable tone for a person's avatar, chosen from their name.
 *
 * Every avatar on the board was the same amber, which read as a design choice and was not one: the
 * amber is `.unlinked`, and every row in the register is unlinked, so the wall showed one colour
 * eighty times. Giving each person a tone of their own makes a card findable by shape rather than by
 * reading it — you learn where "yours" sits.
 *
 * Amber is deliberately NOT in the set. It means "needs attention" everywhere else on this screen,
 * and a decorative amber avatar beside a real amber warning teaches people to ignore the warning.
 */
const TONES = 5
function tone(name: string): number {
  let h = 0
  for (const ch of name) h = (h * 31 + ch.codePointAt(0)!) >>> 0
  return h % TONES
}

/** Two letters for the avatar. Azerbaijani casing, so «i» becomes «İ» and not «I». */
function initials(name: string): string {
  const parts = name.trim().split(/\s+/).filter(Boolean)
  const picked = parts.length >= 2 ? [parts[0]!, parts[1]!] : parts.slice(0, 1)
  return picked.map((p) => p.charAt(0).toLocaleUpperCase('az')).join('')
}

/**
 * Vəzifə and işlədiyi ərazi — two facts, kept apart.
 *
 * They used to be one line joined by "·". Once the line wrapped, a middle dot sitting somewhere
 * inside two lines of text stopped reading as a boundary at all, and the job title and the place ran
 * into each other. The blank-title case was worse: `[null, area].join()` put the AREA in the title's
 * position, where it simply read as the person's job.
 *
 * So they are given different shapes rather than a separator. The title is text; the place is a
 * tagged line with a pin on it — which is also what it is elsewhere on this screen, a thing you
 * filter by. There is no separator left to lose in a wrap.
 */
function Who({ position, area }: { position: string | null; area: string | null }) {
  const place = area?.trim()
  return (
    <>
      <span className="eq-pos">{position?.trim() || 'vəzifə yazılmayıb'}</span>
      {place && (
        <span className="eq-area">
          <IconMapPin />
          <span className="eq-area-t">{place}</span>
        </span>
      )}
    </>
  )
}

/** Keeps the newlines the register uses to list a second machine under the first. */
function Lines({ text }: { text: string | null }) {
  if (!text) return <span className="muted">—</span>
  return <span style={{ whiteSpace: 'pre-line' }}>{text}</span>
}

/**
 * @param ordinary  Kinds so common in this register that their name is background noise — see
 *                  `ordinaryKinds`. They render as icon (and count) only, name in the tooltip.
 */
function KitChips({ row, ordinary }: { row: EquipmentRecord; ordinary: Set<KitKind> }) {
  const kit = readKit(row)
  // Spans, not divs: this renders inside the card's <button>, which may only hold phrasing
  // content. The layout comes from CSS either way.
  if (kit.length === 0) return <span className="eq-empty-kit">Avadanlıq yazılmayıb</span>
  return (
    <span className="eq-kit">
      {kit.map((item) => {
        const Icon = KIT_ICON[item.kind]
        const quiet = ordinary.has(item.kind)
        return (
          <span
            key={item.kind}
            className={`eq-kit-chip k-${item.kind}${quiet ? ' quiet' : ''}`}
            title={quiet ? kitLabel(item) : undefined}
          >
            <Icon />
            {quiet ? (item.count !== null && item.count > 1 ? `×${item.count}` : '') : kitLabel(item)}
          </span>
        )
      })}
    </span>
  )
}

/**
 * One block of the detail panel: a heading, and the register's own words underneath it.
 *
 * Renders nothing when the column is empty. All four blocks used to show unconditionally, and most
 * lines fill two of them — so the panel was half placeholder, and "yazılmayıb" three times over is
 * three things to read and discard before reaching the one fact you opened the panel for. Same call
 * the dashboard made about zeroes.
 *
 * Monospaced, because these are specifications — "i5 11-ci nəsil / 8 GB / SSD 480 GB" is a list of
 * values with structure worth preserving, and a spec read as a paragraph is a spec nobody checks.
 * The register's summary sentence is NOT one of these; see Summary below.
 */
function Spec({ title, value }: { title: string; value: string | null }) {
  const written = value?.trim()
  if (!written) return null
  return (
    <div className="eq-spec">
      <div className="eq-spec-t">{title}</div>
      <div className="eq-spec-v">{written}</div>
    </div>
  )
}

/**
 * The register's "Avadanlıq" column — its own sentence about what this person has.
 *
 * It is a SUMMARY of the three columns after it, so in a panel that already shows the chips and then
 * each detail block, printing it as a fourth co-equal box said the same three facts a third time,
 * in the loudest position on the panel. And it is prose, so setting it in the spec blocks' monospace
 * made a sentence read like a config file.
 *
 * It is not dropped, because it can name something the detail columns have no place for. It is
 * demoted: a quiet caption under the chips when there are details to summarise, and a proper block
 * when it is the only thing written — which is the difference between a summary and the record.
 */
function Summary({ text, alone }: { text: string | null; alone: boolean }) {
  const written = text?.trim()
  if (!written) return null
  if (alone) return <Spec title="Avadanlıq" value={written} />
  return <p className="eq-summary">{written}</p>
}

export function EquipmentPage() {
  const [rows, setRows] = useState<EquipmentRecord[]>([])
  const [employees, setEmployees] = useState<AdminEmployee[]>([])
  const [loaded, setLoaded] = useState(false)
  const [busy, setBusy] = useState(false)
  const [msg, setMsg] = useState<string | null>(null)
  const [err, setErr] = useState<string | null>(null)
  const [imported, setImported] = useState<ImportResult | null>(null)

  const [search, setSearch] = useState('')
  const [view, setView] = useState<'cards' | 'table'>('cards')
  const [menu, setMenu] = useState(false)
  /** Which shape to print. Two different documents: an overview somebody files, and the full list. */
  const [printing, setPrinting] = useState<'summary' | 'full' | null>(null)
  const [filter, setFilter] = useState<KitKind | 'all' | 'unlinked'>('all')
  const [area, setArea] = useState('')
  const [openId, setOpenId] = useState<string | null>(null)

  const [showForm, setShowForm] = useState(false)
  const [editingId, setEditingId] = useState<string | null>(null)
  const [form, setForm] = useState<EquipmentInput>(EMPTY_FORM)

  const fileInput = useRef<HTMLInputElement>(null)

  /**
   * The whole register, once — search and every filter are applied here rather than on the server.
   *
   * It used to send the search term as `q` and re-fetch on every pause in typing. That has to change
   * for the headline band to mean anything: totals computed from a filtered list would fall as you
   * typed, so "55 sistem bloku" would silently become "3 sistem bloku" and still look like the
   * company's holding. The band must count the register, not the search.
   *
   * Affordable because the page has no pagination and never had: it already loaded every row. This
   * is one fetch instead of one per keystroke-pause, and the search is now instant. The server keeps
   * its `q` support for other callers.
   */
  async function load() {
    const list = await getEquipment()
    if (list.status === 200 && Array.isArray(list.data)) setRows(list.data)
    setLoaded(true)
  }

  useEffect(() => { void load() }, [])

  useEffect(() => {
    void getEmployees().then((r) => {
      if (r.status === 200 && Array.isArray(r.data)) setEmployees(r.data)
    })
  }, [])

  // The print class has to be on the DOM before the print dialog reads the page, and a state update
  // has not flushed by the time an onClick returns — so the dialog is opened from an effect.
  useEffect(() => {
    if (!printing) return
    const t = setTimeout(() => { window.print(); setPrinting(null) }, 60)
    return () => clearTimeout(t)
  }, [printing])

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

  /** What the company holds, counted over the WHOLE register — never over what is on screen. */
  const totals = useMemo(() => countKit(rows), [rows])
  const unlinked = useMemo(() => rows.filter((r) => !r.employeeId).length, [rows])
  // From the WHOLE register, not the filtered view — a chip that changes shape while you read it is
  // worse than a chip that repeats.
  const ordinary = useMemo(() => ordinaryKinds(rows), [rows])
  // Amber is spent only when it means something. Every row unlinked = eighty warnings = none.
  const warnUnlinked = unlinkedIsExceptional(unlinked, rows.length)

  const visible = useMemo(() => {
    // Same columns the server's `q` searched, so moving the search here changed where it runs and
    // nothing about what it finds.
    const needle = search.trim().toLocaleLowerCase('az')
    const matches = (r: EquipmentRecord) =>
      !needle
      || [r.fullName, r.position, r.area, r.equipment, r.systemUnit, r.monitor, r.otherEquipment]
        .some((v) => v?.toLocaleLowerCase('az').includes(needle))

    return rows.filter((r) => {
      if (!matches(r)) return false
      if (area && r.area?.trim() !== area) return false
      if (filter === 'all') return true
      if (filter === 'unlinked') return !r.employeeId
      return hasKind(r, filter)
    })
  }, [rows, filter, area, search])

  /** The cards, gathered under the site they belong to — biggest first, long tail folded. */
  const groups = useMemo(() => groupByArea(visible), [visible])

  /** Every site in the register, with what it holds. The print summary's table — not the display
   *  groups, because a report on paper lists every site rather than folding the small ones away. */
  const byArea = useMemo(() => {
    const map = new Map<string, EquipmentRecord[]>()
    for (const r of visible) {
      const key = r.area?.trim() || 'Ərazi yazılmayıb'
      const list = map.get(key)
      if (list) list.push(r)
      else map.set(key, [r])
    }
    return [...map.entries()]
      .map(([area, rows]) => ({ area, rows, kit: countKit(rows) }))
      .sort((a, b) => b.rows.length - a.rows.length || a.area.localeCompare(b.area, 'az'))
  }, [visible])

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
    <div className={printing === 'full' ? 'eq-print-full' : 'eq-print-summary'}>
      <div className="card card-pad eq-head">
        <div className="eq-head-top">
          <div className="eq-head-count">
            <b>{rows.length}</b> nəfər
            {areas.length > 0 && <> <span className="eq-dot">·</span> <b>{areas.length}</b> ərazi</>}
          </div>
          <div style={{ position: 'relative', flex: '1 1 260px', display: 'flex', alignItems: 'center' }}>
            <input
              className="inp eq-search"
              placeholder="Ad, vəzifə, ərazi, avadanlıq axtar…"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              style={{ width: '100%' }}
            />
            {search && (
              <button
                type="button"
                onClick={() => setSearch('')}
                style={{
                  position: 'absolute',
                  right: 10,
                  top: '50%',
                  transform: 'translateY(-50%)',
                  background: 'none',
                  border: 'none',
                  color: 'var(--c400)',
                  cursor: 'pointer',
                  fontSize: 12,
                  padding: 4,
                }}
                title="Təmizlə"
              >
                ✕
              </button>
            )}
          </div>
          <div className="eq-view" role="group" aria-label="Görünüş">
            <button type="button" className={view === 'cards' ? 'active' : ''} onClick={() => setView('cards')}
              title="Kart görünüşü"><IconGrid /></button>
            <button type="button" className={view === 'table' ? 'active' : ''} onClick={() => setView('table')}
              title="Cədvəl görünüşü — fayl ilə tutuşdurmaq üçün"><IconTable /></button>
          </div>
          {/* Editing the register is a once-a-month job and printing it is a once-a-quarter one;
              reading it is daily. The three of them used to sit across the top with equal weight. */}
          <div className="eq-more">
            <button className="btn eq-more-btn" onClick={() => setMenu((m) => !m)} aria-label="Digər əməliyyatlar">
              <IconDots />
            </button>
            {menu && (
              <>
                <div className="eq-more-back" onClick={() => setMenu(false)} />
                <div className="eq-more-menu">
                  <button onClick={() => { setMenu(false); openNew() }}>Yeni qeyd</button>
                  <button disabled={busy} onClick={() => { setMenu(false); fileInput.current?.click() }}>
                    Excel-dən idxal
                  </button>
                  <button onClick={() => { setMenu(false); setPrinting('summary') }}>Çap et — icmal</button>
                  <button onClick={() => { setMenu(false); setPrinting('full') }}>Çap et — tam siyahı</button>
                </div>
              </>
            )}
          </div>
          <input
            ref={fileInput}
            type="file"
            accept=".xlsx"
            style={{ display: 'none' }}
            onChange={(e) => void onFile(e)}
          />
        </div>

        {/*
          What the company holds — and the filter, in the same control.
          The two used to be separate rows naming the same categories, one counting and one filtering.
          They are one row now, which removes a row of chrome and puts the number that was missing
          altogether — how much kit there IS, rather than how many rows are tidy — at the top of the
          screen where somebody looking for it would start.
        */}
        <div className="eq-tiles" role="group" aria-label="Avadanlıq üzrə süzgəc">
          <button
            type="button"
            className={`eq-tile${filter === 'all' ? ' active' : ''}`}
            onClick={() => setFilter('all')}
          >
            <span className="eq-tile-n">{rows.length}</span>
            <span className="eq-tile-l">Hamısı</span>
          </button>
          {totals.map((t) => {
            const Icon = KIT_ICON[t.kind]
            return (
              <button
                key={t.kind}
                type="button"
                className={`eq-tile k-${t.kind}${filter === t.kind ? ' active' : ''}`}
                onClick={() => setFilter(filter === t.kind ? 'all' : t.kind)}
                title={`ən azı ${t.devices} ədəd · ${t.people} nəfərdə`}
              >
                <span className="eq-tile-ic"><Icon /></span>
                <span className="eq-tile-n">{t.devices}</span>
                <span className="eq-tile-l">{KIT_LABEL[t.kind]}</span>
              </button>
            )
          })}
          {unlinked > 0 && (
            <button
              type="button"
              className={`eq-tile${warnUnlinked ? ' warn' : ''}${filter === 'unlinked' ? ' active' : ''}`}
              onClick={() => setFilter(filter === 'unlinked' ? 'all' : 'unlinked')}
              title="Bu qeydlər işçi siyahısındakı heç kimlə uyğunlaşmayıb"
            >
              <span className="eq-tile-ic">{warnUnlinked ? <IconAlert /> : <IconUser />}</span>
              <span className="eq-tile-n">{unlinked}</span>
              <span className="eq-tile-l">Bağlanmayıb</span>
            </button>
          )}
        </div>

        <div className="eq-head-foot">
          {/* Said once, plainly. The alternative is a screen that either lies by rounding down or
              prints "1 monitor" on a line that never claimed a number. */}
          <span className="muted">
            Saylar reyestrin mətnindən oxunur; say yazılmayıbsa 1 götürülür — faktiki miqdar bundan az deyil.
          </span>
          {areas.length > 1 && (
            <select
              className="inp eq-area-pick"
              value={area}
              onChange={(e) => setArea(e.target.value)}
            >
              <option value="">Bütün ərazilər</option>
              {areas.map((a) => <option key={a} value={a}>{a}</option>)}
            </select>
          )}
          {visible.length !== rows.length && (
            <span className="muted eq-count">{visible.length} / {rows.length}</span>
          )}
        </div>

        {msg && <div className="fb fb-ok" style={{ marginTop: 12 }}><IconCheck /><span>{msg}</span></div>}
        {err && <div className="fb fb-err" style={{ marginTop: 12 }}><IconX /><span>{err}</span></div>}
        {imported && (
          <div className="fb fb-ok" style={{ marginTop: 12 }}>
            <IconCheck />
            <span>
              <b>{imported.added}</b> yeni sətir əlavə edildi, <b>{imported.updated}</b> sətir yeniləndi.
              {' '}<b>{imported.linked}</b> ad işçi siyahısı ilə uyğunlaşdı.
              {/* Where the explanation belongs: next to the numbers it explains, at the one moment
                  somebody is looking at them. It used to sit above the whole screen, every day, for
                  the sake of the once-a-month upload. */}
              <div className="muted" style={{ fontSize: 12, marginTop: 6 }}>
                İdxal «Sıra №» üzrə işləyir — eyni faylı yenidən yükləsəniz sətirlər təzələnir,
                təkrarlanmır. Faylda olmayan sətirlər silinmir.
              </div>
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

      {/*
        The one-page overview — screen-hidden, print-only.
        The card wall is the screen's document; on paper it is eight to ten sheets nobody files. What
        a director keeps is a page: what the company holds, where it sits, and the exceptions.
      */}
      <div className="eq-print" aria-hidden="true">
        <div className="eq-print-h">
          <h1>Kompüter inventarizasiyası</h1>
          <span>{fmtDateOfInstant(new Date().toISOString())}</span>
        </div>
        <div className="eq-print-tot">
          <b>{rows.length}</b> nəfər · <b>{byArea.length}</b> ərazi
          {totals.map((t) => (
            <span key={t.kind}> · <b>{t.devices}</b> {KIT_LABEL[t.kind].toLocaleLowerCase('az')}</span>
          ))}
        </div>
        <table className="eq-print-tbl">
          <thead>
            <tr>
              <th>Ərazi</th>
              <th>Nəfər</th>
              {totals.map((t) => <th key={t.kind}>{KIT_LABEL[t.kind]}</th>)}
            </tr>
          </thead>
          <tbody>
            {byArea.map((a) => (
              <tr key={a.area}>
                <td>{a.area}</td>
                <td>{a.rows.length}</td>
                {totals.map((t) => {
                  const n = a.kit.find((k) => k.kind === t.kind)?.devices ?? 0
                  return <td key={t.kind}>{n || '—'}</td>
                })}
              </tr>
            ))}
          </tbody>
          <tfoot>
            <tr>
              <td>Cəmi</td>
              <td>{visible.length}</td>
              {totals.map((t) => <td key={t.kind}>{t.devices}</td>)}
            </tr>
          </tfoot>
        </table>
        <p className="eq-print-note">
          Saylar reyestrin mətnindən oxunur; say yazılmayıbsa 1 götürülür — faktiki miqdar bundan az deyil.
          {unlinked > 0 && ` ${unlinked} qeyd işçi siyahısı ilə bağlanmayıb.`}
        </p>
      </div>

      {view === 'cards' ? (
        <div className="eq-groups">
          {groups.map((g) => (
            <section key={g.area} className="eq-group">
              {/* Headings only when there is more than one place to tell apart. A single heading over
                  everything is a label, not a structure. */}
              {groups.length > 1 && (
                <h2 className="eq-group-h">
                  <span className="eq-group-t">{g.area}</span>
                  <span className="eq-group-n">{g.rows.length} nəfər</span>
                  {/* What THIS site holds. The band at the top is the company's total; the question
                      anyone asks next is where it sits, and that answer belonged in the heading
                      rather than behind picking the site out of a dropdown. */}
                  <span className="eq-group-kit">
                    {countKit(g.rows).map((t) => {
                      const Icon = KIT_ICON[t.kind]
                      return (
                        <span key={t.kind} className={`eq-group-kit-i k-${t.kind}`} title={KIT_LABEL[t.kind]}>
                          <Icon />{t.devices}
                        </span>
                      )
                    })}
                  </span>
                </h2>
              )}
              <div className="eq-grid">
                {g.rows.map((row) => (
                  <button
                    key={row.id}
                    type="button"
                    className={`eq-card${!row.employeeId && warnUnlinked ? ' unlinked' : ''}`}
                    onClick={() => setOpenId(row.id)}
                  >
                    <span className="eq-card-head">
                      <span className={`eq-av t${tone(row.fullName)}`}>{initials(row.fullName)}</span>
                      <span className="eq-id">
                        <span className="eq-nm">{row.fullName}</span>
                        <Who position={row.position} area={groups.length > 1 && !g.merged ? null : row.area} />
                      </span>
                      <span className="eq-no">№{row.rowNo}</span>
                    </span>
                    <span className="eq-card-rule" />
                    <KitChips row={row} ordinary={ordinary} />
                  </button>
                ))}
              </div>
            </section>
          ))}
          {loaded && visible.length === 0 && (
            <div className="eq-none">
              {rows.length === 0
                ? 'Siyahı boşdur — «⋯ → Excel-dən idxal» ilə mövcud faylı yükləyin.'
                : 'Bu şərtlərə uyğun qeyd tapılmadı.'}
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
                      ? 'Siyahı boşdur — «⋯ → Excel-dən idxal» ilə mövcud faylı yükləyin.'
                      : 'Bu şərtlərə uyğun sətir tapılmadı.'}
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
                <Who position={shown.position} area={shown.area} />
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
              <KitChips row={shown} ordinary={SPELL_EVERYTHING_OUT} />
              <Summary
                text={shown.equipment}
                alone={!shown.systemUnit?.trim() && !shown.monitor?.trim() && !shown.otherEquipment?.trim()}
              />
              <Spec title="Sistem bloku" value={shown.systemUnit} />
              <Spec title="Monitor" value={shown.monitor} />
              <Spec title="Digər avadanlıq" value={shown.otherEquipment} />
              <div className="muted eq-drawer-meta">
                Sıra № {shown.rowNo} · yeniləndi {fmtDateOfInstant(shown.updatedAtUtc)}
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
