import { useEffect, useMemo, useState } from 'react'
import {
  downloadPaperRoster,
  getPaperRoster,
  type PaperEmployerCount,
  type PaperPerson,
  type PaperSiteCount,
} from '../../api/hq'

/**
 * «Sənəd üzrə siyahı» — the roster one employer's documents claim, wherever those people stand.
 *
 * This screen exists because no company panel can produce it. Çingiz Hümbətov works at Green Garden
 * and is on Bakı Abadlıq Xidməti's books; his row lives in Green Garden's data, and one company's
 * panel may not read another's. A «sənəd üzrə» switch inside a panel could therefore only ever drop
 * rows, never add them — a file that looks complete and quietly is not.
 *
 * The rule is one expression and lives on the server: a person's paper employer is their
 * PaperEmployer, or their own company when nobody wrote one. Empty means the paperwork agrees.
 */
export function PaperRosterPage() {
  const [employers, setEmployers] = useState<PaperEmployerCount[]>([])
  const [employer, setEmployer] = useState<string>('')
  // Two site filters, kept apart because «ərazi» is ambiguous on this screen and the ambiguity
  // changes the answer: one asks where somebody stands, the other what their documents say.
  const [site, setSite] = useState<string>('')
  const [paperSite, setPaperSite] = useState<string>('')
  const [sites, setSites] = useState<PaperSiteCount[]>([])
  const [paperSites, setPaperSites] = useState<PaperSiteCount[]>([])
  const [onlyElsewhere, setOnlyElsewhere] = useState(false)
  const [rows, setRows] = useState<PaperPerson[]>([])
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState<string | null>(null)

  useEffect(() => {
    void load()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [employer, site, paperSite, onlyElsewhere])

  async function load() {
    setLoading(true)
    setErr(null)
    const res = await getPaperRoster({
      employer: employer || undefined,
      site: site || undefined,
      paperSite: paperSite || undefined,
      onlyElsewhere,
    })
    if (res.status === 200 && res.data) {
      setEmployers(res.data.employers)
      setSites(res.data.sites)
      setPaperSites(res.data.paperSites)
      setRows(res.data.rows)
    } else {
      setErr('Siyahı gəlmədi')
    }
    setLoading(false)
  }

  async function onExport() {
    setBusy(true)
    try {
      await downloadPaperRoster({
        employer: employer || undefined,
        site: site || undefined,
        paperSite: paperSite || undefined,
        onlyElsewhere,
      })
    } catch {
      setErr('Fayl yüklənmədi')
    }
    setBusy(false)
  }

  // Grouped the way somebody reads it: of the people on these books, where are they standing.
  const groups = useMemo(() => {
    const by = new Map<string, PaperPerson[]>()
    for (const r of rows) {
      const key = `${r.actualCompany} · ${r.actualSite}`
      const list = by.get(key)
      if (list) list.push(r)
      else by.set(key, [r])
    }
    return [...by.entries()]
  }, [rows])

  const elsewhereCount = rows.filter((r) => r.elsewhere).length

  return (
    <div className="flex flex-col gap-4">
      <div>
        <h1 className="text-xl font-extrabold">Sənəd üzrə siyahı</h1>
        <p className="text-sm text-slate-500">
          Sənədə görə bir şirkətin işçiləri — faktiki olaraq hansı şirkətdə qeydiyyatda olmasından
          asılı olmayaraq. Bu, davamiyyət deyil, kadr siyahısıdır.
        </p>
      </div>

      <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap', alignItems: 'center' }}>
        <select
          className="inp"
          style={{ width: 'auto' }}
          value={employer}
          onChange={(e) => {
            // Clear the sites with it: a site chosen under the previous employer usually has nobody
            // under the new one, and an empty list reads as a broken screen rather than a stale filter.
            setSite('')
            setPaperSite('')
            setEmployer(e.target.value)
          }}
        >
          <option value="">Bütün qrup</option>
          {employers.map((x) => (
            <option key={x.name} value={x.name}>
              {x.name} — {x.total} nəfər{x.elsewhere > 0 ? ` (${x.elsewhere} kənarda)` : ''}
            </option>
          ))}
        </select>

        <select className="inp" style={{ width: 'auto' }} value={site} onChange={(e) => setSite(e.target.value)}>
          <option value="">Faktiki ərazi — hamısı</option>
          {sites.map((x) => (
            <option key={x.name} value={x.name}>{x.name} — {x.total}</option>
          ))}
        </select>

        {/* Only offered once somebody has actually written one; an empty picker beside a full one
            teaches the reader that the screen is broken rather than that the field is unfilled. */}
        {paperSites.length > 0 && (
          <select className="inp" style={{ width: 'auto' }} value={paperSite} onChange={(e) => setPaperSite(e.target.value)}>
            <option value="">Sənəd üzrə ərazi — hamısı</option>
            {paperSites.map((x) => (
              <option key={x.name} value={x.name}>{x.name} — {x.total}</option>
            ))}
          </select>
        )}

        <label style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 14, fontWeight: 600 }}>
          <input type="checkbox" checked={onlyElsewhere} onChange={(e) => setOnlyElsewhere(e.target.checked)} />
          Yalnız başqa şirkətdə işləyənlər
        </label>

        <button className="btn" style={{ marginLeft: 'auto' }} onClick={onExport} disabled={busy || rows.length === 0}>
          {busy ? 'Hazırlanır…' : 'Excel export'}
        </button>
      </div>

      {err && <div className="alert alert-err">{err}</div>}

      {!loading && rows.length > 0 && (
        <div className="muted" style={{ fontSize: 13 }}>
          {rows.length} nəfər
          {elsewhereCount > 0 && <> · <b style={{ color: '#b45309' }}>{elsewhereCount}</b> nəfər başqa şirkətdə işləyir</>}
        </div>
      )}

      {loading ? (
        <div className="muted">Yüklənir…</div>
      ) : rows.length === 0 ? (
        <div className="muted" style={{ padding: 24, textAlign: 'center' }}>
          Bu şərtlərə uyğun işçi yoxdur.
        </div>
      ) : (
        // `group`, not `site` — the state variable of that name is the FILTER, and shadowing it here
        // would let a later edit read the group heading as the chosen filter without a type error.
        groups.map(([group, people]) => (
          <div key={group}>
            <div style={{ fontWeight: 800, margin: '6px 0 6px' }}>
              {group} <span className="muted" style={{ fontWeight: 600 }}>· {people.length}</span>
            </div>
            <div className="tbl-wrap">
              <table>
                <thead>
                  <tr>
                    <th>Ad Soyad</th>
                    <th>Vəzifə</th>
                    <th>Sənəd üzrə</th>
                    <th>Telefon</th>
                  </tr>
                </thead>
                <tbody>
                  {people.map((p) => (
                    <tr key={p.id} style={{ opacity: p.isActive ? 1 : 0.55 }}>
                      <td data-label="Ad Soyad" style={{ fontWeight: 700 }}>{p.fullName}</td>
                      <td data-label="Vəzifə">{p.position || '—'}</td>
                      {/* The only cell where the two answers sit side by side — the reason the file
                          exists. Quiet when they agree, so the eye lands on the ones that do not. */}
                      <td data-label="Sənəd üzrə">
                        {p.elsewhere ? (
                          <span style={{ color: '#b45309', fontWeight: 700 }}>
                            {p.paperEmployer}{p.paperSite ? ` / ${p.paperSite}` : ''}
                          </span>
                        ) : (
                          <span className="muted">eyni şirkət</span>
                        )}
                      </td>
                      <td data-label="Telefon" className="mono">{p.phoneNumber ? `0${p.phoneNumber}` : '—'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        ))
      )}
    </div>
  )
}
