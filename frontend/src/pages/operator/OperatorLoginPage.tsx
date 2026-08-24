import { useState, type FormEvent } from 'react'
import { BrandLogo } from '../../components/BrandLogo'
import { PinInput } from '../../components/PinInput'
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
          {/* The same mark the rest of the app shows. This screen used to point at /brand/qrlog.svg —
              a sentinel path, not a picture anyone drew — so the console opened on a logo the product
              had already replaced. */}
          <div style={{ display: 'flex', justifyContent: 'center', marginBottom: 10 }}>
            <BrandLogo size={52} />
          </div>
          <div style={{ fontSize: 13, color: 'var(--c400)', marginTop: 10 }}>SuperAdmin paneli</div>
        </div>

        <form onSubmit={onSubmit} style={{ background: '#fff', borderRadius: 20, padding: 28 }}>
          <div style={{ fontFamily: 'Manrope,sans-serif', fontWeight: 700, fontSize: 16, marginBottom: 18, color: 'var(--c900)' }}>
            SuperAdmin girişi
          </div>

          {error && (
            <div className="fb fb-err" style={{ marginBottom: 14 }}>
              <IconX />
              <span>{error}</span>
            </div>
          )}

          <div style={{ marginBottom: 12 }}>
            {/* An operator signs in with an email as often as a number, so unlike the staff screen this
                one keeps both — the label says so rather than making them guess. */}
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
            <label className="form-label">PIN (4 rəqəm)</label>
            <PinInput value={password} onChange={setPassword} />
          </div>

          <button type="submit" disabled={loading} className="btn btn-primary btn-bl btn-lg">
            {loading ? 'Yoxlanılır…' : 'Daxil ol'}
          </button>
        </form>
      </div>
    </div>
  )
}
