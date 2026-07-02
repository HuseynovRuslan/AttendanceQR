import { useState, type FormEvent } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import { login } from '../api/auth'
import { useAuth } from '../auth/AuthContext'
import { decodeJwt, roleHome } from '../lib/jwt'

export function LoginPage() {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)
  const { saveToken } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    setLoading(true)
    try {
      const { status, data } = await login(email, password)
      if (status === 200 && data && 'token' in data) {
        saveToken(data.token)
        // Back to where they were headed, else the role's home (staff → /admin, employee → /scan).
        const from = (location.state as { from?: string } | null)?.from
        const role = decodeJwt(data.token)?.role
        navigate(from ?? roleHome(role), { replace: true })
      } else {
        setError('Email və ya parol yanlışdır')
      }
    } catch {
      setError('Serverə qoşulmaq mümkün olmadı')
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="min-h-screen flex items-center justify-center bg-slate-100 p-4">
      <form
        onSubmit={onSubmit}
        className="w-full max-w-sm bg-white rounded-2xl shadow-lg p-6 space-y-5"
      >
        <div className="text-center">
          <h1 className="text-2xl font-bold text-slate-800">AttendanceQR</h1>
          <p className="text-slate-500 text-sm mt-1">İşçi girişi</p>
        </div>

        {error && (
          <div className="bg-red-50 text-red-700 text-sm rounded-lg p-3">{error}</div>
        )}

        <div>
          <label className="block text-sm font-medium text-slate-700 mb-1">Email</label>
          <input
            type="email"
            required
            autoComplete="username"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            className="w-full rounded-lg border border-slate-300 px-3 py-3 focus:outline-none focus:ring-2 focus:ring-blue-500"
          />
        </div>

        <div>
          <label className="block text-sm font-medium text-slate-700 mb-1">Parol</label>
          <input
            type="password"
            required
            autoComplete="current-password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            className="w-full rounded-lg border border-slate-300 px-3 py-3 focus:outline-none focus:ring-2 focus:ring-blue-500"
          />
        </div>

        <button
          type="submit"
          disabled={loading}
          className="w-full bg-blue-600 hover:bg-blue-700 disabled:opacity-50 text-white font-semibold rounded-lg py-3 text-lg transition"
        >
          {loading ? 'Yoxlanılır…' : 'Daxil ol'}
        </button>
      </form>
    </div>
  )
}
