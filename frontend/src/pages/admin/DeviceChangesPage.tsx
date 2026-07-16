import { useEffect, useState } from 'react'
import { EmployeeLink } from '../../components/EmployeeLink'
import {
  approveDeviceChange,
  getDeviceBindings,
  getPendingDeviceChanges,
  rejectDeviceChange,
  revokeDeviceBinding,
  type DeviceBinding,
  type PendingDeviceChange,
} from '../../api/admin'
import { usePolling } from '../../lib/usePolling'
import { IconCheck, IconPhone, IconTrash, IconX } from '../../components/icons'
import { fmtDateTime } from '../../lib/format'

const ORIGIN: Record<DeviceBinding['boundVia'], { label: string; cls: string }> = {
  Activation: { label: 'Aktivləşdirmə', cls: 'bg-slate-100 text-slate-600' },
  AdminApproval: { label: 'Admin təsdiqi', cls: 'bg-blue-100 text-blue-700' },
  AutoBind: { label: 'Avtomatik', cls: 'bg-amber-100 text-amber-700' },
}

export function DeviceChangesPage() {
  const [rows, setRows] = useState<PendingDeviceChange[]>([])
  const [error, setError] = useState<string | null>(null)
  const [loadedOnce, setLoadedOnce] = useState(false)
  const [busyId, setBusyId] = useState<string | null>(null)

  async function refresh() {
    const { status, data } = await getPendingDeviceChanges()
    if (status === 200 && Array.isArray(data)) {
      setRows(data)
      setError(null)
    } else if (status === 403) {
      setError('İcazəniz yoxdur')
    } else {
      setError('Yüklənmədi')
    }
    setLoadedOnce(true)
  }

  usePolling(refresh, 30_000)

  async function act(id: string, kind: 'approve' | 'reject') {
    setBusyId(id)
    const call = kind === 'approve' ? approveDeviceChange : rejectDeviceChange
    const { status } = await call(id)
    if (status === 200) {
      setRows((prev) => prev.filter((r) => r.requestId !== id))
    } else {
      setError(kind === 'approve' ? 'Təsdiq alınmadı' : 'Rədd alınmadı')
    }
    await refresh()
    setBusyId(null)
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 24 }}>
      {error && (
        <div className="fb fb-err">
          <IconX />
          <span>{error}</span>
        </div>
      )}

      <section>
        <h2 style={{ fontSize: 15, fontWeight: 800, color: 'var(--c900)', marginBottom: 10 }}>
          Gözləyən tələblər
        </h2>
        {loadedOnce && rows.length === 0 && !error ? (
          <div className="card card-pad muted" style={{ textAlign: 'center', padding: 28 }}>
            Gözləyən tələb yoxdur
          </div>
        ) : (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
            {rows.map((r) => (
              <div
                key={r.requestId}
                className="card card-pad"
                style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', justifyContent: 'space-between', gap: 16 }}
              >
                <div style={{ minWidth: 0 }}>
                  <div style={{ fontWeight: 700, color: 'var(--c900)', display: 'flex', alignItems: 'center', gap: 8 }}>
                    <IconPhone /> <EmployeeLink id={r.employeeId} name={r.employeeName} />
                  </div>
                  <div style={{ fontSize: 13, color: 'var(--c500)', marginTop: 4 }}>
                    Köhnə: <span className="mono">{r.currentDeviceFingerprint ?? '—'}</span> → Yeni:{' '}
                    <span className="mono">{r.newDeviceFingerprint}</span>
                  </div>
                  <div style={{ fontSize: 11, color: 'var(--c400)', marginTop: 2 }}>
                    {new Date(r.requestedAtUtc).toLocaleString('az-AZ')}
                  </div>
                </div>
                <div style={{ display: 'flex', gap: 8 }}>
                  <button className="btn btn-primary btn-sm" disabled={busyId === r.requestId} onClick={() => act(r.requestId, 'approve')}>
                    <IconCheck /> Təsdiqlə
                  </button>
                  <button className="btn btn-danger btn-sm" disabled={busyId === r.requestId} onClick={() => act(r.requestId, 'reject')}>
                    <IconX /> Rədd et
                  </button>
                </div>
              </div>
            ))}
          </div>
        )}
      </section>

      <BoundDevices />
    </div>
  )
}

