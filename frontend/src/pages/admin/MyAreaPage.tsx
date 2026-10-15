import { Link } from 'react-router-dom'
import { useAuth } from '../../auth/AuthContext'
import { FEATURE, useFeatureEnabled } from '../../branding/BrandingContext'
import { useAccess } from '../../auth/AccessContext'
import { groupPermissions, SCOPE_LABEL, TEMPLATE_HINT } from '../../lib/accessGroups'
import { PANEL_PAGES, pagesAfterSwitch, pagesFor } from '../../lib/panelAccess'

const MODE_NOTE: Record<string, string> = {
  Legacy: 'Şirkət hələ köhnə qaydadadır: panelin hansı bölmələrinin açılması bugünkü rolunuzdan asılıdır.',
  Shadow: 'Şirkət yoxlama rejimindədir: yeni qayda sakitcə köhnə ilə müqayisə olunur, amma qərarı hələ köhnə qayda verir.',
  Enforced: 'Şirkət yeni səlahiyyət qaydasına keçib.',
}

/**
 * «Mənim sahəm» — what I hold, where, and what the panel opens for me.
 *
 * The landing page of somebody whose panel comes only from a role in the new model (a timekeeper who is
 * plain staff today): until the switch their role opens nothing else, and a panel with an empty menu and no
 * word about why reads as broken. So it says it — this is your area, this is what you may do in it, these
 * sections open after the switch. Everybody else in the panel sees the same card for their own access.
 */
export function MyAreaPage() {
  const { role } = useAuth()
  const { access, loading } = useAccess()
  // The menu hides a page the company has switched off; this card follows it, or it would link to one.
  const switchedOff: Record<string, boolean> = {
    payroll: !useFeatureEnabled(FEATURE.Payroll),
    announcements: !useFeatureEnabled(FEATURE.Announcements),
  }

  if (loading && !access) return <div className="muted">Yüklənir…</div>
  if (!access) {
    return (
      <div className="fb fb-err">
        <span>Səlahiyyət məlumatı gəlmədi — səhifəni yeniləyin.</span>
      </div>
    )
  }

  const branchName = new Map(access.branches.map((b) => [b.id, b.name]))
  // Branch assignments of one role read as one line with their branches, not one line a branch.
  const lines = new Map<string, { role: string; hint: string | null; areas: string[] }>()
  for (const a of access.assignments) {
    const key = `${a.roleId}|${a.scope}|${a.structureId ?? ''}`
    const line = lines.get(key) ?? {
      role: a.roleName ?? 'Rol',
      hint: a.template ? TEMPLATE_HINT[a.template] ?? null : null,
      areas: [],
    }
    if (a.scope === 'Company') line.areas.push(SCOPE_LABEL.Company)
    else if (a.scope === 'Structure') line.areas.push(`Struktur: ${a.structureName ?? '—'}`)
    else line.areas.push(...a.locationIds.map((id) => branchName.get(id) ?? '—'))
    lines.set(key, line)
  }

  const labels = new Map(access.permissions.map((p) => [p.key, p.label]))
  const company = new Set(access.permissions.filter((p) => p.company).map((p) => p.key))
  const groups = groupPermissions(access.permissions.map((p) => p.key))
  const openNow = pagesFor(role, access)
  const later = pagesAfterSwitch(role, access)
  const menuPages = PANEL_PAGES.filter((p) => p.menu !== false && p.path !== 'my-area' && !switchedOff[p.path])

  return (
    <div style={{ display: 'grid', gap: 16, maxWidth: 880 }}>
      <div className="card card-pad">
        <div className="card-title">Rollarım</div>
        {lines.size === 0 ? (
          <p className="muted" style={{ fontSize: 13, margin: '6px 0 0' }}>
            Sizə yeni qaydada rol verilməyib. Panel bugünkü rolunuzla açılır.
          </p>
        ) : (
          <div className="acc-lines">
            {[...lines.values()].map((l, i) => (
              <div key={i} className="acc-line">
                <div className="acc-line-role">{l.role}</div>
                {l.hint && <div className="muted" style={{ fontSize: 12 }}>{l.hint}</div>}
                <div className="acc-line-area">{l.areas.join(' · ')}</div>
              </div>
            ))}
          </div>
        )}
        <p className="muted" style={{ fontSize: 12, margin: '12px 0 0', lineHeight: 1.6 }}>
          {MODE_NOTE[access.mode] ?? ''} Rol verilən və ya alınan kimi növbəti açılışda görünür — çıxıb-girmək lazım
          deyil.
        </p>
      </div>

      {access.branches.length > 0 && (
        <div className="card card-pad">
          <div className="card-title">Filiallarım ({access.branches.length})</div>
          <div className="acc-chips" style={{ marginTop: 8 }}>
            {access.branches.map((b) => <span key={b.id} className="acc-chip">{b.name}</span>)}
          </div>
        </div>
      )}

      <div className="card card-pad">
        <div className="card-title">Panel bölmələri</div>
        <div className="acc-split">
          <div>
            <div className="acc-label">İndi açıqdır</div>
            <ul className="acc-pages">
              {menuPages.filter((p) => openNow.has(p.path)).map((p) => (
                <li key={p.path}><Link to={`/admin/${p.path}`}>{p.label}</Link></li>
              ))}
            </ul>
          </div>
          {later.size > 0 && (
            <div>
              <div className="acc-label">Keçiddən sonra açılacaq</div>
              <ul className="acc-pages later">
                {menuPages.filter((p) => later.has(p.path)).map((p) => <li key={p.path}>{p.label}</li>)}
              </ul>
            </div>
          )}
        </div>
      </div>

      {groups.length > 0 && (
        <div className="card card-pad">
          <div className="card-title">Hüquqlarım</div>
          <div className="acc-groups">
            {groups.map((g) => (
              <div key={g.title}>
                <div className="acc-label">{g.title}</div>
                <ul className="acc-perms">
                  {g.keys.map((k) => (
                    <li key={k}>
                      {labels.get(k) ?? k}
                      {!company.has(k) && access.branches.length > 0 && <span className="muted"> — öz sahəmdə</span>}
                    </li>
                  ))}
                </ul>
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  )
}
