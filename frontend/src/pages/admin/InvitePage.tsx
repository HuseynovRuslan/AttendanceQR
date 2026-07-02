import { useEffect, useState, type FormEvent } from 'react'
import { getAdminLocations, invite, type InviteResult, type LocationDto } from '../../api/admin'
import type { Role } from '../../lib/jwt'

const inputCls =
  'rounded-lg border border-slate-300 px-3 py-2.5 focus:outline-none focus:ring-2 focus:ring-blue-500'

export function InvitePage() {
  const [fullName, setFullName] = useState('')
  const [email, setEmail] = useState('')
  const [locationId, setLocationId] = useState('')
  const [role, setRole] = useState<Role>('Employee')
  const [locations, setLocations] = useState<LocationDto[]>([])
  const [result, setResult] = useState<InviteResult | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)
  const [copied, setCopied] = useState(false)

  useEffect(() => {
    getAdminLocations().then(({ status, data }) => {
      if (status === 200 && Array.isArray(data)) {
        setLocations(data)
        if (data.length > 0) setLocationId(data[0].id)
      }
    })
  }, [])

  const activationLink = result
    ? `${window.location.origin}/activate?token=${result.activationToken}`
    : ''

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    setResult(null)
    setCopied(false)
    setLoading(true)
    const { status, data } = await invite(fullName, email, locationId, role)
    if (status === 200 && data && 'activationToken' in data) {
      setResult(data)
      setFullName('')
      setEmail('')
    } else if (status === 403) {
      setError('İcazəniz yoxdur')
    } else if (data && 'error' in data && data.error === 'EmailAlreadyExists') {
      setError('Bu email artıq mövcuddur')
    } else if (data && 'error' in data && data.error === 'LocationNotFound') {
      setError('Lokasiya tapılmadı')
    } else {
      setError('Dəvət alınmadı')
    }
    setLoading(false)
  }

  async function copyLink() {
    try {
      await navigator.clipboard.writeText(activationLink)
      setCopied(true)
      setTimeout(() => setCopied(false), 2000)
    } catch {
      /* clipboard blocked — user can select manually */
    }
  }

  return (
    <div className="max-w-xl">
      <h1 className="text-2xl font-bold text-slate-800 mb-4">İşçi dəvəti</h1>

      <form onSubmit={onSubmit} className="bg-white rounded-xl shadow p-5 space-y-4">
        {error && <div className="bg-red-50 text-red-700 rounded-lg p-3">{error}</div>}

        <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
          <label className="flex flex-col gap-1">
            <span className="text-sm font-medium text-slate-700">Ad Soyad</span>
            <input required value={fullName} onChange={(e) => setFullName(e.target.value)} className={inputCls} />
          </label>
          <label className="flex flex-col gap-1">
            <span className="text-sm font-medium text-slate-700">Email</span>
            <input required type="email" value={email} onChange={(e) => setEmail(e.target.value)} className={inputCls} />
          </label>
          <label className="flex flex-col gap-1">
            <span className="text-sm font-medium text-slate-700">Ərazi</span>
            <select value={locationId} onChange={(e) => setLocationId(e.target.value)} className={inputCls}>
              {locations.map((l) => (
                <option key={l.id} value={l.id}>
                  {l.name}
                </option>
              ))}
            </select>
          </label>
          <label className="flex flex-col gap-1">
            <span className="text-sm font-medium text-slate-700">Rol</span>
            <select value={role} onChange={(e) => setRole(e.target.value as Role)} className={inputCls}>
              <option value="Employee">Employee</option>
              <option value="Manager">Manager</option>
              <option value="Admin">Admin</option>
            </select>
          </label>
        </div>

        <button
          type="submit"
          disabled={loading || !locationId}
          className="bg-blue-600 hover:bg-blue-700 disabled:opacity-50 text-white font-semibold rounded-lg px-5 py-2.5"
        >
          {loading ? 'Dəvət olunur…' : 'Dəvət et'}
        </button>
      </form>

      {result && (
        <div className="bg-green-50 border border-green-200 rounded-xl p-5 mt-4">
          <p className="font-semibold text-green-800">✓ Dəvət yaradıldı</p>
          <p className="text-sm text-green-700 mt-1">
            Bu linki işçiyə göndərin — o, linki açıb parol təyin edəcək (email/SMS yoxdur, əl ilə paylaşın):
          </p>
          <div className="mt-3 flex items-stretch gap-2">
            <input
              readOnly
              value={activationLink}
              onFocus={(e) => e.target.select()}
              className="flex-1 rounded-lg border border-green-300 bg-white px-3 py-2.5 text-sm font-mono"
            />
            <button
              onClick={copyLink}
              className="bg-green-600 hover:bg-green-700 text-white rounded-lg px-4 py-2.5 font-medium whitespace-nowrap"
            >
              {copied ? 'Kopyalandı ✓' : 'Kopyala'}
            </button>
          </div>
        </div>
      )}
    </div>
  )
}
