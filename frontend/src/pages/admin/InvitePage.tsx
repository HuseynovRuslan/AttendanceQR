import { useEffect, useState, type FormEvent } from 'react'
import { getAdminLocations, invite, type InviteResult, type LocationDto } from '../../api/admin'
import type { Role } from '../../lib/jwt'
import { IconCheck, IconSend, IconX } from '../../components/icons'

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
    <div style={{ maxWidth: 620 }}>
      <form onSubmit={onSubmit} className="card card-pad">
        {error && (
          <div className="fb fb-err" style={{ marginBottom: 14 }}>
            <IconX />
            <span>{error}</span>
          </div>
        )}

        <div className="form-row cols2">
          <div>
            <label className="form-label">Ad Soyad</label>
            <input className="inp" required value={fullName} onChange={(e) => setFullName(e.target.value)} />
          </div>
          <div>
            <label className="form-label">Email</label>
            <input className="inp" type="email" required value={email} onChange={(e) => setEmail(e.target.value)} />
          </div>
        </div>
        <div className="form-row cols2">
          <div>
            <label className="form-label">Ərazi</label>
            <select className="inp" value={locationId} onChange={(e) => setLocationId(e.target.value)}>
              {locations.map((l) => (
                <option key={l.id} value={l.id}>
                  {l.name}
                </option>
              ))}
            </select>
          </div>
          <div>
            <label className="form-label">Rol</label>
            <select className="inp" value={role} onChange={(e) => setRole(e.target.value as Role)}>
              <option value="Employee">İşçi</option>
              <option value="Manager">Menecer</option>
              <option value="Admin">Admin</option>
            </select>
          </div>
        </div>

        <button type="submit" className="btn btn-primary" disabled={loading || !locationId}>
          <IconSend />
          {loading ? 'Dəvət olunur…' : 'Dəvət et'}
        </button>
      </form>

      {result && (
        <div className="card card-pad" style={{ marginTop: 16 }}>
          <div className="fb fb-ok" style={{ marginBottom: 12 }}>
            <IconCheck />
            <span>Dəvət yaradıldı. Bu linki işçiyə göndərin (email/SMS yoxdur — əl ilə paylaşın):</span>
          </div>
          <div className="link-box">{activationLink}</div>
          <button className="btn btn-primary btn-sm" style={{ marginTop: 10 }} onClick={copyLink}>
            {copied ? 'Kopyalandı ✓' : 'Kopyala'}
          </button>
        </div>
      )}
    </div>
  )
}
