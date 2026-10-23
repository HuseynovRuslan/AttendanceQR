import { useEffect, useState } from 'react'
import { endedSessionNotice, forgetEndedSessionNotice } from '../api/client'
import { useAuth } from '../auth/AuthContext'

/**
 * Says once, on whatever screen the operator lands on, that their support session ran out.
 *
 * Since 03.10.2026 an expired session hands the operator back to their own panel instead of signing
 * them out. A screen that changes under somebody with no word of why reads as a glitch, so this says
 * it. The login screens say it themselves for the rarer case where the operator's own session had run
 * out too, which is why this one only speaks to somebody signed in.
 */
export function EndedSessionNotice() {
  const { isAuthenticated } = useAuth()
  const [notice, setNotice] = useState<string | null>(null)

  useEffect(() => {
    if (!isAuthenticated) return
    const text = endedSessionNotice()
    if (!text) return
    forgetEndedSessionNotice()
    setNotice(text)
    const t = setTimeout(() => setNotice(null), 10_000)
    return () => clearTimeout(t)
  }, [isAuthenticated])

  if (!notice) return null
  return (
    <div
      role="status"
      className="fb fb-info"
      style={{
        position: 'fixed', top: 16, left: '50%', transform: 'translateX(-50%)', zIndex: 1000,
        maxWidth: 'calc(100vw - 32px)', boxShadow: '0 8px 24px rgba(0,0,0,.12)',
      }}
    >
      <span>{notice}</span>
      <button type="button" className="btn-link" style={{ marginLeft: 12, marginTop: 0 }} onClick={() => setNotice(null)}>
        Bağla
      </button>
    </div>
  )
}