/** Every active binding. An employee legitimately holds several (Safari + the installed PWA), so the
 *  signal to look for is not "more than one" but an unfamiliar device on somebody's row. */
function BoundDevices() {
  const [all, setAll] = useState<DeviceBinding[] | null>(null)
  const [autoOnly, setAutoOnly] = useState(false)
  const [busyId, setBusyId] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    void load()
  }, [])

  async function load() {
    const { status, data } = await getDeviceBindings()
    if (status === 200 && Array.isArray(data)) setAll(data)
    else setError('Cihaz siyahısı yüklənmədi')
  }

  async function revoke(b: DeviceBinding) {
    if (!confirm(`${b.employeeName} — "${b.deviceLabel ?? 'cihaz'}" ləğv edilsin?\n\nBu cihaz bir daha avtomatik bağlanmayacaq; yalnız admin təsdiqi ilə qayıda bilər.`))
      return
    setBusyId(b.id)
    const { status } = await revokeDeviceBinding(b.id)
    if (status === 200) setAll((prev) => (prev ?? []).filter((x) => x.id !== b.id))
    else setError('Ləğv edilmədi')
    setBusyId(null)
  }

  const rows = (all ?? []).filter((b) => !autoOnly || b.boundVia === 'AutoBind')
  const autoCount = (all ?? []).filter((b) => b.boundVia === 'AutoBind').length

  return (
    <section>
      <div style={{ display: 'flex', alignItems: 'baseline', justifyContent: 'space-between', gap: 12, flexWrap: 'wrap', marginBottom: 4 }}>
        <h2 style={{ fontSize: 15, fontWeight: 800, color: 'var(--c900)' }}>Bağlı cihazlar</h2>
        {autoCount > 0 && (
          <label style={{ fontSize: 13, color: 'var(--c500)', display: 'flex', alignItems: 'center', gap: 6 }}>
            <input type="checkbox" checked={autoOnly} onChange={(e) => setAutoOnly(e.target.checked)} />
            Yalnız avtomatik bağlananlar ({autoCount})
          </label>
        )}
      </div>
      <div className="muted" style={{ fontSize: 13, marginBottom: 10 }}>
        Bir işçidə bir neçə cihaz normaldır — Safari və tətbiq ayrı sayılır. Tanımadığın cihazı ləğv et.
      </div>

      {error && (
        <div className="fb fb-err" style={{ marginBottom: 12 }}>
          <IconX />
          <span>{error}</span>
        </div>
      )}

      <div className="tbl-wrap">
        <table>
          <thead>
            <tr>
              <th>İşçi</th>
              <th>Cihaz</th>
              <th>Necə bağlanıb</th>
              <th>Bağlanıb</th>
              <th>Son istifadə</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {rows.map((b) => (
              <tr key={b.id}>
                <td style={{ fontWeight: 700, color: 'var(--c900)' }}><EmployeeLink id={b.employeeId} name={b.employeeName} /></td>
                <td>{b.deviceLabel ?? <span className="muted">Naməlum cihaz</span>}</td>
                <td>
                  <span className={`inline-flex items-center rounded-full px-2 py-0.5 text-xs font-semibold ${ORIGIN[b.boundVia].cls}`}>
                    {ORIGIN[b.boundVia].label}
                  </span>
                </td>
                <td className="mono">{fmtDateTime(b.boundAtUtc)}</td>
                <td className="mono">{fmtDateTime(b.lastSeenAtUtc)}</td>
                <td style={{ textAlign: 'right' }}>
                  <button className="btn btn-danger btn-sm" disabled={busyId === b.id} onClick={() => revoke(b)}>
                    <IconTrash /> Ləğv et
                  </button>
                </td>
              </tr>
            ))}
            {all !== null && rows.length === 0 && (
              <tr>
                <td colSpan={6} className="muted" style={{ textAlign: 'center', padding: 28 }}>
                  {autoOnly ? 'Avtomatik bağlanan cihaz yoxdur' : 'Bağlı cihaz yoxdur'}
                </td>
              </tr>
            )}
            {all === null && !error && (
              <tr>
                <td colSpan={6} className="muted" style={{ textAlign: 'center', padding: 28 }}>
                  Yüklənir…
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </section>
  )
}
