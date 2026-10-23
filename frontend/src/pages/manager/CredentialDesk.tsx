import { useEffect, useRef, useState } from 'react'
import {
  changeManagerEmployeePhone,
  findCredentialTargets,
  resetManagerEmployeePin,
  type CredentialTarget,
} from '../../api/manager'

const ERRORS: Record<string, string> = {
  PhoneInvalid: 'Nömrə düzgün deyil — ən azı 7 rəqəm yazın',
  PhoneAlreadyExists: 'Bu nömrə ilə başqa bir hesab artıq var',
  ManagerCannotManageRole: 'Bu hesabı dəyişə bilməzsiniz',
  CannotManageOperator: 'Bu hesabı dəyişə bilməzsiniz',
  EmployeeNotFound: 'İşçi tapılmadı',
}

const errorCode = (data: unknown) =>
  data && typeof data === 'object' && 'error' in data ? String((data as { error: unknown }).error) : ''

type Notice = { kind: 'ok' | 'err'; text: string; pin?: string }

/**
 * Company-wide PIN reset and login-number change for a manager.
 *
 * The roster above it is the manager's own branches and nothing more, which is right for editing people
 * and wrong for the two things that cannot wait: a worker at an area nobody manages who has forgotten
 * their PIN, or a fellow manager who has changed phones. Those were an admin's to do, and the admin is
 * one person. The server decides who qualifies — any plain employee or fellow manager, never an admin —
 * and writes every use to the audit log under the manager's name, which this card says up front.
 */
