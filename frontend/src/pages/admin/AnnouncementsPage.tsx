import { useEffect, useMemo, useState } from 'react'
import {
  createAnnouncement,
  deleteAnnouncement,
  getAdminAnnouncements,
  retireAnnouncement,
  type AdminAnnouncement,
  type AnnouncementAudience,
} from '../../api/announcements'
import { getEmployees, type AdminEmployee } from '../../api/admin'
import { IconSend, IconTrash, IconX } from '../../components/icons'

const AUDIENCE: { value: AnnouncementAudience; label: string; hint: string }[] = [
  { value: 'All', label: '👥 Hamı', hint: 'Bütün işçilər' },
  { value: 'AtWork', label: '🏢 İşdə olanlar', hint: 'Bu gün giriş edənlər' },
  { value: 'NotAtWork', label: '🚶 İşdə olmayanlar', hint: 'Bu gün giriş etməyənlər' },
  { value: 'Selected', label: '🎯 Seçilmiş', hint: 'Aşağıdan işçi seçin' },
]

const AUDIENCE_LABEL: Record<AnnouncementAudience, string> = {
  All: 'Hamı',
  AtWork: 'İşdə olanlar',
  NotAtWork: 'İşdə olmayanlar',
  Selected: 'Seçilmiş',
}

export function AnnouncementsPage() {
  const [items, setItems] = useState<AdminAnnouncement[]>([])
  const [employees, setEmployees] = useState<AdminEmployee[]>([])

  const [title, setTitle] = useState('')
  const [message, setMessage] = useState('')
  const [audience, setAudience] = useState<AnnouncementAudience>('All')
  const [recipientIds, setRecipientIds] = useState<string[]>([])
  const [empSearch, setEmpSearch] = useState('')
  const [schedule, setSchedule] = useState(false)
  const [scheduledFor, setScheduledFor] = useState('')

  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [busyId, setBusyId] = useState<string | null>(null)

  async function load() {
    const [a, e] = await Promise.all([getAdminAnnouncements(), getEmployees()])
    if (a.status === 200 && Array.isArray(a.data)) setItems(a.data)
    if (e.status === 200 && Array.isArray(e.data)) setEmployees(e.data.filter((x) => x.isActive))
  }

  useEffect(() => {
    void load()
  }, [])

  const filteredEmployees = useMemo(() => {
    const q = empSearch.trim().toLowerCase()
    return q ? employees.filter((e) => e.fullName.toLowerCase().includes(q)) : employees
  }, [employees, empSearch])

  function toggleRecipient(id: string) {
    setRecipientIds((ids) => (ids.includes(id) ? ids.filter((x) => x !== id) : [...ids, id]))
  }

  function reset() {
    setTitle('')
    setMessage('')
    setAudience('All')
    setRecipientIds([])
    setSchedule(false)
    setScheduledFor('')
  }

  async function onPost() {
    setError(null)
    if (!message.trim()) return setError('Mesaj boş ola bilməz')
    if (audience === 'Selected' && recipientIds.length === 0) return setError('Ən azı bir işçi seçin')
    if (schedule && !scheduledFor) return setError('Planlaşdırma vaxtını seçin')

    setSaving(true)
    const { status, data } = await createAnnouncement({
      title: title.trim() || null,
      message: message.trim(),
      audience,
      scheduledForLocal: schedule ? scheduledFor : null,
      recipientIds: audience === 'Selected' ? recipientIds : undefined,
    })
    setSaving(false)
    if (status === 200 && data && 'id' in data) {
      reset()
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

        <label className="form-label">Başlıq (istəyə görə)</label>
        <input
          className="inp"
          value={title}
          onChange={(e) => setTitle(e.target.value)}
          placeholder="Məsələn: Yenilik"
          maxLength={120}
          style={{ marginBottom: 12 }}
        />

        <label className="form-label">Mesaj</label>
        <textarea
          className="inp"
          rows={3}
          value={message}
          onChange={(e) => setMessage(e.target.value)}
          placeholder="Bildiriş mesajını yazın…"
          maxLength={2000}
          style={{ resize: 'vertical', marginBottom: 14 }}
        />

        <label className="form-label">Alıcılar</label>
        <div className="chip-row" style={{ marginTop: 4 }}>
          {AUDIENCE.map((a) => (
            <span
              key={a.value}
              className={`chip${audience === a.value ? ' active' : ''}`}
              title={a.hint}
              onClick={() => setAudience(a.value)}
            >
              {a.label}
            </span>
          ))}
        </div>

        {audience === 'Selected' && (
          <div style={{ border: '1px solid var(--c100)', borderRadius: 'var(--r)', padding: 12, marginBottom: 14 }}>
            <input
              className="inp"
              value={empSearch}
              onChange={(e) => setEmpSearch(e.target.value)}
              placeholder="İşçi axtar…"
              style={{ marginBottom: 8 }}
            />
            <div style={{ fontSize: 12, color: 'var(--c500)', marginBottom: 8 }}>
              {recipientIds.length} işçi seçilib
            </div>
            <div style={{ maxHeight: 220, overflowY: 'auto', display: 'flex', flexDirection: 'column', gap: 2 }}>
              {filteredEmployees.map((e) => (
                <label
                  key={e.id}
                  style={{ display: 'flex', alignItems: 'center', gap: 8, padding: '6px 4px', cursor: 'pointer', fontSize: 14 }}
                >
                  <input
                    type="checkbox"
                    checked={recipientIds.includes(e.id)}
                    onChange={() => toggleRecipient(e.id)}
                  />
                  {e.fullName}
                  <span className="muted" style={{ fontSize: 12 }}>{e.locationName ?? ''}</span>
                </label>
              ))}
            </div>
          </div>
        )}

        <label className="form-label" style={{ marginTop: audience === 'Selected' ? 0 : 4 }}>
          Göndərmə vaxtı
        </label>
        <div className="chip-row" style={{ marginTop: 4 }}>
          <span className={`chip${!schedule ? ' active' : ''}`} onClick={() => setSchedule(false)}>⚡ Dərhal</span>
          <span className={`chip${schedule ? ' active' : ''}`} onClick={() => setSchedule(true)}>🕒 Planlaşdır</span>
        </div>
        {schedule && (
          <input
            className="inp"
            type="datetime-local"
            value={scheduledFor}
            onChange={(e) => setScheduledFor(e.target.value)}
            style={{ width: 'auto', marginBottom: 8 }}
          />
        )}

        {error && (
          <div className="fb fb-err" style={{ marginTop: 10 }}>
            <IconX />
            <span>{error}</span>
          </div>
        )}

        <div style={{ marginTop: 14 }}>
          <button className="btn btn-primary" disabled={saving || !message.trim()} onClick={onPost}>
            <IconSend />
            {saving ? 'Göndərilir…' : schedule ? 'Yadda saxla & planlaşdır' : 'Göndər'}
          </button>
        </div>
      </div>

      <div className="card card-pad">
        <div className="card-title">Elanlar</div>
        {items.length === 0 ? (
          <div className="muted" style={{ padding: '16px 0' }}>Hələ elan yoxdur</div>
        ) : (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
            {items.map((a) => {
              const scheduledFuture = a.scheduledForUtc && new Date(a.scheduledForUtc) > new Date()
              return (
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
                      {a.title && <div style={{ fontWeight: 700, color: 'var(--c900)', marginBottom: 2 }}>{a.title}</div>}
                      <div style={{ whiteSpace: 'pre-line', fontSize: 14, color: 'var(--c700)' }}>{a.message}</div>
                      <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap', marginTop: 8 }}>
                        <span className="tag">
                          {AUDIENCE_LABEL[a.audience]}
                          {a.audience === 'Selected' && ` (${a.recipientCount})`}
                        </span>
                        {scheduledFuture && (
                          <span className="tag" style={{ background: 'var(--amber-bg)', color: 'var(--amber)' }}>
                            🕒 {new Date(a.scheduledForUtc!).toLocaleString('az-AZ')}
                          </span>
                        )}
                        {!a.isActive && <span className="tag">söndürülüb</span>}
                        <span className="tag muted">{new Date(a.createdAtUtc).toLocaleDateString('az-AZ')}</span>
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
              )
            })}
          </div>
        )}
      </div>
    </div>
  )
}
