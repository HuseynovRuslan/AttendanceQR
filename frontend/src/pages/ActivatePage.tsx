import { useMemo, useState, type FormEvent } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { activate } from '../api/auth'
import { useAuth } from '../auth/AuthContext'
import { getDeviceFingerprint, getFriendlyDeviceName } from '../lib/device'

export function ActivatePage() {
  const [params] = useSearchParams()
  const token = params.get('token') ?? ''
  const device = useMemo(() => getDeviceFingerprint(), [])
  const deviceLabel = useMemo(() => getFriendlyDeviceName(), [])

  const [password, setPassword] = useState('')
  const [confirm, setConfirm] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)
  const { saveToken } = useAuth()
  const navigate = useNavigate()

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    if (!/^\d{4}$/.test(password)) {
      setError('PIN düz 4 rəqəm olmalıdır')
      return
    }
    if (password !== confirm) {
      setError('PIN-lər uyğun gəlmir')
      return
    }
    setLoading(true)
    try {
      const { status, data } = await activate(token, password, device, deviceLabel)
      if (status === 200 && data && 'token' in data) {
        saveToken(data.token)
        navigate('/scan', { replace: true })
        return
      }
      const code = data && 'error' in data ? data.error : ''
      setError(
        code === 'AlreadyActivated'
          ? 'Bu hesab artıq aktivləşdirilib. Zəhmət olmasa daxil olun.'
          : code === 'TokenExpired'
            ? 'Aktivasiya linki köhnəlib. Yeni dəvət tələb edin.'
            : code === 'PinInvalid'
              ? 'PIN düz 4 rəqəm olmalıdır'
              : 'Aktivasiya alınmadı — token yanlışdır.',
      )
    } catch {
      setError('Serverə qoşulmaq mümkün olmadı')
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="min-h-screen flex items-center justify-center bg-slate-100 p-4">
      <div className="w-full max-w-sm bg-white rounded-2xl shadow-lg p-6 space-y-5">
        <div className="text-center">
          <h1 className="text-2xl font-bold text-slate-800">Hesabı aktivləşdir</h1>
          <p className="text-slate-500 text-sm mt-1">PIN təyin edin və bu cihazı bağlayın</p>
        </div>

        {!token ? (
          <div className="bg-red-50 text-red-700 text-sm rounded-lg p-3">
            Aktivasiya token-i tapılmadı. Zəhmət olmasa dəvət linkindən istifadə edin.
          </div>
        ) : (
          <form onSubmit={onSubmit} className="space-y-5">
            {error && (
              <div className="bg-red-50 text-red-700 text-sm rounded-lg p-3">{error}</div>
            )}

            <div>
              <label className="block text-sm font-medium text-slate-700 mb-1">4 rəqəmli PIN təyin edin</label>
              <input
                type="password"
                inputMode="numeric"
                pattern="\d{4}"
                maxLength={4}
                required
                autoComplete="new-password"
                value={password}
                onChange={(e) => setPassword(e.target.value.replace(/\D/g, '').slice(0, 4))}
                className="w-full rounded-lg border border-slate-300 px-3 py-3 focus:outline-none focus:ring-2 focus:ring-blue-500"
                style={{ letterSpacing: 8, textAlign: 'center', fontSize: 22 }}
              />
            </div>

            <div>
              <label className="block text-sm font-medium text-slate-700 mb-1">PIN-i təkrarlayın</label>
              <input
                type="password"
                inputMode="numeric"
                pattern="\d{4}"
                maxLength={4}
                required
                autoComplete="new-password"
                value={confirm}
                onChange={(e) => setConfirm(e.target.value.replace(/\D/g, '').slice(0, 4))}
                className="w-full rounded-lg border border-slate-300 px-3 py-3 focus:outline-none focus:ring-2 focus:ring-blue-500"
                style={{ letterSpacing: 8, textAlign: 'center', fontSize: 22 }}
              />
            </div>

            <p className="text-xs text-slate-400">
              Bu cihaz hesabınıza bağlanacaq — bundan sonra yalnız bu cihazla skan edə biləcəksiniz.
            </p>

            <button
              type="submit"
              disabled={loading}
              className="w-full bg-blue-600 hover:bg-blue-700 disabled:opacity-50 text-white font-semibold rounded-lg py-3 text-lg transition"
            >
              {loading ? 'Aktivləşdirilir…' : 'Aktivləşdir'}
            </button>
          </form>
        )}
      </div>
    </div>
  )
}
