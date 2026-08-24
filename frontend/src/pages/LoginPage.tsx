import { useState, type FormEvent } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import { login, appLogin } from '../api/auth'
import { isAppMode } from '../lib/host'
import { useAuth } from '../auth/AuthContext'
import { useBranding } from '../branding/BrandingContext'
import { decodeJwt, roleHome } from '../lib/jwt'
import { BrandLogo } from '../components/BrandLogo'
import { IconX } from '../components/icons'

export function LoginPage() {
  const location = useLocation()
  // Coming back from "PIN-i unutdum" with a freshly issued temporary PIN: that screen hands the number
  // back in the nav state (both directions now), so nobody has to retype a phone number from memory on
  // a keypad while holding a temp PIN in their head.
  const [email, setEmail] = useState((location.state as { identifier?: string } | null)?.identifier ?? '')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)
  const { saveToken } = useAuth()
  const branding = useBranding()
  const navigate = useNavigate()

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    setLoading(true)
    try {
      // The native app (no company subdomain) resolves the tenant from the credentials; the web uses
      // its subdomain. Same request/response shape either way.
      const { status, data } = await (isAppMode() ? appLogin : login)(email, password)
      if (status === 200 && data && 'token' in data) {
        saveToken(data.token)
        const from = (location.state as { from?: string } | null)?.from
        const role = decodeJwt(data.token)?.role
        navigate(from ?? roleHome(role), { replace: true })
      } else if (status === 429) {
        const m = data && 'minutes' in data && data.minutes ? data.minutes : 5
        setError(`Çox sayda cəhd — ${m} dəqiqə sonra yenidən cəhd edin`)
      } else {
        // Warn before the cool-off: show the count only when it's getting close.
        const rem = data && 'remaining' in data ? data.remaining : undefined
        setError(
          rem !== undefined && rem <= 3
            ? `Yanlış PIN — ${rem} cəhdiniz qaldı`
            : 'Nömrə və ya PIN yanlışdır',
        )
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
            <BrandLogo size={52} />
          </div>
          {/* The company's name, when we know it. On app.qrlog.az we do not — the app shell serves every
              company and only learns which one from the phone number typed below — and the fallback used
              to print "Davamiyyət" directly above the "Davamiyyət sistemi" line, saying the same word
              twice under a logo that already says QRLog. With no company to name, the logo and the
              subtitle are the whole heading. */}
          {branding.displayName && (
            <div style={{ fontFamily: 'Manrope,sans-serif', fontWeight: 800, fontSize: 20, color: '#fff' }}>
              {branding.displayName}
            </div>
          )}
          <div style={{ fontSize: 13, color: 'var(--c400)', marginTop: 2 }}>Davamiyyət sistemi</div>
        </div>

        <form onSubmit={onSubmit} style={{ background: '#fff', borderRadius: 20, padding: 28 }}>
          <div style={{ fontFamily: 'Manrope,sans-serif', fontWeight: 700, fontSize: 16, marginBottom: 18, color: 'var(--c900)' }}>
            Sistemə daxil olun
          </div>

          {error && (
            <div className="fb fb-err" style={{ marginBottom: 14 }}>
              <IconX />
              <span>{error}</span>
            </div>
          )}

          <div style={{ marginBottom: 12 }}>
            <label className="form-label">Telefon nömrəsi</label>
            <input
              className="inp"
              // The field still carries whatever is typed straight to the same endpoint, which resolves
              // it as a phone number OR an email — the admins who sign in with an email keep working.
              // Only the wording changed: "Email və ya telefon nömrəsi" meant nothing to the older
              // employees, who then typed nothing at all. type="tel" opens the number pad on a phone
              // without restricting what can be entered.
              type="tel"
              inputMode="tel"
              placeholder="0XX XXX XX XX"
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

          {/* Forgot-PIN: no dead end. The dedicated flow verifies by face from the bound device and
              hands out a PIN on the spot, falling back to an admin request if it can't. */}
          <button
            type="button"
            onClick={() => navigate('/forgot-pin', { state: { identifier: email } })}
            style={{
              marginTop: 16,
              width: '100%',
              background: 'none',
              border: 'none',
              color: 'var(--c500)',
              fontSize: 13,
              fontWeight: 600,
              cursor: 'pointer',
              textDecoration: 'underline',
              textUnderlineOffset: 2,
            }}
          >
            PIN-i unutdum?
          </button>
        </form>
      </div>
    </div>
  )
}
