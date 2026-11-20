import { useEffect, useMemo, useState, type FormEvent } from 'react'
import { getAdminLocations, type AdminLocation } from '../../api/admin'
import {
  addDailyReportRecipient,
  fetchDailyReportPreview,
  getDailyReport,
  newDailyReportConnectLink,
  removeDailyReportRecipient,
  saveDailyReportTime,
  sendDailyReportNow,
  updateDailyReportRecipient,
  type DailyReportRecipient,
  type DailyReportState,
} from '../../api/dailyReport'
import { IconAlert, IconCheck, IconDownload, IconEye, IconKey, IconPencil, IconSend, IconTrash, IconX } from '../../components/icons'
import { fmtDate, fmtDateTime } from '../../lib/format'

const ERRORS: Record<string, string> = {
  NameRequired: 'Ad tələb olunur',
  NameTooLong: 'Ad çox uzundur',
  LocationNotFound: 'Seçilmiş filial tapılmadı',
  TimeInvalid: 'Vaxt yanlışdır (SS:dd)',
  BotNotConfigured: 'Telegram botu bu serverdə qoşulmayıb',
  NoConnectedRecipients: 'Telegram-a qoşulmuş alıcı yoxdur',
  RecipientNotFound: 'Alıcı tapılmadı',
}

const DEFAULT_TIME = '18:00'

function errorText(data: unknown, fallback: string): string {
  if (data && typeof data === 'object' && 'error' in data)
    return ERRORS[(data as { error: string }).error] ?? fallback
  return fallback
}

