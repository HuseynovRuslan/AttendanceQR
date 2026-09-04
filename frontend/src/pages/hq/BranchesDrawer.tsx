import { useMemo, useState } from 'react'
import type { GroupCompany, GroupSite } from '../../api/hq'
import { HqDrawer } from './HqDrawer'
import { fmt } from './format'

/**
 * Every branch of every company, in one list.
 *
 * The map already shows where they are; what it cannot show is which ones are EMPTY — a site with
 * nobody on it draws the smallest marker on the screen, so the thing most worth noticing is the
 * thing hardest to see. This list inverts that: the empty ones come first.
 *
 * Pure client-side. It is the same `sites` the map is drawn from, so opening it costs no request and
 * the figures cannot disagree with the map beside them.
 */
export function BranchesDrawer({
  sites, companies, accentOf, onClose,
}: {
  sites: GroupSite[]
  companies: GroupCompany[]
  accentOf: (i: number) => string
  onClose: () => void
}) {
  const [query, setQuery] = useState('')
  const [emptyFirst, setEmptyFirst] = useState(true)

  const rows = useMemo(() => {
    const q = query.trim().toLocaleLowerCase('az-AZ')
    const named = sites.map((s) => ({
      ...s,
      company: companies[s.companyIndex]?.name ?? '',
    }))
    const filtered = q
      ? named.filter((s) =>
          s.name.toLocaleLowerCase('az-AZ').includes(q) ||
          s.company.toLocaleLowerCase('az-AZ').includes(q))
      : named
    // Empty first is the default because that is the question this panel answers. The other order is
    // for reading the day the ordinary way round.
    return [...filtered].sort((a, b) =>
      emptyFirst ? a.onDuty - b.onDuty || b.staff - a.staff : b.onDuty - a.onDuty)
  }, [sites, companies, query, emptyFirst])

  const empty = sites.filter((s) => s.onDuty === 0).length

  return (
    <HqDrawer
      title="Filiallar"
      subtitle={`${fmt.format(sites.length)} filial · hazırda ${fmt.format(empty)}-də heç kim yoxdur`}
      accent="#38BDF8"
      onClose={onClose}
      above={
        <div className="hq-drawer-tools">
          <input
            className="hq-feed-search"
            type="search"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder="Filial və ya şirkət…"
            aria-label="Filiallarda axtar"
          />
          <div className="hq-chips">
            <button
              type="button"
              className={`hq-chip${emptyFirst ? ' is-on' : ''}`}
              onClick={() => setEmptyFirst(true)}
            >
              Boşlar əvvəl
            </button>
            <button
              type="button"
              className={`hq-chip${!emptyFirst ? ' is-on' : ''}`}
              onClick={() => setEmptyFirst(false)}
            >
              Ən doluları əvvəl
            </button>
          </div>
        </div>
      }
    >
      {rows.length === 0 ? (
        <p className="hq-drawer-empty">Nəticə yoxdur.</p>
      ) : (
        <div className="hq-drawer-sites">
          {rows.map((s) => (
            <div key={s.id} className={`hq-drawer-site${s.onDuty === 0 ? ' is-idle' : ''}`}>
              <span
                className="hq-drawer-site-dot"
                style={{ background: accentOf(s.companyIndex < 0 ? 0 : s.companyIndex) }}
              />
              <div className="hq-ns-main">
                <div className="hq-drawer-site-name">{s.name}</div>
                <div className="hq-drawer-feed-loc">{s.company}</div>
              </div>
              <span className="hq-drawer-site-val hq-num">
                <b style={{ color: s.onDuty > 0 ? 'var(--live)' : 'var(--fg-faint)' }}>{s.onDuty}</b>
                <small>/ {s.staff || '—'}</small>
              </span>
            </div>
          ))}
        </div>
      )}
    </HqDrawer>
  )
}
