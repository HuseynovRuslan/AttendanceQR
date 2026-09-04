import { useEffect, useMemo, useState } from 'react'
import { getNotStarted, type NotStartedRow } from '../../api/hq'
import { HqDrawer } from './HqDrawer'
import { fmt } from './format'

/**
 * Who has never once opened the app.
 *
 * This is the board's largest number with no story attached: 335 of 656 active staff, 310 of them at
 * one company. Until this panel it was stated and nowhere explained, which makes it a figure a
 * director can only worry about rather than act on.
 *
 * So the list is cut the way the NEXT step is decided, not alphabetically. Three states, each one
 * somebody else's to fix — and the split was chosen against production data, not guessed:
 *   • no number on file (2)        → the company must supply one; nothing technical helps
 *   • never logged in (250)        → they hold an account and the temporary PIN never reached them.
 *                                    A distribution problem: somebody has to hand out PINs.
 *   • opened the app, no scan (77) → the account works, they got in, and no scan ever landed. The
 *                                    poster, the geofence or the camera failed them. THIS is the
 *                                    list worth phoning, and it was invisible until now.
 *
 * «Activated» is deliberately not one of them: the bulk import stamps ActivatedAtUtc on everyone it
 * creates, so it was true for 309 of 309 at one company and separated nobody from anybody.
 */
export function NotStartedDrawer({ accent, onClose }: { accent: string; onClose: () => void }) {
  const [rows, setRows] = useState<NotStartedRow[] | null>(null)
  const [failed, setFailed] = useState(false)
  const [query, setQuery] = useState('')
  const [only, setOnly] = useState<'all' | 'nophone' | 'nologin' | 'opened'>('all')

  useEffect(() => {
    let alive = true
    void (async () => {
      const { status, data } = await getNotStarted()
      if (!alive) return
      if (status === 200 && data && 'rows' in data) setRows(data.rows)
      else setFailed(true)
    })()
    return () => { alive = false }
  }, [])

  // Order matters: no number beats everything (nothing else is actionable without one), and having
  // opened the app beats never logging in, because it is the stronger evidence about this person.
  const bucketOf = (r: NotStartedRow) =>
    !r.hasPhone ? 'nophone' : r.openedApp ? 'opened' : 'nologin'

  const counts = useMemo(() => {
    const c = { nophone: 0, nologin: 0, opened: 0 }
    for (const r of rows ?? []) c[bucketOf(r)]++
    return c
  }, [rows])

  const shown = useMemo(() => {
    const q = query.trim().toLocaleLowerCase('az-AZ')
    return (rows ?? []).filter((r) => {
      if (only !== 'all' && bucketOf(r) !== only) return false
      if (!q) return true
      return r.fullName.toLocaleLowerCase('az-AZ').includes(q) ||
             r.company.toLocaleLowerCase('az-AZ').includes(q) ||
             r.location.toLocaleLowerCase('az-AZ').includes(q) ||
             (r.position ?? '').toLocaleLowerCase('az-AZ').includes(q)
    })
  }, [rows, query, only])

  const FILTERS: { key: typeof only; label: string; count: number }[] = [
    { key: 'all', label: 'Hamısı', count: rows?.length ?? 0 },
    { key: 'opened', label: 'Tətbiqi açıb, skan yox', count: counts.opened },
    { key: 'nologin', label: 'Heç vaxt giriş etməyib', count: counts.nologin },
    { key: 'nophone', label: 'Nömrəsi yoxdur', count: counts.nophone },
  ]

  return (
    <HqDrawer
      title="Tətbiqi açmayanlar"
      subtitle={rows === null ? 'yüklənir…' : `${fmt.format(rows.length)} nəfər · davamiyyət faizinə daxil deyil`}
      accent={accent}
      onClose={onClose}
      above={
        <div className="hq-drawer-tools">
          <input
            className="hq-feed-search"
            type="search"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder="Ad, şirkət, filial və ya vəzifə…"
            aria-label="Siyahıda axtar"
          />
          <div className="hq-chips">
            {FILTERS.map((f) => (
              <button
                key={f.key}
                type="button"
                className={`hq-chip${only === f.key ? ' is-on' : ''}`}
                onClick={() => setOnly(f.key)}
              >
                {f.label} <b>{fmt.format(f.count)}</b>
              </button>
            ))}
          </div>
        </div>
      }
    >
      {failed && <p className="hq-drawer-empty">Siyahı gəlmədi — yenidən cəhd edin.</p>}
      {rows === null && !failed && <p className="hq-drawer-empty">Yüklənir…</p>}
      {rows !== null && shown.length === 0 && (
        <p className="hq-drawer-empty">Bu şərtlərə uyğun nəticə yoxdur.</p>
      )}
      {shown.length > 0 && (
        <div className="hq-drawer-sites">
          {shown.map((r) => {
            const b = bucketOf(r)
            return (
              <div key={r.id} className="hq-ns-row">
                <div className="hq-ns-main">
                  <div className="hq-drawer-feed-name">{r.fullName}</div>
                  <div className="hq-drawer-feed-loc">
                    {r.company}{r.location ? ` · ${r.location}` : ''}{r.position ? ` · ${r.position}` : ''}
                    {r.daysSince !== null && (
                      <> · {r.daysSince === 0 ? 'bu gün açıb' : `${r.daysSince} gün əvvəl açıb`}</>
                    )}
                  </div>
                </div>
                <span className={`hq-ns-tag is-${b}`} title={
                  b === 'nophone' ? 'Nömrəsi yoxdur — şirkət verməlidir'
                    : b === 'opened' ? 'Tətbiqi açıb, amma bir dəfə də skan etməyib — poster, geofence və ya kamera'
                      : 'Müvəqqəti PIN-lə qalıb — heç vaxt giriş etməyib'}>
                  {b === 'nophone' ? 'nömrəsiz' : b === 'opened' ? 'skan yox' : 'giriş yox'}
                </span>
              </div>
            )
          })}
        </div>
      )}
    </HqDrawer>
  )
}
