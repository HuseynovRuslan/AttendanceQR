import { useState, type FormEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { changePassword } from '../api/auth'
import { useAuth } from '../auth/AuthContext'
import { SubPageHeader } from '../components/SubPageHeader'
import { PushToggle } from '../components/PushToggle'

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
        setTimeout(() => navigate('/menu', { replace: true }), 1200)
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
    'w-full rounded-xl border border-slate-200 bg-white px-3 py-3 text-center text-xl tracking-widest focus:border-blue-500 focus:outline-none focus:ring-2 focus:ring-blue-200'

  return (
    <div className="min-h-screen bg-slate-50 text-slate-900">
      <SubPageHeader title="Profil / PIN" />

      <main className="mx-auto w-full max-w-sm p-4">
        <p className="mb-6 text-center text-slate-500">{email}</p>

        {ok ? (
          <div className="rounded-2xl bg-green-500 p-6 text-center text-white">
            <div className="mb-2 text-4xl font-bold">✓</div>
            <h2 className="text-lg font-bold">PIN-iniz dəyişdirildi</h2>
            <p className="mt-1 text-base opacity-90">Menyuya qayıdılır…</p>
          </div>
        ) : (
          <form
            onSubmit={onSubmit}
            className="flex flex-col gap-4 rounded-3xl border border-slate-100 bg-white p-5 shadow-sm"
          >
            <h2 className="text-center text-lg font-bold">PIN-i dəyişin</h2>

            {error && (
              <div className="rounded-lg bg-red-50 p-3 text-center text-base font-medium text-red-700">{error}</div>
            )}

            <div>
              <label className="mb-1 block text-sm text-slate-500">Cari PIN</label>
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
              <label className="mb-1 block text-sm text-slate-500">Yeni PIN (4 rəqəm)</label>
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
              {loading ? 'Yadda saxlanır…' : 'PIN-i dəyiş'}
            </button>
          </form>
        )}

        <div className="mt-6 rounded-3xl border border-slate-100 bg-white p-5 text-center shadow-sm">
          <p className="mb-3 text-base text-slate-500">Telefonunuz dəyişib?</p>
          <Link
            to="/device-change-request"
            className="inline-block rounded-xl bg-slate-100 px-4 py-2 text-base font-semibold text-slate-700 transition active:bg-slate-200"
          >
            Yeni telefon tələbi göndər
          </Link>
        </div>

        {/* Deliberately tucked away behind a details toggle: notifications carry the announcements and
            the checkout reminder, so turning them off should be possible but never a casual tap. */}
        <details className="mt-6">
          <summary className="cursor-pointer list-none text-center text-sm text-slate-400">
            Bildiriş ayarları
          </summary>
          <div className="mt-3">
            <PushToggle />
          </div>
        </details>
      </main>
    </div>
  )
}
