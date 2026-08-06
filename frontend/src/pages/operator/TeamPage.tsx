import { useEffect, useState } from 'react'
import { getTeam, setOperatorRole, type TeamMember } from '../../api/admin'

const ROLE_LABEL: Record<string, string> = {
  Full: 'Tam səlahiyyət',
  Support: 'Dəstək',
  Billing: 'Faktura',
}

const ROLE_HELP: Record<string, string> = {
  Full: 'Hər şey — şirkət yaratmaq/söndürmək, plan/qiymət, faktura, istifadəçilər, impersonation, elan, komanda',
  Support: 'Oxumaq + istifadəçi köməyi (PIN sıfırla / aktiv et / sessiya) + impersonation. Şirkət/plan/faktura/elan YOX',
  Billing: 'Oxumaq + ödəniş qeyd etmək. Başqa heç nə',
}

const ERRORS: Record<string, string> = {
  NotAnOperator: 'Bu şəxs operator siyahısında (.env) deyil',
  CannotChangeOwnRole: 'Öz rolunuzu dəyişə bilməzsiniz',
  CannotRemoveLastFullOperator: 'Ən azı bir Tam səlahiyyətli operator qalmalıdır',
  RoleInvalid: 'Rol yanlışdır',
  Forbidden: 'Bunun üçün icazəniz yoxdur',
}

export function TeamPage() {
  const [rows, setRows] = useState<TeamMember[]>([])
  const [roles, setRoles] = useState<string[]>([])
  const [loading, setLoading] = useState(true)
  const [busyId, setBusyId] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  async function load() {
    try {
      const res = await getTeam()
      if (res.status === 200 && res.data && 'rows' in res.data) {
        setRows(res.data.rows)
        setRoles(res.data.roles)
        setError(null)
      } else if (res.status === 403) {
        setError('İcazəniz yoxdur')
      } else {
        setError('Yüklənmədi')
      }
    } catch {
      setError('Yüklənmədi')
    } finally {
      setLoading(false)
    }
  }
  useEffect(() => { void load() }, [])

  async function changeRole(m: TeamMember, role: string) {
    if (role === m.role) return
    if (!window.confirm(`«${m.fullName}» → ${ROLE_LABEL[role] ?? role}?`)) return
    setBusyId(m.employeeId); setError(null)
    const { status, data } = await setOperatorRole(m.employeeId, role)
    setBusyId(null)
    if (status === 200) await load()
    else setError((data && 'error' in data && ERRORS[data.error]) || 'Alınmadı')
  }

  return (
    <div>
      {error && <div className="fb fb-err" style={{ marginBottom: 14 }}>{error}</div>}

      <div className="card">
        <table className="tbl">
          <thead>
            <tr><th>Operator</th><th>Şirkət</th><th>Rol</th></tr>
          </thead>
          <tbody>
            {loading && <tr><td colSpan={3} className="muted" style={{ padding: 18 }}>Yüklənir…</td></tr>}
            {!loading && rows.length === 0 && (
              <tr><td colSpan={3} className="muted" style={{ padding: 18 }}>Operator yoxdur</td></tr>
            )}
            {rows.map((m) => (
              <tr key={m.employeeId} style={{ opacity: m.resolved ? 1 : 0.6 }}>
                <td>
                  <div style={{ fontWeight: 700 }}>
                    {m.fullName}
                    {m.isYou && <span className="tag" style={{ marginLeft: 6, background: 'var(--leaf-bg)', color: 'var(--leaf-d)' }}>siz</span>}
                  </div>
                  {m.phone && <div style={{ fontSize: 11, color: 'var(--c400)' }}>0{m.phone}</div>}
                </td>
                <td style={{ fontSize: 13 }}>{m.tenantName ?? '—'}</td>
                <td>
                  {m.isYou || !m.resolved ? (
                    <span style={{ fontWeight: 600 }}>{ROLE_LABEL[m.role] ?? m.role}</span>
                  ) : (
                    <select
                      className="inp"
                      style={{ maxWidth: 200 }}
                      value={m.role}
                      disabled={busyId === m.employeeId}
                      onChange={(e) => void changeRole(m, e.target.value)}
                    >
                      {roles.map((r) => <option key={r} value={r}>{ROLE_LABEL[r] ?? r}</option>)}
                    </select>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <div className="card card-pad" style={{ marginTop: 16 }}>
        <div className="card-title">Rollar</div>
        {roles.map((r) => (
          <div key={r} style={{ marginBottom: 8 }}>
            <b>{ROLE_LABEL[r] ?? r}</b>
            <span className="muted" style={{ fontSize: 13 }}> — {ROLE_HELP[r] ?? ''}</span>
          </div>
        ))}
        <div className="muted" style={{ fontSize: 12, marginTop: 8 }}>
          Kim operator olması <code>.env</code>-də təyin olunur (yeni operator əlavə etmək üçün deploy lazımdır) —
          burada yalnız mövcud operatorların rolları dəyişilir. Öz rolunuzu dəyişə bilməzsiniz.
        </div>
      </div>
    </div>
  )
}
