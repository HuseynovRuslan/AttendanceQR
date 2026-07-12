import { useState, type FormEvent } from 'react'
import { Navigate, useNavigate } from 'react-router-dom'
import { setInitialPin } from '../api/auth'
import { useAuth } from '../auth/AuthContext'
import { roleHome } from '../lib/jwt'

/**
 * Forced first-login screen for an account still on a temporary PIN (bulk import or an admin reset).
 * The route guards (ProtectedRoute / AdminRoute) send every other path here until a PIN is set. No
 * current PIN is asked — the employee has just signed in with the temp one; the server only permits
 * this while MustChangePin is set.
 */
export function SetPinPage() {
  const { saveToken, mustChangePin, role } = useAuth()
  const navigate = useNavigate()

  const [pin, setPin] = useState('')
  const [confirm, setConfirm] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)

  // Not on a temporary PIN — nothing to do here.
  if (!mustChangePin) return <Navigate to={roleHome(role)} replace />

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    if (!/^\d{4}$/.test(pin)) {
      setError('PIN düz 4 rəqəm olmalıdır')
      return
    }
    if (pin !== confirm) {
      setError('PIN-lər uyğun gəlmir')
      return
    }
    setLoading(true)
    try {
      const { status, data } = await setInitialPin(pin)
      if (status === 200 && data && 'token' in data) {
        saveToken(data.token)
        navigate(roleHome(role), { replace: true })
        return
      }
      const code = data && 'error' in data ? data.error : ''
      setError(code === 'PinInvalid' ? 'PIN düz 4 rəqəm olmalıdır' : 'PIN təyin edilmədi')
    } catch {
      setError('Serverə qoşulmaq mümkün olmadı')
    } finally {
      setLoading(false)
    }
  }

  const pinInputClass =
    'w-full rounded-xl border border-slate-200 bg-white px-3 py-3 text-center text-xl tracking-widest focus:border-blue-500 focus:outline-none focus:ring-2 focus:ring-blue-200'

  return (
    <div className="flex min-h-screen flex-col items-center justify-center bg-slate-50 p-4 text-slate-900">
      <form onSubmit={onSubmit} className="w-full max-w-sm rounded-3xl border border-slate-100 bg-white p-6 shadow-sm">
        <h1 className="text-center text-xl font-bold">Öz PIN-inizi təyin edin</h1>
        <p className="mt-2 text-center text-sm text-slate-500">
          Bu ilk girişdir. Təhlükəsizlik üçün müvəqqəti PIN-i öz PIN-inizlə əvəz edin.
        </p>

        {error && (
          <div className="mt-4 rounded-lg bg-red-50 p-3 text-center text-base font-medium text-red-700">{error}</div>
        )}

        <div className="mt-5 flex flex-col gap-4">
          <div>
            <label className="mb-1 block text-sm text-slate-500">Yeni PIN (4 rəqəm)</label>
            <input
              type="password"
              inputMode="numeric"
              maxLength={4}
              required
              autoComplete="new-password"
              value={pin}
              onChange={(e) => setPin(e.target.value.replace(/\D/g, '').slice(0, 4))}
              className={pinInputClass}
            />
          </div>
          <div>
            <label className="mb-1 block text-sm text-slate-500">Yeni PIN-i təkrarlayın</label>
            <input
              type="password"
              inputMode="numeric"
              maxLength={4}
              required
              autoComplete="new-password"
              value={confirm}
              onChange={(e) => setConfirm(e.target.value.replace(/\D/g, '').slice(0, 4))}
              className={pinInputClass}
            />
          </div>

          <button
            type="submit"
            disabled={loading}
            className="w-full rounded-2xl bg-blue-600 py-4 text-lg font-bold text-white transition hover:bg-blue-700 disabled:opacity-50"
          >
            {loading ? 'Yadda saxlanır…' : 'PIN-i təyin et'}
          </button>
        </div>
      </form>
    </div>
  )
}
