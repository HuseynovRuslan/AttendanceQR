import { useEffect, useRef, useState } from 'react'
import type { GroupCompany, GroupOverview, GroupSite } from '../../api/hq'
import { viewTenant } from '../../api/admin'
import { startImpersonation } from '../../api/client'
import { fmt, timeOf } from './format'

/** What the focus trap treats as reachable inside the panel. */
const FOCUSABLE = 'a[href], button:not([disabled]), input, [tabindex]:not([tabindex="-1"])'

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
 * company, over the map he was reading. Reading it costs no request and touches no token.
 *
 * The full panel is still one deliberate click away, and it MINTS A SESSION on this origin rather
 * than linking to the company's own subdomain. A cross-origin link was tried and is a dead end: the
 * token lives in localStorage, which is per-origin, so the new tab arrives holding nothing and falls
 * through to /login — and login on a tenant subdomain is tenant-scoped, while the group head's
 * employee row exists in exactly ONE of the five companies. That link would have worked for that one
 * company and shown a login form for the other four. Signing in there is not something he can do;
 * a read-only session is the entire reason that endpoint exists.
 *
 * Doing it again is safe only because the trip is no longer one-way: the board's refusal screen now
 * recognises a session and offers to leave it, and the banner's «Çıx» returns to the screen the
 * session began on (impersonationReturnPath) rather than to /tenants, which is not a route on every
 * host a session can now start from.
 */
export function CompanyDrawer({ company, companyIndex, accent, sites, feed, onClose }: CompanyDrawerProps) {
  const [opening, setOpening] = useState(false)
  const [openError, setOpenError] = useState(false)
  const panelRef = useRef<HTMLElement | null>(null)
  const closeRef = useRef<HTMLButtonElement | null>(null)

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') { onClose(); return }
      // Keep Tab inside the panel. It carries aria-modal, which tells a screen reader the rest of the
      // page is unavailable; letting focus walk out into a board the reader was just told is not
      // there is the contradiction that turns that attribute into a lie.
      if (e.key !== 'Tab' || !panelRef.current) return
      const focusable = panelRef.current.querySelectorAll<HTMLElement>(FOCUSABLE)
      if (focusable.length === 0) return
      const first = focusable[0]
      const last = focusable[focusable.length - 1]
      if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus() }
      else if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus() }
    }
    window.addEventListener('keydown', onKey)

    // Move focus in, and put it back on the way out — otherwise a keyboard reader is returned to the
    // top of the document rather than to the card they opened.
    const returnTo = document.activeElement as HTMLElement | null
    closeRef.current?.focus()
    // The board behind is a scrolling page; letting it scroll under a modal is how a reader loses
    // the place they came back to.
    const prevOverflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    return () => {
      window.removeEventListener('keydown', onKey)
      document.body.style.overflow = prevOverflow
      returnTo?.focus?.()
    }
  }, [onClose])

  /** Mint a read-only session in this company and go to its panel — on THIS origin, the only place
   *  the token can be stored. Every mutating request in that session is refused server-side by
   *  ViewOnlyBoundary, so «yalnız oxu» is enforced rather than merely promised. */
  async function openPanel() {
    setOpening(true)
    setOpenError(false)
    const { status, data } = await viewTenant(company.id)
    if (status === 200 && data && !('error' in data)) {
      startImpersonation(data.token, { tenantName: data.tenantName, adminName: data.adminName })
      window.location.href = '/admin'
      return
    }
    // Say so, rather than leaving a button that does nothing when pressed.
    setOpening(false)
    setOpenError(true)
  }

  const companySites = sites
    .filter((s) => s.companyIndex === companyIndex)
    .sort((a, b) => b.onDuty - a.onDuty)
  // By id, not by name — two tenants may carry the same display name, and nothing prevents it.
  const companyFeed = feed.filter((f) => f.companyId === company.id)

  // Everyone the system can actually see. Imported staff who have never once opened the app are not
  // evidence of anything, and this has to match the denominator the percentage beside it uses.
  const observed = company.employees - company.notStarted

  return (
    <div className="hq-drawer-root">
      <div className="hq-drawer-backdrop" onClick={onClose} aria-hidden="true" />
      <aside ref={panelRef} className="hq-drawer" role="dialog" aria-modal="true" aria-label={company.name}>
        <div className="hq-drawer-bar" style={{ background: accent }} />

        <div className="hq-drawer-head">
          <div>
            <h2 className="hq-drawer-title">{company.name}</h2>
            <div className="hq-drawer-sub">
              {company.locations} filial · {fmt.format(company.employees)} işçi
              {company.notStarted > 0 && <> · {fmt.format(company.notStarted)} aktivləşdirməyib</>}
            </div>
          </div>
          <button ref={closeRef} type="button" className="hq-drawer-close" onClick={onClose} aria-label="Bağla">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round">
              <path d="M18 6L6 18M6 6l12 12" />
            </svg>
          </button>
        </div>

        <div className="hq-drawer-action">
          <button
            type="button"
            className="hq-drawer-btn"
            onClick={() => void openPanel()}
            disabled={opening}
            style={{ ['--btn-accent' as string]: accent }}
          >
            <span>{opening ? 'açılır…' : 'Tam admin panelinə keçid'}</span>
            {!opening && (
              <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round">
                <path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6" />
                <polyline points="15 3 21 3 21 9" />
                <line x1="10" y1="14" x2="21" y2="3" />
              </svg>
            )}
          </button>
          <div className="hq-drawer-hint">
            {openError
              ? 'Panel açılmadı — yenidən cəhd edin.'
              : 'Yalnız oxu rejimində açılır; qayıtmaq üçün yuxarıdakı «Çıx».'}
          </div>
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