/** «Rəhbərlik hesabatı»: the daily attendance summary that goes to leadership on Telegram. Company admins only. */
export function DailyReportPage() {
  const [state, setState] = useState<DailyReportState | null>(null)
  const [loadFailed, setLoadFailed] = useState(false)
  const [locations, setLocations] = useState<AdminLocation[]>([])
  const [error, setError] = useState<string | null>(null)
  const [ok, setOk] = useState<string | null>(null)
  const [busy, setBusy] = useState<string | null>(null)

  // Schedule
  const [enabled, setEnabled] = useState(false)
  const [time, setTime] = useState(DEFAULT_TIME)

  // Recipient form (add or edit)
  const [editingId, setEditingId] = useState<string | null>(null)
  const [name, setName] = useState('')
  const [wholeCompany, setWholeCompany] = useState(true)
  const [picked, setPicked] = useState<Set<string>>(new Set())
  const [filter, setFilter] = useState('')

  // The one-time «connect» link just issued, shown until dismissed.
  const [connect, setConnect] = useState<{ name: string; link: string } | null>(null)
  const [copied, setCopied] = useState(false)

  async function refresh() {
    const res = await getDailyReport()
    if (res.status === 200 && res.data && 'recipients' in res.data) {
      setState(res.data)
      setLoadFailed(false)
      return res.data
    }
    setLoadFailed(true)
    return null
  }

  useEffect(() => {
    void (async () => {
      const [s, locs] = await Promise.all([refresh(), getAdminLocations()])
      if (s) {
        setEnabled(s.time !== null)
        setTime(s.time ?? DEFAULT_TIME)
      }
      if (locs.status === 200 && Array.isArray(locs.data)) setLocations(locs.data.filter((l) => l.isActive))
    })()
  }, [])

  const locationName = useMemo(() => new Map(locations.map((l) => [l.id, l.name])), [locations])
  const shownLocations = useMemo(() => {
    const q = filter.trim().toLocaleLowerCase('az')
    return q ? locations.filter((l) => l.name.toLocaleLowerCase('az').includes(q)) : locations
  }, [locations, filter])

  function flash(message: string) {
    setError(null)
    setOk(message)
  }
  function fail(message: string) {
    setOk(null)
    setError(message)
  }

  async function onSaveTime(e: FormEvent) {
    e.preventDefault()
    setBusy('time')
    const res = await saveDailyReportTime(enabled ? time : null)
    setBusy(null)
    if (res.status === 200) {
      flash(enabled ? `Hesabat hər gün saat ${time}-da göndəriləcək` : 'Avtomatik göndəriş söndürüldü')
      await refresh()
    } else fail(errorText(res.data, 'Yadda saxlanmadı'))
  }

  function resetForm() {
    setEditingId(null)
    setName('')
    setWholeCompany(true)
    setPicked(new Set())
    setFilter('')
  }

  function startEdit(r: DailyReportRecipient) {
    setEditingId(r.id)
    setName(r.name)
    setWholeCompany(r.locationIds.length === 0)
    setPicked(new Set(r.locationIds))
    setFilter('')
    setError(null)
    setOk(null)
  }

  function togglePicked(id: string) {
    setPicked((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })
  }

  async function onSaveRecipient(e: FormEvent) {
    e.preventDefault()
    if (!wholeCompany && picked.size === 0) {
      fail('Ən azı bir filial seçin və ya «Bütün şirkət» seçin')
      return
    }
    const ids = wholeCompany ? [] : [...picked]
    setBusy('recipient')
    if (editingId) {
      const res = await updateDailyReportRecipient(editingId, name.trim(), ids)
      setBusy(null)
      if (res.status === 200) {
        flash('Alıcı yeniləndi')
        resetForm()
        await refresh()
      } else fail(errorText(res.data, 'Yadda saxlanmadı'))
      return
    }
    const res = await addDailyReportRecipient(name.trim(), ids)
    setBusy(null)
    if (res.status === 200 && res.data && 'id' in res.data) {
      const link = 'connectLink' in res.data ? res.data.connectLink : null
      if (link) setConnect({ name: name.trim(), link })
      setCopied(false)
      flash(link ? 'Alıcı əlavə olundu — qoşulma linkini ona göndərin' : 'Alıcı əlavə olundu')
      resetForm()
      await refresh()
    } else fail(errorText(res.data, 'Yadda saxlanmadı'))
  }

  async function onConnectLink(r: DailyReportRecipient) {
    if (r.linked && !window.confirm(`${r.name} artıq qoşulub. Yeni link verilsin? (yeni telefon üçün — köhnə çat link işlənəndə əvəz olunur)`)) return
    setBusy(`link:${r.id}`)
    const res = await newDailyReportConnectLink(r.id)
    setBusy(null)
    if (res.status === 200 && res.data && 'connectLink' in res.data) {
      setConnect({ name: r.name, link: res.data.connectLink })
      setCopied(false)
      setError(null)
      setOk(null)
      await refresh()
    } else fail(errorText(res.data, 'Link yaradılmadı'))
  }

  async function onRemove(r: DailyReportRecipient) {
    if (!window.confirm(`${r.name} alıcılardan çıxarılsın? Ona hesabat getməyəcək və köhnə linkləri açılmayacaq.`)) return
    setBusy(`remove:${r.id}`)
    const res = await removeDailyReportRecipient(r.id)
    setBusy(null)
    if (res.status === 200) {
      flash('Alıcı çıxarıldı')
      if (editingId === r.id) resetForm()
      await refresh()
    } else fail(errorText(res.data, 'Çıxarılmadı'))
  }

  async function onSendNow(r?: DailyReportRecipient) {
    const who = r ? r.name : 'bütün qoşulmuş alıcılara'
    if (!window.confirm(`Bugünkü hesabat indi ${r ? `${who} üçün ` : who} göndərilsin?`)) return
    setBusy(r ? `send:${r.id}` : 'send')
    const res = await sendDailyReportNow(r?.id)
    setBusy(null)
    if (res.status === 200 && res.data && 'results' in res.data) {
      const failed = res.data.results.filter((x) => !x.sent)
      if (failed.length === 0) flash(`Göndərildi: ${res.data.results.map((x) => x.recipientName).join(', ')}`)
      else fail(`Göndərilmədi: ${failed.map((x) => `${x.recipientName}${x.error ? ` (${x.error})` : ''}`).join('; ')} — yenidən cəhd ediləcək`)
    } else fail(errorText(res.data, 'Göndərilmədi'))
    await refresh()
  }

  async function onPreview(format: 'html' | 'pdf', r?: DailyReportRecipient) {
    // Opened before the request so the browser treats it as the click's own window, not a popup.
    const tab = format === 'html' ? window.open('', '_blank') : null
    setBusy(`preview:${format}:${r?.id ?? ''}`)
    const blob = await fetchDailyReportPreview(format, r?.id)
    setBusy(null)
    if (!blob) {
      tab?.close()
      fail('Önizləmə hazırlanmadı')
      return
    }
    const url = URL.createObjectURL(blob)
    if (tab) {
      tab.location.href = url
    } else {
      const a = document.createElement('a')
      a.href = url
      a.download = format === 'pdf' ? 'davamiyyet-hesabati.pdf' : 'davamiyyet-hesabati.html'
      document.body.appendChild(a)
      a.click()
      a.remove()
    }
    setTimeout(() => URL.revokeObjectURL(url), 60_000)
  }

  async function copyLink() {
    if (!connect) return
    try {
      await navigator.clipboard.writeText(connect.link)
      setCopied(true)
    } catch {
      window.prompt('Linki kopyalayın:', connect.link)
    }
  }

  function scopeText(r: DailyReportRecipient) {
    if (r.locationIds.length === 0) return 'Bütün şirkət'
    const names = r.locationIds.map((id) => locationName.get(id)).filter(Boolean) as string[]
    if (names.length === 0) return `${r.locationIds.length} filial`
    return names.length <= 2 ? names.join(', ') : `${names.slice(0, 2).join(', ')} və daha ${names.length - 2}`
  }

  if (loadFailed && !state) {
    return (
      <div className="fb fb-err" style={{ maxWidth: 640 }}>
        <IconX />
        <span>Məlumat yüklənmədi. Səhifəni yeniləyin.</span>
      </div>
    )
  }
  if (!state) return <div className="muted" style={{ padding: 24 }}>Yüklənir…</div>

  const linkedCount = state.recipients.filter((r) => r.linked).length
  const canSend = state.botConfigured && linkedCount > 0

  return (
    <div style={{ display: 'grid', gap: 16, maxWidth: 980 }}>
      {!state.botConfigured && (
        <div className="fb fb-warn">
          <IconAlert />
          <span>
            Telegram botu bu serverdə qoşulmayıb — hesabat göndərilə bilməz. Önizləmə (HTML və PDF) işləyir.
          </span>
        </div>
      )}

      {error && (
        <div className="fb fb-err" role="alert">
          <IconX />
          <span>{error}</span>
        </div>
      )}
      {ok && (
        <div className="fb fb-ok" role="status">
          <IconCheck />
          <span>{ok}</span>
        </div>
      )}

      {/* ── Today's report, now ─────────────────────────────────────────────── */}
      <div className="card card-pad">
        <div className="card-title" style={{ marginBottom: 6 }}>Bugünkü hesabat</div>
        <p className="muted" style={{ fontSize: 13, lineHeight: 1.6, margin: '0 0 14px' }}>
          Ştat, işçi sayı, vakant yer, işdə olanlar, istirahət, məzuniyyət, icazə/xəstəlik, ezamiyyət, işə
          gəlməyənlər və növbəsi hələ başlamayanlar — filial və struktur üzrə. Önizləmə heç kimə göndərilmir.
        </p>
        <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
          <button className="btn" type="button" disabled={busy !== null} onClick={() => void onPreview('html')}>
            <IconEye /> Önizləmə
          </button>
          <button className="btn" type="button" disabled={busy !== null} onClick={() => void onPreview('pdf')}>
            <IconDownload /> {busy === 'preview:pdf:' ? 'PDF hazırlanır…' : 'PDF yüklə'}
          </button>
          <button
            className="btn btn-primary"
            type="button"
            disabled={busy !== null || !canSend}
            title={canSend ? undefined : 'Əvvəlcə alıcı əlavə edib Telegram-a qoşun'}
            onClick={() => void onSendNow()}
          >
            <IconSend /> {busy === 'send' ? 'Göndərilir…' : 'Hesabatı indi göndər'}
          </button>
        </div>
      </div>

      {/* ── Schedule ────────────────────────────────────────────────────────── */}
      <form className="card card-pad" onSubmit={onSaveTime}>
        <div className="card-title" style={{ marginBottom: 6 }}>Avtomatik göndəriş</div>
        <p className="muted" style={{ fontSize: 13, lineHeight: 1.6, margin: '0 0 14px' }}>
          Hər gün seçilmiş saatda (Bakı vaxtı) qoşulmuş alıcıların hər birinə bir dəfə göndərilir. Göndərilə
          bilməsə, sistem bir neçə dəfə təkrar cəhd edir.
        </p>
        <div style={{ display: 'flex', gap: 14, alignItems: 'center', flexWrap: 'wrap' }}>
          <label style={{ display: 'inline-flex', gap: 8, alignItems: 'center', fontSize: 14, fontWeight: 600, color: 'var(--c900)' }}>
            <input type="checkbox" checked={enabled} onChange={(e) => setEnabled(e.target.checked)} />
            Hər gün göndər
          </label>
          <input
            className="inp"
            type="time"
            aria-label="Göndəriş vaxtı"
            style={{ width: 130 }}
            disabled={!enabled}
            required={enabled}
            value={time}
            onChange={(e) => setTime(e.target.value)}
          />
          <button className="btn btn-primary" type="submit" disabled={busy !== null}>
            <IconCheck /> {busy === 'time' ? 'Yadda saxlanır…' : 'Yadda saxla'}
          </button>
          <span className="muted" style={{ fontSize: 12 }}>
            {state.time ? `İndi: hər gün ${state.time}` : 'İndi: söndürülüb'}
          </span>
        </div>
      </form>

      {/* ── The one-time connect link just issued ───────────────────────────── */}
      {connect && (
        <div className="card card-pad" style={{ borderColor: 'var(--blue)' }}>
          <div className="card-title" style={{ marginBottom: 6 }}>{connect.name} üçün qoşulma linki</div>
          <p className="muted" style={{ fontSize: 13, lineHeight: 1.6, margin: '0 0 12px' }}>
            Linki yalnız bu şəxsə göndərin. Telefonunda açıb Telegram-da <b>«Start»</b> düyməsinə bassın — bot
            «Qoşuldunuz» yazacaq. Link 48 saat etibarlıdır və bir dəfə işləyir.
          </p>
          <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', alignItems: 'center' }}>
            <input className="inp mono" readOnly value={connect.link} style={{ flex: '1 1 320px', fontSize: 12 }} onFocus={(e) => e.target.select()} />
            <button className="btn btn-primary" type="button" onClick={() => void copyLink()}>
              <IconCheck /> {copied ? 'Kopyalandı' : 'Kopyala'}
            </button>
            <button className="btn btn-ghost" type="button" onClick={() => setConnect(null)}>
              Bağla
            </button>
          </div>
        </div>
      )}

      {/* ── Recipients ──────────────────────────────────────────────────────── */}
      <div className="card card-pad">
        <div className="card-title" style={{ marginBottom: 6 }}>Alıcılar</div>
        <p className="muted" style={{ fontSize: 13, lineHeight: 1.6, margin: '0 0 14px' }}>
          Hər alıcı ya bütün şirkətin, ya da yalnız seçilmiş filialların hesabatını alır. Alıcını çıxaranda ona
          göndəriş dayanır və əvvəlki linkləri də açılmır.
        </p>

        <div className="tbl-wrap tbl-cards" style={{ marginBottom: 18 }}>
          <table>
            <thead>
              <tr>
                <th>Ad</th>
                <th>Əhatə</th>
                <th>Telegram</th>
                <th style={{ textAlign: 'right' }}>Əməliyyat</th>
              </tr>
            </thead>
            <tbody>
              {state.recipients.map((r) => (
                <tr key={r.id}>
                  <td data-label="Ad" style={{ fontWeight: 600 }}>{r.name}</td>
                  <td data-label="Əhatə">{scopeText(r)}</td>
                  <td data-label="Telegram">
                    {r.linked ? (
                      <span className="pill pill-ok">Qoşulub</span>
                    ) : r.linkPending ? (
                      <span className="pill pill-info">Link gözləyir</span>
                    ) : (
                      <span className="pill">Qoşulmayıb</span>
                    )}
                  </td>
                  <td data-label="">
                    <div style={{ display: 'flex', gap: 6, justifyContent: 'flex-end', flexWrap: 'wrap' }}>
                      {state.botConfigured && (
                        <button className="btn btn-sm" type="button" disabled={busy !== null} onClick={() => void onConnectLink(r)}>
                          <IconKey /> {r.linked ? 'Yeni link' : 'Qoşulma linki'}
                        </button>
                      )}
                      <button className="btn btn-sm" type="button" disabled={busy !== null} onClick={() => void onPreview('html', r)}>
                        <IconEye /> Önizləmə
                      </button>
                      {r.linked && state.botConfigured && (
                        <button className="btn btn-sm" type="button" disabled={busy !== null} onClick={() => void onSendNow(r)}>
                          <IconSend /> {busy === `send:${r.id}` ? 'Göndərilir…' : 'İndi göndər'}
                        </button>
                      )}
                      <button className="btn btn-sm" type="button" disabled={busy !== null} onClick={() => startEdit(r)}>
                        <IconPencil /> Düzəlt
                      </button>
                      <button className="btn btn-danger btn-sm" type="button" disabled={busy !== null} onClick={() => void onRemove(r)}>
                        <IconTrash /> Çıxar
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
              {state.recipients.length === 0 && (
                <tr>
                  <td colSpan={4} className="muted" style={{ textAlign: 'center', padding: 24 }}>
                    Hələ alıcı yoxdur — aşağıdan rəhbərlikdən birini əlavə edin
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>

        <form onSubmit={onSaveRecipient} style={{ borderTop: '1px solid var(--c100)', paddingTop: 16 }}>
          <div style={{ fontWeight: 700, color: 'var(--c900)', marginBottom: 12 }}>
            {editingId ? 'Alıcını düzəlt' : 'Yeni alıcı'}
          </div>
          <div className="form-row cols2">
            <div>
              <label className="form-label" htmlFor="dr-name">Ad, vəzifə</label>
              <input
                id="dr-name"
                className="inp"
                required
                maxLength={120}
                placeholder="məs. Baş direktor — Əliyev Ramin"
                value={name}
                onChange={(e) => setName(e.target.value)}
              />
            </div>
            <div>
              <span className="form-label">Əhatə</span>
              <div style={{ display: 'flex', gap: 16, alignItems: 'center', minHeight: 38, flexWrap: 'wrap' }}>
                <label style={{ display: 'inline-flex', gap: 6, alignItems: 'center', fontSize: 14 }}>
                  <input type="radio" name="dr-scope" checked={wholeCompany} onChange={() => setWholeCompany(true)} />
                  Bütün şirkət
                </label>
                <label style={{ display: 'inline-flex', gap: 6, alignItems: 'center', fontSize: 14 }}>
                  <input type="radio" name="dr-scope" checked={!wholeCompany} onChange={() => setWholeCompany(false)} />
                  Seçilmiş filiallar
                </label>
              </div>
            </div>
          </div>

          {!wholeCompany && (
            <div style={{ marginBottom: 14 }}>
              <div style={{ display: 'flex', gap: 8, alignItems: 'center', marginBottom: 8, flexWrap: 'wrap' }}>
                <input
                  className="inp"
                  placeholder="Filial axtar…"
                  aria-label="Filial axtar"
                  style={{ flex: '1 1 220px' }}
                  value={filter}
                  onChange={(e) => setFilter(e.target.value)}
                />
                <span className="muted" style={{ fontSize: 12 }}>{picked.size} seçilib</span>
              </div>
              <div
                style={{
                  maxHeight: 240,
                  overflowY: 'auto',
                  border: '1px solid var(--c200)',
                  borderRadius: 10,
                  padding: '6px 10px',
                  display: 'grid',
                  gridTemplateColumns: 'repeat(auto-fill, minmax(220px, 1fr))',
                  gap: '2px 12px',
                }}
              >
                {shownLocations.map((l) => (
                  <label key={l.id} style={{ display: 'flex', gap: 8, alignItems: 'center', fontSize: 13, padding: '5px 0' }}>
                    <input type="checkbox" checked={picked.has(l.id)} onChange={() => togglePicked(l.id)} />
                    <span>{l.name}</span>
                  </label>
                ))}
                {shownLocations.length === 0 && <span className="muted" style={{ fontSize: 13, padding: '6px 0' }}>Filial tapılmadı</span>}
              </div>
            </div>
          )}

          <div style={{ display: 'flex', gap: 8 }}>
            <button className="btn btn-primary" type="submit" disabled={busy !== null}>
              <IconCheck /> {busy === 'recipient' ? 'Yadda saxlanır…' : editingId ? 'Yadda saxla' : 'Əlavə et'}
            </button>
            {editingId && (
              <button className="btn btn-ghost" type="button" onClick={resetForm}>
                Ləğv et
              </button>
            )}
          </div>
        </form>
      </div>

      {/* ── Delivery log ────────────────────────────────────────────────────── */}
      <div className="card card-pad">
        <div className="card-title">Son göndərişlər</div>
        <div className="tbl-wrap tbl-cards">
          <table>
            <thead>
              <tr>
                <th>Hesabat günü</th>
                <th>Alıcı</th>
                <th>Növ</th>
                <th>Status</th>
                <th>Vaxt</th>
              </tr>
            </thead>
            <tbody>
              {state.deliveries.map((d) => (
                <tr key={d.id}>
                  <td data-label="Hesabat günü" className="mono">{fmtDate(d.date)}</td>
                  <td data-label="Alıcı">{d.recipientName}</td>
                  <td data-label="Növ">{d.kind === 'manual' ? 'Əl ilə' : 'Avtomatik'}</td>
                  <td data-label="Status">
                    {d.status === 'sent' ? (
                      <span className="badge b-present">Göndərildi</span>
                    ) : d.status === 'failed' ? (
                      <span className="badge b-absent" title={d.lastError ?? undefined}>Alınmadı</span>
                    ) : (
                      <span className="badge b-pending" title={d.lastError ?? undefined}>
                        {d.attempts > 0 ? `Təkrar cəhd (${d.attempts})` : 'Növbədə'}
                      </span>
                    )}
                    {d.status === 'failed' && d.lastError && (
                      <div className="muted" style={{ fontSize: 11, marginTop: 4, maxWidth: 320 }}>{d.lastError}</div>
                    )}
                  </td>
                  <td data-label="Vaxt" className="mono">{fmtDateTime(d.sentAtUtc ?? d.createdAtUtc)}</td>
                </tr>
              ))}
              {state.deliveries.length === 0 && (
                <tr>
                  <td colSpan={5} className="muted" style={{ textAlign: 'center', padding: 24 }}>
                    Hələ göndəriş olmayıb
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  )
}
