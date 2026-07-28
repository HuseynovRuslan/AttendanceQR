import { useState } from 'react'
import { EmployeeLink } from '../../components/EmployeeLink'
import {
  dismissPinReset,
  getPendingPinResets,
  resolvePinReset,
  type PendingPinReset,
} from '../../api/admin'
import { usePolling } from '../../lib/usePolling'
import { IconCheck, IconX } from '../../components/icons'

/** Admin queue for "PIN-i unutdum". Resolving resets the PIN and shows the temporary one so the admin
 *  can pass it on; the employee logs in with it and is forced to pick their own. */
export function PinResetsPage() {
  const [rows, setRows] = useState<PendingPinReset[]>([])
  // Kept separate from the polled pending list so a just-issued temp PIN stays on screen (for the admin
  // to read out) instead of vanishing on the next 30s refresh.
  const [resolved, setResolved] = useState<{ requestId: string; employeeName: string; tempPin: string }[]>([])
  const [error, setError] = useState<string | null>(null)
  const [loadedOnce, setLoadedOnce] = useState(false)
  const [busyId, setBusyId] = useState<string | null>(null)

  async function refresh() {
    const { status, data } = await getPendingPinResets()
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

  async function resolve(r: PendingPinReset) {
    setBusyId(r.requestId)
    const { status, data } = await resolvePinReset(r.requestId)
    if (status === 200 && data && 'tempPin' in data) {
      setResolved((p) => [{ requestId: r.requestId, employeeName: r.employeeName, tempPin: data.tempPin }, ...p])
      setRows((prev) => prev.filter((x) => x.requestId !== r.requestId))
    } else {
      setError('PIN sıfırlanmadı')
    }
    setBusyId(null)
  }

  async function dismiss(id: string) {
    setBusyId(id)
    const { status } = await dismissPinReset(id)
    if (status === 200) {
      setRows((prev) => prev.filter((r) => r.requestId !== id))
    } else {
      setError('Rədd alınmadı')
    }
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

      {/* Just-issued temporary PINs — read them out to the employee, then close. */}
      {resolved.length > 0 && (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
          {resolved.map((r) => (
            <div
              key={r.requestId}
              className="card card-pad"
              style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', justifyContent: 'space-between', gap: 16, background: '#E7F6EC', borderColor: '#8BD3A5' }}
            >
              <div style={{ minWidth: 0 }}>
                <div style={{ fontWeight: 700, color: 'var(--c900)' }}>{r.employeeName}</div>
                <div style={{ fontSize: 13, color: 'var(--c600)', marginTop: 4 }}>
                  Yeni müvəqqəti PIN:{' '}
                  <b style={{ fontSize: 22, letterSpacing: 3, color: '#1B7F3B' }}>{r.tempPin}</b>
                </div>
                <div style={{ fontSize: 12, color: 'var(--c500)', marginTop: 2 }}>
                  İşçiyə deyin — bununla daxil olub öz PIN-ini təyin edəcək.
                </div>
              </div>
              <button
                className="btn btn-sm"
                onClick={() => setResolved((p) => p.filter((x) => x.requestId !== r.requestId))}
              >
                Bağla
              </button>
            </div>
          ))}
        </div>
      )}

      <section>
        <h2 style={{ fontSize: 15, fontWeight: 800, color: 'var(--c900)', marginBottom: 10 }}>
          Gözləyən tələblər
        </h2>
        {loadedOnce && rows.length === 0 && !error ? (
          <div className="card card-pad muted" style={{ textAlign: 'center', padding: 28 }}>
            Gözləyən PIN sıfırlama tələbi yoxdur
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
                  <div style={{ fontWeight: 700, color: 'var(--c900)' }}>
                    <EmployeeLink id={r.employeeId} name={r.employeeName} />
                  </div>
                  <div style={{ fontSize: 13, color: 'var(--c500)', marginTop: 4 }}>
                    {r.phoneNumber ?? r.email ?? '—'}
                  </div>
                  <div style={{ fontSize: 11, color: 'var(--c400)', marginTop: 2 }}>
                    {new Date(r.requestedAtUtc).toLocaleString('az-AZ')}
                  </div>
                </div>
                <div style={{ display: 'flex', gap: 8 }}>
                  <button className="btn btn-primary btn-sm" disabled={busyId === r.requestId} onClick={() => resolve(r)}>
                    <IconCheck /> PIN sıfırla
                  </button>
                  <button className="btn btn-danger btn-sm" disabled={busyId === r.requestId} onClick={() => dismiss(r.requestId)}>
                    <IconX /> Rədd et
                  </button>
                </div>
              </div>
            ))}
          </div>
        )}
      </section>
    </div>
  )
}
