import { useEffect, useState } from 'react'
import {
  getEmployeePresenceChecks,
  setEmployeePresenceChecks,
  type PresenceCheckView,
} from '../../api/presenceChecks'
import { fmtDateTime, fmtFullDateTime, fmtTime } from '../../lib/format'
import { presenceDistance, presenceResultLabel, presenceWeekLine } from '../../lib/presenceCheck'
import { IconCheck, IconX } from '../../components/icons'

const TONE: Record<'ok' | 'bad' | 'muted' | 'warn', { color: string; background: string }> = {
  ok: { color: 'var(--leaf-d)', background: 'var(--leaf-bg)' },
  bad: { color: 'var(--clay)', background: 'var(--clay-bg)' },
  warn: { color: 'var(--amber)', background: 'var(--amber-bg)' },
  muted: { color: 'var(--c600)', background: 'var(--c100)' },
}

/**
 * «Mövcudluq yoxlaması» on a person's profile — see PresenceCheck on the server.
 *
 * A switch with an end (7, 14 or 30 days), the last week in one sentence and the history of what was asked. The
 * planned moments are never shown: a check that is known in advance checks nothing. «Cavab yoxdur» is listed but
 * not coloured as a failure — a busy person and an absent one look the same from here; «filialdan kənarda» is the
 * one result that means something, and the only one the manager is pushed about.
 */
export function PresenceChecksCard({ employeeId }: { employeeId: string }) {
  const [view, setView] = useState<PresenceCheckView | null>(null)
  const [days, setDays] = useState(14)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [hidden, setHidden] = useState(false)

  useEffect(() => {
    let alive = true
    void (async () => {
      try {
        const r = await getEmployeePresenceChecks(employeeId)
        if (!alive) return
        if (r.status === 200 && r.data && 'on' in r.data) setView(r.data)
        // Not this caller's to see (403) — the card simply is not there for them.
        else if (r.status === 403) setHidden(true)
        else setError('Yüklənmədi')
      } catch {
        if (alive) setError('Yüklənmədi — internet bağlantısını yoxlayın')
      }
    })()
    return () => { alive = false }
  }, [employeeId])

  async function change(next: number | null) {
    if (next === null && !window.confirm('Mövcudluq yoxlaması söndürülsün? Bu gün üçün planlaşdırılmış yoxlamalar da ləğv olunacaq.'))
      return
    setBusy(true); setError(null)
    try {
      const r = await setEmployeePresenceChecks(employeeId, next)
      if (r.status === 200 && r.data && 'on' in r.data) setView(r.data)
      else setError(r.status === 403 ? 'Bu işçi üçün dəyişmək icazəniz yoxdur' : 'Yadda saxlanmadı')
    } catch {
      setError('Yadda saxlanmadı — internet bağlantısını yoxlayın')
    } finally {
      setBusy(false)
    }
  }

  if (hidden) return null

  return (
    <div className="card card-pad">
      <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 4, flexWrap: 'wrap' }}>
        <div className="card-title" style={{ margin: 0 }}>Mövcudluq yoxlaması</div>
        {view && (
          <span className="tag" style={view.on ? TONE.ok : undefined}>
            {view.on ? 'Açıq' : 'Söndürülüb'}
          </span>
        )}
      </div>
      <div className="muted" style={{ fontSize: 12.5, marginBottom: 14, lineHeight: 1.5 }}>
        Açıq olduğu müddətdə işçidən iş günü ərzində ən çox 2 dəfə, təsadüfi vaxtda yerini təsdiqləməsi istənir.
        Filialdan kənarda cavab versə, sizə bildiriş gəlir. Cavabsız yoxlamalar yalnız burada sayılır.
        Giriş, saat və maaşa heç nə təsir etmir.
      </div>

      {error && <div className="fb fb-err" style={{ marginBottom: 10 }}><IconX /><span>{error}</span></div>}

      {view === null ? (
        !error && <div className="muted" style={{ fontSize: 13 }}>Yüklənir…</div>
      ) : (
        <>
          {view.on ? (
            <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap', marginBottom: 14 }}>
              <div style={{ flex: 1, minWidth: 220, fontSize: 13, display: 'flex', alignItems: 'center', gap: 6, flexWrap: 'wrap' }}>
                <IconCheck style={{ width: 15, height: 15, flexShrink: 0, color: 'var(--leaf-d)' }} />
                <span>
                  <b>{view.untilUtc ? fmtFullDateTime(view.untilUtc) : ''}</b>-dək açıqdır
                  {view.setByName && <span className="muted"> · {view.setByName} açıb</span>}
                </span>
              </div>
              {view.canChange && (
                <button className="btn btn-sm btn-ghost-danger" disabled={busy} onClick={() => void change(null)}>
                  Söndür
                </button>
              )}
            </div>
          ) : view.canChange ? (
            <div style={{ display: 'flex', alignItems: 'end', gap: 10, flexWrap: 'wrap', marginBottom: 14 }}>
              <label className="form-label" style={{ margin: 0, minWidth: 160 }}>
                Müddət
                <select
                  className="inp"
                  value={days}
                  onChange={(e) => setDays(Number(e.target.value))}
                  style={{ height: 38, padding: '0 12px', fontSize: 13, marginTop: 4 }}
                >
                  {view.allowedDays.map((d) => (
                    <option key={d} value={d}>{d} gün</option>
                  ))}
                </select>
              </label>
              <button
                className="btn btn-primary"
                disabled={busy}
                onClick={() => void change(days)}
                style={{ height: 38, padding: '0 16px', fontSize: 13 }}
              >
                <IconCheck /> Aç
              </button>
            </div>
          ) : (
            <div className="muted" style={{ fontSize: 13, marginBottom: 14 }}>
              Bu işçi üçün yoxlamanı açmaq icazəniz yoxdur.
            </div>
          )}

          <div style={{ fontSize: 13, fontWeight: 600, color: 'var(--c700)', marginBottom: 10 }}>
            {presenceWeekLine(view.week)}
          </div>

          {view.checks.length > 0 && (
            <div style={{ border: '1px solid var(--c100)', borderRadius: 12, overflow: 'hidden' }}>
              {view.checks.map((c, i) => {
                const label = presenceResultLabel(c.result)
                const distance = presenceDistance(c)
                return (
                  <div
                    key={c.id}
                    style={{
                      display: 'flex', alignItems: 'center', gap: 12, padding: '9px 14px', flexWrap: 'wrap',
                      borderTop: i === 0 ? 'none' : '1px solid var(--c100)', fontSize: 13,
                    }}
                  >
                    <span
                      className="mono"
                      style={{
                        fontSize: 12, fontWeight: 600, color: 'var(--c700)', background: 'var(--c100)',
                        padding: '3px 8px', borderRadius: 6, whiteSpace: 'nowrap', fontVariantNumeric: 'tabular-nums',
                      }}
                    >
                      {fmtDateTime(c.sentAtUtc)}
                    </span>
                    <span className="tag" style={TONE[label.tone]}>{label.text}</span>
                    {c.late && <span className="tag">gec cavab</span>}
                    <span className="muted" style={{ flex: 1, minWidth: 160 }}>
                      {[
                        distance,
                        c.respondedAtUtc && c.result !== 'NoAnswer' ? `cavab ${fmtTime(c.respondedAtUtc)}` : null,
                        c.branch,
                        c.reached === 0 ? 'bildiriş telefona çatmayıb' : null,
                      ].filter(Boolean).join(' · ')}
                    </span>
                  </div>
                )
              })}
            </div>
          )}
        </>
      )}
    </div>
  )
}
