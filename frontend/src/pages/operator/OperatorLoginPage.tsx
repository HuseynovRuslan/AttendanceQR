import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { operatorLogin } from '../../api/auth'
import { useAuth } from '../../auth/AuthContext'
import { IconX } from '../../components/icons'

/**
 * Login for the operator console (admin.qrlog.az). Uses the cross-tenant operator-login endpoint, which
 * finds the account across every company AND requires the super-admin allowlist — a normal company admin
 * who lands here with correct credentials is refused server-side, so the shell never even paints for them.
 */
export function OperatorLoginPage() {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)
  const { saveToken } = useAuth()
  const navigate = useNavigate()

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    setLoading(true)
    try {
      const { status, data } = await operatorLogin(email, password)
      if (status === 200 && data && 'token' in data) {
        saveToken(data.token)
        navigate('/', { replace: true })
      } else if (status === 429) {
        const m = data && 'minutes' in data && data.minutes ? data.minutes : 5
        setError(`Çox sayda cəhd — ${m} dəqiqə sonra yenidən cəhd edin`)
      } else {
        // Deliberately one message for wrong-PIN and not-an-operator: the endpoint doesn't distinguish.
        setError('Giriş məlumatları yanlışdır və ya bu hesabın operator icazəsi yoxdur')
      }
    } catch {
      setError('Serverə qoşulmaq mümkün olmadı')
    } finally {
      setLoading(false)
    }
  }

  return (
    <div
      style={{
        minHeight: '100vh',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        background: 'var(--c900)',
        padding: 16,
      }}
    >
      <div style={{ width: '100%', maxWidth: 380 }}>
        <div style={{ textAlign: 'center', marginBottom: 28 }}>
          <div style={{ display: 'flex', justifyContent: 'center', marginBottom: 10 }}>
            <img src="/brand/qrlog.svg" alt="QRLog" width={52} height={52} style={{ borderRadius: 12 }} />
          </div>
          <div style={{ fontFamily: 'Manrope,sans-serif', fontWeight: 800, fontSize: 20, color: '#fff' }}>
            QRLog
          </div>
          <div style={{ fontSize: 13, color: 'var(--c400)', marginTop: 2 }}>Operator paneli</div>
        </div>

        <form onSubmit={onSubmit} style={{ background: '#fff', borderRadius: 20, padding: 28 }}>
          <div style={{ fontFamily: 'Manrope,sans-serif', fontWeight: 700, fontSize: 16, marginBottom: 18, color: 'var(--c900)' }}>
            Operator girişi
          </div>

          {error && (
            <div className="fb fb-err" style={{ marginBottom: 14 }}>
              <IconX />
              <span>{error}</span>
            </div>
          )}

          <div style={{ marginBottom: 12 }}>
            <label className="form-label">Email və ya telefon nömrəsi</label>
            <input
              className="inp"
              type="text"
              required
              autoComplete="username"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
            />
          </div>
          <div style={{ marginBottom: 18 }}>
            <label className="form-label">Şifrə / PIN</label>
            <input
              className="inp"
              type="password"
              required
              autoComplete="current-password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
            />
          </div>

          <button type="submit" disabled={loading} className="btn btn-primary btn-bl btn-lg">
            {loading ? 'Yoxlanılır…' : 'Daxil ol'}
          </button>
        </form>
      </div>
    </div>
  )
}
