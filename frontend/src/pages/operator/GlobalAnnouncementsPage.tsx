import { useEffect, useState, type FormEvent } from 'react'
import {
  getGlobalAnnouncements,
  createGlobalAnnouncement,
  retireGlobalAnnouncement,
  type GlobalAnnouncement,
} from '../../api/admin'
import { fmtDateTime } from '../../lib/format'
import { useCan } from './OperatorContext'
import { EmptyState } from './console'

export function GlobalAnnouncementsPage() {
  const [rows, setRows] = useState<GlobalAnnouncement[]>([])
  const [loading, setLoading] = useState(true)
  const [title, setTitle] = useState('')
  const [message, setMessage] = useState('')
  const [sending, setSending] = useState(false)
  const [busyId, setBusyId] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const canAnnounce = useCan('Announce')

  async function load() {
    try {
      const res = await getGlobalAnnouncements()
      if (res.status === 200 && Array.isArray(res.data)) setRows(res.data)
      else if (res.status === 403) setError('İcazəniz yoxdur')
      else setError('Elanları yükləmək alınmadı')
    } catch {
      // A rejected fetch / non-JSON error body must not pin the spinner forever, nor read as "empty".
      setError('Elanları yükləmək alınmadı')
    } finally {
      setLoading(false)
    }
  }
  useEffect(() => { void load() }, [])

  async function send(e: FormEvent) {
    e.preventDefault()
    const t = title.trim()
    const m = message.trim()
    if (!t || !m) { setError('Başlıq və mətn tələb olunur'); return }
    if (!window.confirm('Bu elan BÜTÜN şirkətlərin işçilərinə göndəriləcək. Davam edək?')) return
    setSending(true); setError(null)
    const { status } = await createGlobalAnnouncement({ title: t, message: m })
    setSending(false)
    if (status === 200) { setTitle(''); setMessage(''); await load() }
    else setError('Göndərilmədi')
  }

  async function retire(a: GlobalAnnouncement) {
    if (!window.confirm(`«${a.title}» dayandırılsın? İşçilər artıq görməyəcək.`)) return
    setBusyId(a.id); setError(null)
    const { status } = await retireGlobalAnnouncement(a.id)
    setBusyId(null)
    if (status === 200) await load(); else setError('Alınmadı')
  }

  return (
    <div>
      {error && <div className="fb fb-err" style={{ marginBottom: 14 }}>{error}</div>}

      {canAnnounce && (
      <form onSubmit={send} className="card card-pad" style={{ marginBottom: 18 }}>
        <div className="card-title">Yeni qlobal elan</div>
        <div
          style={{
            fontSize: 12, color: 'var(--clay)', background: 'rgba(200,60,40,0.08)',
            borderRadius: 8, padding: '6px 10px', marginBottom: 12,
          }}
        >
          ⚠️ Bu elan platformadakı <b>bütün şirkətlərin bütün işçilərinə</b> ana ekranda görünəcək.
        </div>
        <div style={{ marginBottom: 10 }}>
          <label className="form-label">Başlıq</label>
          <input className="inp" value={title} onChange={(e) => setTitle(e.target.value)} maxLength={120} placeholder="məs. Planlı texniki iş" />
        </div>
        <div style={{ marginBottom: 12 }}>
          <label className="form-label">Mətn</label>
          <textarea
            className="inp"
            value={message}
            onChange={(e) => setMessage(e.target.value)}
            rows={3}
            maxLength={1000}
            placeholder="Bazar günü 02:00–04:00 arası sistem qısa müddət əlçatmaz ola bilər."
          />
        </div>
        <button className="btn btn-primary" disabled={sending || !title.trim() || !message.trim()}>
          {sending ? 'Göndərilir…' : 'Bütün şirkətlərə göndər'}
        </button>
      </form>
      )}

      <div className="card">
        <table className="tbl">
          <thead>
            <tr><th>Elan</th><th>Kim</th><th>Tarix</th><th>Status</th><th /></tr>
          </thead>
          <tbody>
            {loading && <tr><td colSpan={5} className="muted" style={{ padding: 18 }}>Yüklənir…</td></tr>}
            {!loading && rows.length === 0 && (
              <tr><td colSpan={5} style={{ padding: 0 }}>
                <EmptyState
                  icon="📣"
                  title="Hələ qlobal elan yoxdur"
                  note="Buradan göndərilən elan bütün şirkətlərin işçilərinin ana ekranında görünür."
                />
              </td></tr>
            )}
            {rows.map((a) => (
              <tr key={a.id} style={{ opacity: a.isActive ? 1 : 0.55 }}>
                <td>
                  <div style={{ fontWeight: 700 }}>{a.title}</div>
                  <div style={{ fontSize: 12, color: 'var(--c400)', maxWidth: 460 }}>{a.message}</div>
                </td>
                <td style={{ fontSize: 13 }}>{a.createdByName}</td>
                <td style={{ fontSize: 13, whiteSpace: 'nowrap' }}>{fmtDateTime(a.createdAtUtc)}</td>
                <td>
                  {a.isActive ? (
                    <span className="tag" style={{ background: 'var(--leaf-bg)', color: 'var(--leaf-d)' }}>Aktiv</span>
                  ) : (
                    <span className="tag" style={{ background: 'var(--tag-neutral, rgba(0,0,0,0.05))', color: 'var(--c400)' }}>Dayandırılıb</span>
                  )}
                </td>
                <td style={{ textAlign: 'right', whiteSpace: 'nowrap' }}>
                  {a.isActive && canAnnounce && (
                    <button className="btn btn-sm" disabled={busyId === a.id} onClick={() => void retire(a)}>
                      {busyId === a.id ? '…' : 'Dayandır'}
                    </button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  )
}
