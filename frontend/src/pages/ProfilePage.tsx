import { useState, type FormEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { changePassword } from '../api/auth'
import { useAuth } from '../auth/AuthContext'
import { EmployeeNav } from '../components/EmployeeNav'

export function ProfilePage() {
  const { saveToken, email } = useAuth()
  const navigate = useNavigate()

  const [current, setCurrent] = useState('')
  const [next, setNext] = useState('')
  const [confirm, setConfirm] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [ok, setOk] = useState(false)
  const [loading, setLoading] = useState(false)

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    if (!/^\d{4}$/.test(next)) {
      setError('Yeni PIN düz 4 rəqəm olmalıdır')
      return
    }
    if (next !== confirm) {
      setError('Yeni PIN-lər uyğun gəlmir')
      return
    }
    setLoading(true)
    try {
      const { status, data } = await changePassword(current, next)
      if (status === 200 && data && 'token' in data) {
        saveToken(data.token)
        setOk(true)
        setTimeout(() => navigate('/scan', { replace: true }), 1200)
        return
      }
      const code = data && 'error' in data ? data.error : ''
      setError(
        code === 'InvalidCurrentPassword'
          ? 'Cari PIN yanlışdır'
          : code === 'PinInvalid'
            ? 'Yeni PIN düz 4 rəqəm olmalıdır'
            : 'PIN dəyişdirilmədi',
      )
    } catch {
      setError('Serverə qoşulmaq mümkün olmadı')
    } finally {
      setLoading(false)
    }
  }

  const pinInputClass =
    'w-full rounded-lg bg-slate-800 border border-slate-700 px-3 py-3 text-center tracking-widest text-xl focus:outline-none focus:ring-2 focus:ring-blue-500'

  return (
    <div className="min-h-screen flex flex-col bg-slate-900 text-white">
      <EmployeeNav title="Profil" />

      <main className="flex-1 flex flex-col items-center justify-center p-4">
        <div className="w-full max-w-sm">
          <p className="text-slate-300 text-center mb-6">{email}</p>

          {ok ? (
            <div className="bg-green-500 text-white rounded-2xl p-6 text-center">
              <div className="text-4xl font-bold mb-2">✓</div>
              <h2 className="text-lg font-bold">Parolunuz dəyişdirildi</h2>
              <p className="mt-1 text-base opacity-90">Skan ekranına qayıdılır…</p>
            </div>
          ) : (
            <form onSubmit={onSubmit} className="flex flex-col gap-4">
              <h1 className="text-xl font-bold text-center">PIN-i dəyişin</h1>

              {error && (
                <div className="bg-red-500 text-white rounded-lg p-3 text-center text-base">{error}</div>
              )}

              <div>
                <label className="block text-sm text-slate-300 mb-1">Cari PIN</label>
                <input
                  type="password"
                  inputMode="numeric"
                  maxLength={4}
                  required
                  autoComplete="current-password"
                  value={current}
                  onChange={(e) => setCurrent(e.target.value.replace(/\D/g, '').slice(0, 4))}
                  className={pinInputClass}
                />
              </div>
              <div>
                <label className="block text-sm text-slate-300 mb-1">Yeni PIN (4 rəqəm)</label>
                <input
                  type="password"
                  inputMode="numeric"
                  maxLength={4}
                  required
                  autoComplete="new-password"
                  value={next}
                  onChange={(e) => setNext(e.target.value.replace(/\D/g, '').slice(0, 4))}
                  className={pinInputClass}
                />
              </div>
              <div>
                <label className="block text-sm text-slate-300 mb-1">Yeni PIN-i təkrarlayın</label>
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
                className="w-full bg-blue-600 hover:bg-blue-700 disabled:opacity-50 text-white font-bold rounded-2xl py-4 text-lg transition"
              >
                {loading ? 'Yadda saxlanır…' : 'PIN-i dəyiş'}
              </button>
            </form>
          )}

          <div className="mt-8 border-t border-slate-800 pt-6 text-center">
            <p className="text-slate-400 text-base mb-3">Telefonunuz dəyişib?</p>
            <Link
              to="/device-change-request"
              className="inline-block bg-slate-800 hover:bg-slate-700 rounded-lg px-4 py-2 text-base font-semibold"
            >
              Yeni telefon tələbi göndər
            </Link>
          </div>
        </div>
      </main>
    </div>
  )
}
