import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { changePassword } from '../api/auth'
import { useAuth } from '../auth/AuthContext'
import { IconX } from './icons'

/**
 * Changing your own PIN, as a sheet.
 *
 * It used to BE the profile screen — three password boxes under the word "Profil". Changing a PIN is
 * something you do perhaps twice a year; it is an action, not an identity, and it belongs in front of
 * the profile rather than instead of it.
 *
 * The backend issues a fresh token and retires every other one for this account, so the new token is
 * saved here immediately. Without that the person who just changed their own PIN would be signed out
 * by their next request — and on a phone shared by a crew, this account is also a saved entry that
 * would quietly stop working.
 */
export function PinChangeSheet({ onClose }: { onClose: () => void }) {
  const { saveToken } = useAuth()
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
            : code === 'PinTooWeak'
              ? 'Bu PIN çox sadədir — 1234, 0000, 1212 kimi PIN-lər qəbul edilmir'
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
    <div className="fixed inset-0 z-50">
      <div className="absolute inset-0 bg-slate-900/50 backdrop-blur-sm" onClick={loading ? undefined : onClose} />
      <div className="absolute inset-x-0 bottom-0 max-h-[92vh] overflow-y-auto rounded-t-3xl bg-white p-5 pb-[max(2rem,env(safe-area-inset-bottom))] shadow-2xl">
        <div className="mx-auto mb-4 h-1 w-10 rounded-full bg-slate-200" />

        {ok ? (
          <div className="rounded-2xl bg-green-500 p-6 text-center text-white">
            <div className="mb-2 text-4xl font-bold">✓</div>
            <h2 className="text-lg font-bold">PIN-iniz dəyişdirildi</h2>
            <p className="mt-1 text-base opacity-90">Menyuya qayıdılır…</p>
          </div>
        ) : (
          <form onSubmit={onSubmit} className="flex flex-col gap-4">
            <div className="font-bold text-slate-900">PIN-i dəyişin</div>

            {error && (
              <div className="rounded-lg bg-red-50 p-3 text-center text-base font-medium text-red-700">
                <IconX className="mr-1 inline h-4 w-4" />
                {error}
              </div>
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

            <div className="flex gap-2">
              <button type="button" onClick={onClose} className="btn btn-bl flex-1">
                Bağla
              </button>
              <button type="submit" disabled={loading} className="btn btn-primary btn-bl flex-1">
                {loading ? 'Yadda saxlanır…' : 'PIN-i dəyiş'}
              </button>
            </div>
          </form>
        )}
      </div>
    </div>
  )
}
