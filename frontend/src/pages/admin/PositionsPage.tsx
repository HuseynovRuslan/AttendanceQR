import { useEffect, useState } from 'react'
import {
  adoptPosition,
  createPosition,
  deletePosition,
  getPositions,
  mergePositions,
  renamePosition,
  type JobPosition,
} from '../../api/positions'
import { IconCheck, IconX } from '../../components/icons'

/**
 * The company's job titles.
 *
 * This exists because position was free text: one job ended up as "Layihə Rəhəri", "Layihə rəhbəri"
 * and "Layihə Meneceri", and every report or ballot rule that groups by position counted them as
 * three. The catalogue stops new duplicates; merging cleans up the ones already in the data.
 */
export function PositionsPage() {
  const [rows, setRows] = useState<JobPosition[]>([])
  const [loaded, setLoaded] = useState(false)
  const [draft, setDraft] = useState('')
  const [busy, setBusy] = useState(false)
  const [msg, setMsg] = useState<string | null>(null)
  const [err, setErr] = useState<string | null>(null)
  const [editing, setEditing] = useState<string | null>(null)
  const [editName, setEditName] = useState('')
  const [mergeFrom, setMergeFrom] = useState<string | null>(null)
  const [mergeInto, setMergeInto] = useState('')

  async function load() {
    const { status, data } = await getPositions()
    if (status === 200 && Array.isArray(data)) setRows(data)
    setLoaded(true)
  }

  useEffect(() => { void load() }, [])

  function reset(message?: string) {
    setEditing(null)
    setMergeFrom(null)
    setMergeInto('')
    setBusy(false)
    setErr(null)
    if (message) setMsg(message)
  }

  async function add() {
    const name = draft.trim()
    if (!name) return
    setBusy(true)
    setMsg(null)
    setErr(null)
    const { status } = await createPosition(name)
    setBusy(false)
    if (status === 200) { setDraft(''); setMsg(`«${name}» əlavə edildi`); void load() }
    else if (status === 409) setErr('Bu vəzifə artıq var')
    else setErr('Əlavə edilmədi')
  }

  async function saveRename(row: JobPosition) {
    const name = editName.trim()
    if (!row.id || !name || name === row.name) { reset(); return }
    const target = rows.find((r) => r.name === name)
    if (target && !window.confirm(
      `«${name}» artıq var. «${row.name}» vəzifəsindəki ${row.count} işçi «${name}» vəzifəsinə keçiriləcək və «${row.name}» siliniəcək.\n\nDavam edilsin?`,
    )) return
    setBusy(true)
    const { status, data } = await renamePosition(row.id, name)
    if (status === 200 && data && 'merged' in data) {
      reset(data.merged ? `${data.movedEmployees} işçi «${name}» vəzifəsinə keçirildi` : `Ad dəyişdirildi: «${name}»`)
      void load()
    } else { setBusy(false); setErr('Dəyişdirilmədi') }
  }

  async function doMerge(row: JobPosition) {
    const into = mergeInto.trim()
    if (!into || into === row.name) return
    if (!window.confirm(
      `«${row.name}» vəzifəsindəki ${row.count} işçi «${into}» vəzifəsinə keçiriləcək və «${row.name}» siyahıdan silinəcək.\n\nDavam edilsin?`,
    )) return
    setBusy(true)
    const { status, data } = await mergePositions(row.name, into)
    if (status === 200 && data && 'movedEmployees' in data) {
      reset(`${data.movedEmployees} işçi «${into}» vəzifəsinə keçirildi`)
      void load()
    } else { setBusy(false); setErr('Birləşdirilmədi') }
  }

  async function remove(row: JobPosition) {
    if (!row.id) return
    if (!window.confirm(`«${row.name}» siyahıdan silinsin?`)) return
    setBusy(true)
    const { status, data } = await deletePosition(row.id)
    setBusy(false)
    if (status === 200) { setMsg(`«${row.name}» silindi`); void load() }
    else setErr(data && 'employees' in data && data.employees
      ? `Bu vəzifədə ${data.employees} işçi var — əvvəlcə başqa vəzifəyə birləşdirin`
      : 'Silinmədi')
  }

  async function adopt(row: JobPosition) {
    setBusy(true)
    await adoptPosition(row.name)
    setBusy(false)
    setMsg(`«${row.name}» siyahıya əlavə edildi`)
    void load()
  }

  const catalogue = rows.filter((r) => r.inCatalogue)
  const orphans = rows.filter((r) => !r.inCatalogue)

  return (
    <div>
      <div className="card card-pad" style={{ marginBottom: 16 }}>
        <div className="card-title">Yeni vəzifə</div>
        <div style={{ display: 'flex', gap: 8, marginTop: 8, maxWidth: 480 }}>
          <input
            className="inp"
            value={draft}
            placeholder="məs. Bağban"
            onChange={(e) => setDraft(e.target.value)}
            onKeyDown={(e) => { if (e.key === 'Enter') void add() }}
          />
          <button className="btn btn-primary" disabled={busy} onClick={() => void add()}>Əlavə et</button>
        </div>
        <div className="muted" style={{ fontSize: 12, marginTop: 8 }}>
          İşçi əlavə edərkən vəzifə bu siyahıdan seçilir. Ona görə eyni iş bir neçə cür yazıla bilmir.
        </div>
        {msg && <div className="fb fb-ok" style={{ marginTop: 12 }}><IconCheck /><span>{msg}</span></div>}
        {err && <div className="fb fb-err" style={{ marginTop: 12 }}><IconX /><span>{err}</span></div>}
      </div>

      {/* Titles people hold that the catalogue never had — a bulk import can create these. Left
          invisible they would look like the list was complete while employees sat outside it. */}
      {orphans.length > 0 && (
        <div className="card card-pad" style={{ marginBottom: 16 }}>
          <div className="card-title">Siyahıda olmayan vəzifələr</div>
          <div className="muted" style={{ fontSize: 12, marginBottom: 10 }}>
            Bu adlar işçilərdə var, amma siyahıda yoxdur. Ya siyahıya əlavə edin, ya da mövcud bir
            vəzifəyə birləşdirin.
          </div>
          <table className="tbl">
            <tbody>
              {orphans.map((row) => (
                <tr key={row.name}>
                  <td style={{ fontWeight: 700 }}>{row.name}</td>
                  <td style={{ width: 90 }} className="muted">{row.count} işçi</td>
                  <td style={{ width: 320, textAlign: 'right' }}>
                    {mergeFrom === row.name ? (
                      <MergeControls
                        options={catalogue.map((c) => c.name)}
                        value={mergeInto}
                        onChange={setMergeInto}
                        onCancel={() => reset()}
                        onConfirm={() => void doMerge(row)}
                        busy={busy}
                      />
                    ) : (
                      <div style={{ display: 'flex', gap: 6, justifyContent: 'flex-end' }}>
                        <button className="btn btn-sm" disabled={busy} onClick={() => void adopt(row)}>
                          Siyahıya əlavə et
                        </button>
                        <button className="btn btn-sm" disabled={busy}
                          onClick={() => { setMergeFrom(row.name); setMergeInto('') }}>
                          Birləşdir
                        </button>
                      </div>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <div className="card card-pad">
        <div className="card-title">Vəzifələr ({catalogue.length})</div>
        {loaded && catalogue.length === 0 && (
          <div className="muted" style={{ marginTop: 8 }}>Hələ vəzifə əlavə edilməyib.</div>
        )}
        <table className="tbl">
          <tbody>
            {catalogue.map((row) => (
              <tr key={row.id}>
                <td style={{ fontWeight: 700 }}>
                  {editing === row.id ? (
                    <input
                      className="inp"
                      autoFocus
                      value={editName}
                      onChange={(e) => setEditName(e.target.value)}
                      onKeyDown={(e) => {
                        if (e.key === 'Enter') void saveRename(row)
                        if (e.key === 'Escape') reset()
                      }}
                      style={{ maxWidth: 260 }}
                    />
                  ) : row.name}
                </td>
                <td style={{ width: 90 }} className="muted">{row.count} işçi</td>
                <td style={{ width: 340, textAlign: 'right' }}>
                  {editing === row.id ? (
                    <div style={{ display: 'flex', gap: 6, justifyContent: 'flex-end' }}>
                      <button className="btn btn-sm btn-primary" disabled={busy} onClick={() => void saveRename(row)}>
                        Yadda saxla
                      </button>
                      <button className="btn btn-sm" onClick={() => reset()}>Ləğv</button>
                    </div>
                  ) : mergeFrom === row.name ? (
                    <MergeControls
                      options={catalogue.filter((c) => c.name !== row.name).map((c) => c.name)}
                      value={mergeInto}
                      onChange={setMergeInto}
                      onCancel={() => reset()}
                      onConfirm={() => void doMerge(row)}
                      busy={busy}
                    />
                  ) : (
                    <div style={{ display: 'flex', gap: 6, justifyContent: 'flex-end' }}>
                      <button className="btn btn-sm" onClick={() => { setEditing(row.id); setEditName(row.name); setErr(null) }}>
                        Adını dəyiş
                      </button>
                      {row.count > 0 && (
                        <button className="btn btn-sm" onClick={() => { setMergeFrom(row.name); setMergeInto(''); setErr(null) }}>
                          Birləşdir
                        </button>
                      )}
                      <button className="btn btn-sm btn-danger" disabled={busy} onClick={() => void remove(row)}>
                        Sil
                      </button>
                    </div>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
        <div className="muted" style={{ fontSize: 12, marginTop: 12 }}>
          Adını dəyişəndə həmin vəzifədəki bütün işçilər yeni ada keçir. Mövcud bir adı yazsanız, iki
          vəzifə birləşir.
        </div>
      </div>
    </div>
  )
}

/** Pick the surviving title. A free-text box here would recreate the duplicate it is meant to fix. */
function MergeControls({
  options, value, onChange, onCancel, onConfirm, busy,
}: {
  options: string[]
  value: string
  onChange: (v: string) => void
  onCancel: () => void
  onConfirm: () => void
  busy: boolean
}) {
  return (
    <div style={{ display: 'flex', gap: 6, justifyContent: 'flex-end', alignItems: 'center' }}>
      <select className="inp" value={value} onChange={(e) => onChange(e.target.value)} style={{ maxWidth: 200 }}>
        <option value="">Hansına birləşsin?</option>
        {options.map((n) => <option key={n} value={n}>{n}</option>)}
      </select>
      <button className="btn btn-sm btn-primary" disabled={busy || !value} onClick={onConfirm}>Birləşdir</button>
      <button className="btn btn-sm" onClick={onCancel}>Ləğv</button>
    </div>
  )
}