export function CredentialDesk() {
  const [q, setQ] = useState('')
  const [results, setResults] = useState<CredentialTarget[]>([])
  const [searching, setSearching] = useState(false)
  const [phoneFor, setPhoneFor] = useState<string | null>(null)
  const [phone, setPhone] = useState('')
  const [busy, setBusy] = useState(false)
  const [notice, setNotice] = useState<Notice | null>(null)
  // Typing is faster than the network: only the newest query may fill the list.
  const latest = useRef(0)

  async function search(term: string) {
    const mine = ++latest.current
    setSearching(true)
    const r = await findCredentialTargets(term)
    if (mine !== latest.current) return
    setSearching(false)
    setResults(r.status === 200 && Array.isArray(r.data) ? r.data : [])
  }

  useEffect(() => {
    const term = q.trim()
    if (term.length < 2) {
      latest.current++
      setResults([])
      setSearching(false)
      return
    }
    const t = setTimeout(() => void search(term), 300)
    return () => clearTimeout(t)
  }, [q])

  async function reset(t: CredentialTarget) {
    const warning = t.isManager
      ? `${t.fullName} MENECERDİR.\n\nYeni PIN yaratsanız onun köhnə PIN-i dərhal işləməyəcək və yeni PIN sizdə olacaq. Bu əməliyyat jurnala sizin adınızla yazılır.\n\nDavam edilsin?`
      : `${t.fullName} üçün yeni müvəqqəti PIN yaradılsın?\n\nKöhnə PIN dərhal işləməyəcək — yenisini işçiyə verməlisiniz.`
    if (!window.confirm(warning)) return
    setBusy(true)
    setNotice(null)
    const r = await resetManagerEmployeePin(t.id)
    setBusy(false)
    if (r.status === 200 && r.data && 'tempPin' in r.data) setNotice({ kind: 'ok', text: t.fullName, pin: r.data.tempPin })
    else setNotice({ kind: 'err', text: ERRORS[errorCode(r.data)] ?? 'PIN sıfırlanmadı' })
  }

  async function savePhone(t: CredentialTarget) {
    setBusy(true)
    setNotice(null)
    const r = await changeManagerEmployeePhone(t.id, phone)
    setBusy(false)
    if (r.status === 200) {
      setNotice({ kind: 'ok', text: `${t.fullName} — nömrə yeniləndi. Növbəti girişdə yeni nömrəni yazacaq.` })
      setPhoneFor(null)
      setPhone('')
      void search(q.trim())
    } else {
      setNotice({ kind: 'err', text: ERRORS[errorCode(r.data)] ?? 'Nömrə dəyişmədi' })
    }
  }

  const term = q.trim()

  return (
    <div className="card card-pad" style={{ marginBottom: 16 }}>
      <div className="card-title">PIN sıfırla · nömrəni dəyiş</div>
      <p className="muted" style={{ fontSize: 13, marginTop: 0, lineHeight: 1.6 }}>
        İşçilər — bütün şirkət üzrə; menecerlər — yalnız öz filiallarınızdakılar. Admin hesablarına aid deyil.
        Hər əməliyyat jurnala sizin adınızla yazılır.
      </p>
      <input
        className="inp"
        type="search"
        value={q}
        onChange={(e) => setQ(e.target.value)}
        placeholder="Ad və ya nömrənin bir hissəsi…"
        aria-label="İşçi axtar"
        style={{ maxWidth: 360 }}
      />

      {notice && notice.kind === 'ok' && (
        <div className="fb fb-ok" style={{ marginTop: 12, display: 'block' }}>
          {notice.pin ? (
            <>
              <div style={{ fontWeight: 700 }}>
                {notice.text} — müvəqqəti PIN: <span className="mono" style={{ fontSize: 16 }}>{notice.pin}</span>
              </div>
              <div style={{ fontSize: 12, marginTop: 4 }}>
                Bu PIN-i yalnız hesabın sahibinə verin. İlk girişdə öz PIN-ini təyin edəcək. Bu pəncərə bağlananda PIN yenidən görünməyəcək.
              </div>
            </>
          ) : (
            <div>{notice.text}</div>
          )}
          <button className="btn btn-sm" style={{ marginTop: 8 }} onClick={() => setNotice(null)}>Bağla</button>
        </div>
      )}
      {notice && notice.kind === 'err' && (
        <div className="fb fb-err" style={{ marginTop: 12 }}><span>{notice.text}</span></div>
      )}

      {term.length >= 2 && !searching && results.length === 0 && (
        <div className="muted" style={{ fontSize: 13, marginTop: 10 }}>Heç kim tapılmadı.</div>
      )}

      {results.length > 0 && (
        <div style={{ marginTop: 12, display: 'flex', flexDirection: 'column', gap: 8 }}>
          {results.map((t) => (
            <div key={t.id} style={{ border: '1px solid var(--c200, #e2e8f0)', borderRadius: 10, padding: '10px 12px' }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
                <div style={{ minWidth: 0, flex: '1 1 220px' }}>
                  <div style={{ fontWeight: 700 }}>
                    {t.fullName}
                    {t.isManager && (
                      <span
                        style={{
                          marginLeft: 8, fontSize: 11, fontWeight: 700, padding: '2px 8px', borderRadius: 999,
                          background: 'var(--c100, #f1f5f9)', color: 'var(--c700, #334155)', verticalAlign: 'middle',
                        }}
                      >
                        Menecer
                      </span>
                    )}
                  </div>
                  <div className="muted" style={{ fontSize: 12 }}>
                    {[t.position, t.locationName, t.phoneTail ? `…${t.phoneTail}` : 'nömrə yoxdur'].filter(Boolean).join(' · ')}
                  </div>
                </div>
                <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
                  <button
                    className="btn btn-sm"
                    disabled={busy || !t.activated}
                    title={t.activated ? undefined : 'Hesab hələ aktivləşdirilməyib'}
                    onClick={() => void reset(t)}
                  >
                    PIN sıfırla
                  </button>
                  <button
                    className="btn btn-sm"
                    disabled={busy}
                    onClick={() => { setPhoneFor(phoneFor === t.id ? null : t.id); setPhone('') }}
                  >
                    Nömrəni dəyiş
                  </button>
                </div>
              </div>
              {phoneFor === t.id && (
                <div style={{ display: 'flex', gap: 8, marginTop: 10, flexWrap: 'wrap' }}>
                  <input
                    className="inp"
                    type="tel"
                    inputMode="tel"
                    value={phone}
                    onChange={(e) => setPhone(e.target.value)}
                    placeholder="+994 50 123 45 67"
                    aria-label={`${t.fullName} üçün yeni nömrə`}
                    style={{ maxWidth: 220 }}
                    autoFocus
                  />
                  <button
                    className="btn btn-primary btn-sm"
                    disabled={busy || phone.replace(/\D/g, '').length < 7}
                    onClick={() => void savePhone(t)}
                  >
                    Yadda saxla
                  </button>
                  <button className="btn btn-sm" disabled={busy} onClick={() => { setPhoneFor(null); setPhone('') }}>
                    Ləğv et
                  </button>
                </div>
              )}
            </div>
          ))}
        </div>
      )}
    </div>
  )
}
