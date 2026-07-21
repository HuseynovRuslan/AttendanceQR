import { useEffect, useState } from 'react'
import { getVoteSettings, saveVoteSettings, type VoteSettings } from '../../api/vote'
import { IconCheck, IconX } from '../../components/icons'

const fmt = (iso: string) => iso.split('-').reverse().join('.')

/** Company-level ballot settings, on the same screen as the results — the owner changes the dates or
 *  switches it off themselves rather than asking for a config change. */
export function VoteSettingsCard() {
  const [s, setS] = useState<VoteSettings | null>(null)
  const [manual, setManual] = useState(false)
  const [busy, setBusy] = useState(false)
  const [msg, setMsg] = useState<string | null>(null)
  const [err, setErr] = useState<string | null>(null)

  useEffect(() => {
    void getVoteSettings().then(({ status, data }) => {
      if (status === 200 && data && 'enabled' in data) {
        setS(data)
        setManual(!!(data.manualFrom && data.manualTo))
      }
    })
  }, [])

  if (!s) return null

  function set<K extends keyof VoteSettings>(k: K, v: VoteSettings[K]) {
    setS((prev) => (prev ? { ...prev, [k]: v } : prev))
  }

  async function save() {
    if (!s) return
    setBusy(true)
    setMsg(null)
    setErr(null)
    const { status, data } = await saveVoteSettings({
      enabled: s.enabled,
      openDaysBeforeEnd: s.openDaysBeforeEnd,
      manualFrom: manual ? s.manualFrom : null,
      manualTo: manual ? s.manualTo : null,
      minCandidates: s.minCandidates,
      minVotesToDecide: s.minVotesToDecide,
    })
    setBusy(false)
    if (status === 200 && data && 'enabled' in data) {
      setS(data)
      setMsg('Yadda saxlanıldı')
    } else {
      const code = data && 'error' in data ? data.error : ''
      setErr(
        code === 'ManualWindowNeedsBothDates' ? 'Hər iki tarixi seçin'
          : code === 'ManualWindowReversed' ? 'Bitmə tarixi başlanğıcdan əvvəl ola bilməz'
          : code === 'OpenDaysOutOfRange' ? 'Gün sayı 1–28 aralığında olmalıdır'
          : 'Yadda saxlanılmadı',
      )
    }
  }

  return (
    <div className="card card-pad" style={{ marginBottom: 16 }}>
      <div className="card-title">Səsvermə parametrləri</div>

      <div className="chip-row">
        <span className={`chip${s.enabled ? ' active' : ''}`} onClick={() => set('enabled', true)}>Aktiv</span>
        <span className={`chip${!s.enabled ? ' active' : ''}`} onClick={() => set('enabled', false)}>Söndürülüb</span>
      </div>

      <label className="form-label">Səsvermə vaxtı</label>
      <div className="chip-row" style={{ marginTop: 4 }}>
        <span className={`chip${!manual ? ' active' : ''}`} onClick={() => setManual(false)}>Hər ay avtomatik</span>
        <span className={`chip${manual ? ' active' : ''}`} onClick={() => setManual(true)}>Tarixi özüm seçim</span>
      </div>

      {manual ? (
        <div className="form-row cols2">
          <div>
            <label className="form-label">Başlanğıc</label>
            <input
              className="inp"
              type="date"
              value={s.manualFrom ?? ''}
              onChange={(e) => set('manualFrom', e.target.value || null)}
            />
          </div>
          <div>
            <label className="form-label">Bitmə</label>
            <input
              className="inp"
              type="date"
              value={s.manualTo ?? ''}
              onChange={(e) => set('manualTo', e.target.value || null)}
            />
          </div>
        </div>
      ) : (
        <div className="form-row cols2">
          <div>
            <label className="form-label">Ayın son neçə günü açıq olsun</label>
            <input
              className="inp"
              type="number"
              min={1}
              max={28}
              value={s.openDaysBeforeEnd}
              onChange={(e) => set('openDaysBeforeEnd', Number(e.target.value))}
            />
          </div>
          <div />
        </div>
      )}

      <div className="form-row cols2">
        <div>
          <label className="form-label">Minimum namizəd sayı</label>
          <input
            className="inp"
            type="number"
            min={2}
            value={s.minCandidates}
            onChange={(e) => set('minCandidates', Number(e.target.value))}
          />
          <div className="muted" style={{ fontSize: 11, marginTop: 4 }}>
            Bundan az işçisi olan filialda səsvermə keçirilmir — səs gizli qala bilmir.
          </div>
        </div>
        <div>
          <label className="form-label">Qalib üçün minimum səs</label>
          <input
            className="inp"
            type="number"
            min={1}
            value={s.minVotesToDecide}
            onChange={(e) => set('minVotesToDecide', Number(e.target.value))}
          />
          <div className="muted" style={{ fontSize: 11, marginTop: 4 }}>
            Bundan az səs olsa, o filial üçün qalib elan olunmur.
          </div>
        </div>
      </div>

      {/* The settings mean nothing to the owner as numbers — this is what they actually produce. */}
      <div className={`fb ${s.isOpenNow ? 'fb-ok' : 'fb-info'}`} style={{ marginTop: 6 }}>
        <span>
          {!s.enabled
            ? 'Səsvermə söndürülüb — işçilərə göstərilmir.'
            : s.isOpenNow
              ? `Səsvermə HAZIRDA AÇIQDIR · ${fmt(s.currentWindowFrom)} – ${fmt(s.currentWindowTo)}`
              : `Səsvermə bağlıdır · növbəti pəncərə: ${fmt(s.currentWindowFrom)} – ${fmt(s.currentWindowTo)}`}
        </span>
      </div>

      {err && <div className="fb fb-err" style={{ marginTop: 10 }}><IconX /><span>{err}</span></div>}
      {msg && <div className="fb fb-ok" style={{ marginTop: 10 }}><IconCheck /><span>{msg}</span></div>}

      <div style={{ marginTop: 12 }}>
        <button className="btn btn-primary" disabled={busy} onClick={() => void save()}>
          {busy ? 'Yadda saxlanılır…' : 'Yadda saxla'}
        </button>
      </div>
    </div>
  )
}
