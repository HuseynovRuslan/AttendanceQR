import { useState } from 'react'
import {
  approveDeviceChange,
  getPendingDeviceChanges,
  rejectDeviceChange,
  type PendingDeviceChange,
} from '../../api/admin'
import { usePolling } from '../../lib/usePolling'

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
      // Optimistic: drop it now, then reconcile on the next poll.
      setRows((prev) => prev.filter((r) => r.requestId !== id))
    } else {
      setError(kind === 'approve' ? 'Təsdiq alınmadı' : 'Rədd alınmadı')
    }
    await refresh()
    setBusyId(null)
  }

  return (
    <div>
      <div className="flex items-center justify-between mb-4">
        <h1 className="text-2xl font-bold text-slate-800">Cihaz təsdiqləri</h1>
        <span className="text-xs text-slate-400">Hər 30 saniyədə yenilənir</span>
      </div>

      {error && <div className="bg-red-50 text-red-700 rounded-lg p-3 mb-3">{error}</div>}

      {loadedOnce && rows.length === 0 && !error ? (
        <div className="bg-white rounded-xl shadow p-10 text-center text-slate-400">
          Gözləyən tələb yoxdur
        </div>
      ) : (
        <div className="space-y-3">
          {rows.map((r) => (
            <div
              key={r.requestId}
              className="bg-white rounded-xl shadow p-4 flex flex-wrap items-center justify-between gap-4"
            >
              <div className="min-w-0">
                <p className="font-semibold text-slate-800">{r.employeeName}</p>
                <p className="text-sm text-slate-500 mt-0.5">
                  Köhnə: <span className="font-mono">{r.currentDeviceFingerprint ?? '—'}</span> →{' '}
                  Yeni: <span className="font-mono">{r.newDeviceFingerprint}</span>
                </p>
                <p className="text-xs text-slate-400 mt-0.5">
                  {new Date(r.requestedAtUtc).toLocaleString('az-AZ')}
                </p>
              </div>
              <div className="flex gap-2">
                <button
                  onClick={() => act(r.requestId, 'approve')}
                  disabled={busyId === r.requestId}
                  className="bg-green-600 hover:bg-green-700 disabled:opacity-50 text-white rounded-lg px-4 py-2 text-sm font-medium"
                >
                  Təsdiqlə
                </button>
                <button
                  onClick={() => act(r.requestId, 'reject')}
                  disabled={busyId === r.requestId}
                  className="bg-red-600 hover:bg-red-700 disabled:opacity-50 text-white rounded-lg px-4 py-2 text-sm font-medium"
                >
                  Rədd et
                </button>
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  )
}
