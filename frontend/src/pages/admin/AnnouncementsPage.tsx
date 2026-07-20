import { useEffect, useState } from 'react'
import {
  createAnnouncement,
  deleteAnnouncement,
  getAdminAnnouncements,
  retireAnnouncement,
  type AdminAnnouncement,
} from '../../api/announcements'
import { IconSend, IconTrash, IconX } from '../../components/icons'

export function AnnouncementsPage() {
  const [items, setItems] = useState<AdminAnnouncement[]>([])
  const [message, setMessage] = useState('')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [busyId, setBusyId] = useState<string | null>(null)

  async function load() {
    const { status, data } = await getAdminAnnouncements()
    if (status === 200 && Array.isArray(data)) setItems(data)
  }

  useEffect(() => {
    void load()
  }, [])

  async function onPost() {
    if (!message.trim()) return
    setSaving(true)
    setError(null)
    const { status, data } = await createAnnouncement(message.trim())
    setSaving(false)
    if (status === 200 && data && 'id' in data) {
      setMessage('')
      void load()
    } else {
      setError('Elan göndərilmədi')
    }
  }

  async function onRetire(id: string) {
    setBusyId(id)
    await retireAnnouncement(id)
    setBusyId(null)
    void load()
  }

  async function onDelete(id: string) {
    setBusyId(id)
    await deleteAnnouncement(id)
    setBusyId(null)
    void load()
  }

  return (
    <div>
      <div className="card card-pad" style={{ marginBottom: 16 }}>
        <div className="card-title">Yeni elan</div>
        <p className="muted" style={{ fontSize: 13, marginTop: -8, marginBottom: 12 }}>
          Bütün işçilər bunu proqramın ev səhifəsində banner kimi görəcək.
        </p>
        <textarea
          className="inp"
          rows={3}
          value={message}
          onChange={(e) => setMessage(e.target.value)}
          placeholder="Məsələn: Artıq internet olmayanda da giriş edə bilərsiniz…"
          maxLength={1000}
          style={{ resize: 'vertical' }}
        />
        {error && (
          <div className="fb fb-err" style={{ marginTop: 10 }}>
            <IconX />
            <span>{error}</span>
          </div>
        )}
        <div style={{ marginTop: 12 }}>
          <button className="btn btn-primary" disabled={saving || !message.trim()} onClick={onPost}>
            <IconSend />
            {saving ? 'Göndərilir…' : 'Bütün işçilərə göndər'}
          </button>
        </div>
      </div>

      <div className="card card-pad">
        <div className="card-title">Elanlar</div>
        {items.length === 0 ? (
          <div className="muted" style={{ padding: '16px 0' }}>Hələ elan yoxdur</div>
        ) : (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
            {items.map((a) => (
              <div
                key={a.id}
                style={{
                  border: '1px solid var(--c100)',
                  borderRadius: 'var(--r)',
                  padding: '12px 14px',
                  opacity: a.isActive ? 1 : 0.55,
                }}
              >
                <div style={{ display: 'flex', alignItems: 'flex-start', gap: 10 }}>
                  <div style={{ flex: 1, minWidth: 0 }}>
                    <div style={{ whiteSpace: 'pre-line', fontSize: 14, color: 'var(--c900)', fontWeight: 500 }}>
                      {a.message}
                    </div>
                    <div style={{ fontSize: 11, color: 'var(--c400)', marginTop: 4 }}>
                      {new Date(a.createdAtUtc).toLocaleString('az-AZ')}
                      {!a.isActive && ' · söndürülüb'}
                    </div>
                  </div>
                  <div style={{ display: 'flex', gap: 6 }}>
                    {a.isActive && (
                      <button className="btn btn-sm" disabled={busyId === a.id} onClick={() => onRetire(a.id)}>
                        Söndür
                      </button>
                    )}
                    <button
                      className="btn btn-sm"
                      disabled={busyId === a.id}
                      onClick={() => onDelete(a.id)}
                      title="Sil"
                      aria-label="Sil"
                    >
                      <IconTrash />
                    </button>
                  </div>
                </div>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  )
}
