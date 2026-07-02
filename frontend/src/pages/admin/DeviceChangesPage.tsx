import { useState } from 'react'
import {
  approveDeviceChange,
  getPendingDeviceChanges,
  rejectDeviceChange,
  type PendingDeviceChange,
} from '../../api/admin'
import { usePolling } from '../../lib/usePolling'
import { IconCheck, IconPhone, IconX } from '../../components/icons'

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
    <div>
      {error && (
        <div className="fb fb-err" style={{ marginBottom: 12 }}>
          <IconX />
          <span>{error}</span>
        </div>
      )}

      {loadedOnce && rows.length === 0 && !error ? (
        <div className="card card-pad muted" style={{ textAlign: 'center', padding: 40 }}>
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
                  <IconPhone /> {r.employeeName}
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
    </div>
  )
}
