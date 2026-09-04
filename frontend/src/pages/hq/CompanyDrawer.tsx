import { useEffect } from 'react'
import type { GroupCompany, GroupOverview, GroupSite } from '../../api/hq'
import { fmt, timeOf } from './format'

interface CompanyDrawerProps {
  company: GroupCompany
  companyIndex: number
  accent: string
  sites: GroupSite[]
  feed: GroupOverview['feed']
  onClose: () => void
}

/**
 * One company, opened from its card on the group board — without leaving the board.
 *
 * It replaces what the card used to do. Clicking a card minted a read-only session inside that
 * company and navigated away, which cost two things: the wall board went dark mid-look, and — worse
 * — the group head's own token was swapped for a tenant one, so coming BACK to /hq answered 403 with
 * a sentence and no way out of it. He was locked out of his own board by his own click
 * (SuperAdminAuditLogs, ViewStarted 2026-09-04 09:56 Baku).
 *
 * So the detail comes to him instead: the same payload the board already fetched, cut to one
 * company, over the map he was reading. Nothing here calls the API and nothing touches the token.
 * The one door out is the explicit link below, and it opens in a NEW TAB on that company's own
 * subdomain — a different origin, so whatever happens over there, this tab's session is untouched.
 */
export function CompanyDrawer({ company, companyIndex, accent, sites, feed, onClose }: CompanyDrawerProps) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose() }
    window.addEventListener('keydown', onKey)
    // The board behind is a scrolling page; letting it scroll under a modal is how a reader loses
    // the place they came back to.
    const prevOverflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    return () => {
      window.removeEventListener('keydown', onKey)
      document.body.style.overflow = prevOverflow
    }
  }, [onClose])

  const companySites = sites
    .filter((s) => s.companyIndex === companyIndex)
    .sort((a, b) => b.onDuty - a.onDuty)
  // By id, not by name — two tenants may carry the same display name, and nothing prevents it.
  const companyFeed = feed.filter((f) => f.companyId === company.id)

  // On qrlog.az every company has its own subdomain; anywhere else (localhost, a staging host) there
  // are no subdomains to send anyone to, so the link stays on this host.
  const adminUrl = window.location.hostname.endsWith('qrlog.az')
    ? `https://${company.slug}.qrlog.az/admin`
    : '/admin'

  // Everyone the system can actually see. Imported staff who have never once opened the app are not
  // evidence of anything, and this has to match the denominator the percentage beside it uses.
  const observed = company.employees - company.notStarted

  return (
    <div className="hq-drawer-root">
      <div className="hq-drawer-backdrop" onClick={onClose} aria-hidden="true" />
      <aside className="hq-drawer" role="dialog" aria-modal="true" aria-label={company.name}>
        <div className="hq-drawer-bar" style={{ background: accent }} />

        <div className="hq-drawer-head">
          <div>
            <h2 className="hq-drawer-title">{company.name}</h2>
            <div className="hq-drawer-sub">
              {company.locations} filial · {fmt.format(company.employees)} işçi
              {company.notStarted > 0 && <> · {fmt.format(company.notStarted)} aktivləşdirməyib</>}
            </div>
          </div>
          <button type="button" className="hq-drawer-close" onClick={onClose} aria-label="Bağla">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round">
              <path d="M18 6L6 18M6 6l12 12" />
            </svg>
          </button>
        </div>

        <div className="hq-drawer-action">
          <a
            href={adminUrl}
            target="_blank"
            rel="noopener noreferrer"
            className="hq-drawer-btn"
            style={{ ['--btn-accent' as string]: accent }}
          >
            <span>Tam admin panelinə keçid</span>
            <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round">
              <path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6" />
              <polyline points="15 3 21 3 21 9" />
              <line x1="10" y1="14" x2="21" y2="3" />
            </svg>
          </a>
          <div className="hq-drawer-hint">Yeni vərəqdə açılır — bu lövhə yerində qalır.</div>
        </div>

        <div className="hq-drawer-kpi">
          <div className="hq-drawer-stat">
            <div className="v hq-num">
              {fmt.format(company.onDuty)}<small>/ {fmt.format(observed)}</small>
            </div>
            <div className="l">İndi iş başında</div>
          </div>
          <div className="hq-drawer-stat">
            <div className="v hq-num" style={{ color: accent }}>{company.attendancePct}%</div>
            <div className="l">Davamiyyət</div>
          </div>
          <div className="hq-drawer-stat">
            <div className="v hq-num">{fmt.format(company.present)}</div>
            <div className="l">Bu gün gələn</div>
          </div>
          <div className="hq-drawer-stat">
            <div className="v hq-num">{company.locations}</div>
            <div className="l">Filial</div>
          </div>
        </div>

        <div className="hq-drawer-body">
          <section className="hq-drawer-sec">
            <div className="hq-drawer-sec-title">Filiallar üzrə canlı vəziyyət ({companySites.length})</div>
            {companySites.length === 0 ? (
              <p className="hq-drawer-empty">Bu şirkətə təyin edilmiş filial yoxdur.</p>
            ) : (
              <div className="hq-drawer-sites">
                {companySites.map((s) => (
                  <div key={s.id} className={`hq-drawer-site${s.onDuty === 0 ? ' is-idle' : ''}`}>
                    <span className="hq-drawer-site-dot" style={{ background: accent }} />
                    <span className="hq-drawer-site-name">{s.name}</span>
                    <span className="hq-drawer-site-val hq-num">
                      <b style={{ color: s.onDuty > 0 ? 'var(--live)' : 'var(--fg-faint)' }}>{s.onDuty}</b>
                      <small>/ {s.staff || '—'}</small>
                    </span>
                  </div>
                ))}
              </div>
            )}
          </section>

          <section className="hq-drawer-sec">
            <div className="hq-drawer-sec-title">Şirkətin canlı hərəkəti ({companyFeed.length})</div>
            {companyFeed.length === 0 ? (
              <p className="hq-drawer-empty">Bu gün bu şirkət üzrə hələ skan qeydə alınmayıb.</p>
            ) : (
              <div className="hq-drawer-feed">
                {companyFeed.map((f, idx) => (
                  <div key={`${f.fullName}-${f.atUtc}-${f.kind}-${idx}`} className="hq-drawer-feed-row">
                    <span className="hq-drawer-feed-time hq-num">{timeOf(f.atUtc)}</span>
                    <div className="hq-drawer-feed-info">
                      <div className="hq-drawer-feed-name">{f.fullName}</div>
                      <div className="hq-drawer-feed-loc">{f.location || 'Filial qeyd olunmayıb'}</div>
                    </div>
                    {/* Four kinds, not two: a «səyyar» visit is an arrival at a site with no poster. */}
                    <span className={`hq-feed-kind ${f.kind.endsWith('in') ? 'hq-in' : 'hq-out'}`}>
                      {f.kind.startsWith('field') ? '📍 ' : ''}{f.kind.endsWith('in') ? 'GİRİŞ' : 'ÇIXIŞ'}
                    </span>
                  </div>
                ))}
              </div>
            )}
          </section>
        </div>
      </aside>
    </div>
  )
}
