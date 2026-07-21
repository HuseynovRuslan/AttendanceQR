import { useEffect, useState } from 'react'
import {
  createVoteCampaign,
  deleteVoteCampaign,
  getVoteCampaign,
  resetVoteCampaignVotes,
  updateVoteCampaign,
  type VoteCampaign,
} from '../../api/vote'
import { IconCheck, IconX } from '../../components/icons'

const fmt = (iso: string) => iso.split('-').reverse().join('.')

/** Last day of the month a period belongs to, as an ISO date. */
function lastDayOf(period: string): string {
  const [y, m] = period.split('-').map(Number)
  const d = new Date(y, m, 0)
  return `${y}-${String(m).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

function addDays(iso: string, delta: number): string {
  const [y, m, d] = iso.split('-').map(Number)
  const dt = new Date(y, m - 1, d + delta)
  return `${dt.getFullYear()}-${String(dt.getMonth() + 1).padStart(2, '0')}-${String(dt.getDate()).padStart(2, '0')}`
}

const ERRORS: Record<string, string> = {
  EndBeforeStart: 'Bitmə tarixi başlanğıcdan əvvəl ola bilməz',
  WindowSpansTwoMonths: 'Başlanğıc və bitmə eyni ayda olmalıdır',
  MinCandidatesTooLow: 'Minimum namizəd sayı ən azı 2 olmalıdır',
  MinVotesTooLow: 'Qalib üçün minimum səs ən azı 1 olmalıdır',
  CampaignAlreadyExists: 'Bu ay üçün səsvermə artıq yaradılıb',
  CannotMoveToAnotherMonth: 'Səsverməni başqa aya keçirmək olmur — yeni ay üçün yenisini yaradın',
}

/**
 * The month's ballot: create it, edit it, or don't. Voting used to open by itself on a schedule, which
 * meant a company that simply didn't want an award that month still had one — and nobody had decided
 * to hold it. Now a month has a vote only because someone made one here.
 */
export function VoteCampaignCard({
  period,
  label,
  onChanged,
}: {
  period: string
  label: string
  onChanged: () => void
}) {
  const [campaign, setCampaign] = useState<VoteCampaign | null>(null)
  const [loaded, setLoaded] = useState(false)
  const [editing, setEditing] = useState(false)
  const [busy, setBusy] = useState(false)
  const [msg, setMsg] = useState<string | null>(null)
  const [err, setErr] = useState<string | null>(null)
  const [showAdvanced, setShowAdvanced] = useState(false)

  // Form state
  const [startsOn, setStartsOn] = useState('')
  const [endsOn, setEndsOn] = useState('')
  const [minCandidates, setMinCandidates] = useState(3)
  const [minVotes, setMinVotes] = useState(5)

  async function load() {
    setLoaded(false)
    const { status, data } = await getVoteCampaign(period)
    if (status === 200 && data && 'campaign' in data) setCampaign(data.campaign)
    setLoaded(true)
    setEditing(false)
    setShowAdvanced(false)
    setMsg(null)
    setErr(null)
  }

  useEffect(() => {
    void load()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [period])

  function startCreate() {
    // The award is for the whole month, so voting belongs at the end of it — people can only judge a
    // month they've lived through. Three days is enough to catch everyone's shifts.
    const last = lastDayOf(period)
    setStartsOn(addDays(last, -2))
    setEndsOn(last)
    setMinCandidates(3)
    setMinVotes(5)
    setEditing(true)
    setErr(null)
    setMsg(null)
  }

  function startEdit(c: VoteCampaign) {
    setStartsOn(c.startsOn)
    setEndsOn(c.endsOn)
    setMinCandidates(c.minCandidates)
    setMinVotes(c.minVotesToDecide)
    setEditing(true)
    setErr(null)
    setMsg(null)
  }

  async function save() {
    setBusy(true)
    setErr(null)
    setMsg(null)
    const input = { startsOn, endsOn, minCandidates, minVotesToDecide: minVotes }
    const { status, data } = campaign
      ? await updateVoteCampaign(campaign.id, input)
      : await createVoteCampaign(input)
    setBusy(false)
    if (status === 200 && data && 'campaign' in data) {
      setCampaign(data.campaign)
      setEditing(false)
      setMsg(campaign ? 'Dəyişikliklər yadda saxlanıldı' : 'Səsvermə yaradıldı')
      onChanged()
    } else {
      const code = data && 'error' in data ? data.error : ''
      setErr(ERRORS[code] ?? 'Yadda saxlanılmadı')
    }
  }

  async function remove() {
    if (!campaign) return
    const warn = campaign.votesCast > 0
      ? `${label} səsverməsi və verilmiş ${campaign.votesCast} səs tamamilə silinəcək.`
      : `${label} səsverməsi silinəcək — bu ay səsvermə keçirilməyəcək.`
    if (!window.confirm(`${warn}\n\nBu geri qaytarılmır. Davam edilsin?`)) return
    setBusy(true)
    await deleteVoteCampaign(campaign.id)
    setBusy(false)
    setCampaign(null)
    setMsg('Səsvermə silindi')
    onChanged()
  }

  async function resetVotes() {
    if (!campaign) return
    if (!window.confirm(`${label} ayının ${campaign.votesCast} səsi silinəcək, səsvermə özü qalacaq.\n\nDavam edilsin?`)) return
    setBusy(true)
    const { status, data } = await resetVoteCampaignVotes(campaign.id)
    setBusy(false)
    if (status === 200 && data && 'removedVotes' in data) {
      setMsg(`${data.removedVotes} səs silindi`)
      void load()
      onChanged()
    }
  }

  if (!loaded) return <div className="card card-pad" style={{ marginBottom: 16, minHeight: 96 }} />

  const badge = !campaign
    ? { text: 'Yaradılmayıb', cls: 'pill' }
    : campaign.state === 'open'
      ? { text: 'Davam edir', cls: 'pill pill-ok' }
      : campaign.state === 'scheduled'
        ? { text: 'Planlaşdırılıb', cls: 'pill pill-info' }
        : { text: 'Bitib', cls: 'pill' }

  return (
    <div className="card card-pad" style={{ marginBottom: 16 }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
        <div className="card-title" style={{ margin: 0 }}>{label} — səsvermə</div>
        <span className={badge.cls}>{badge.text}</span>
      </div>

      {!editing && (
        <>
          <div className="muted" style={{ fontSize: 13, marginTop: 6 }}>
            {!campaign
              ? 'Bu ay üçün səsvermə yaradılmayıb — işçilərdə səsvermə görünmür.'
              : campaign.state === 'open'
                ? `${fmt(campaign.startsOn)} – ${fmt(campaign.endsOn)} · indiyədək ${campaign.votesCast} səs verilib`
                : campaign.state === 'scheduled'
                  ? `${fmt(campaign.startsOn)} tarixində açılacaq, ${fmt(campaign.endsOn)} tarixində bağlanacaq. Açılanda işçilərə bildiriş gedəcək.`
                  : `${fmt(campaign.startsOn)} – ${fmt(campaign.endsOn)} · cəmi ${campaign.votesCast} səs`}
          </div>

          <div style={{ display: 'flex', gap: 8, marginTop: 12, flexWrap: 'wrap' }}>
            {!campaign ? (
              <button className="btn btn-primary" onClick={startCreate}>Səsvermə yarat</button>
            ) : (
              <>
                <button className="btn btn-sm" onClick={() => startEdit(campaign)}>Redaktə et</button>
                {campaign.votesCast > 0 && (
                  <button className="btn btn-sm" disabled={busy} onClick={() => void resetVotes()}>
                    Səsləri sıfırla
                  </button>
                )}
                <button className="btn btn-sm btn-danger" disabled={busy} onClick={() => void remove()}>
                  Sil
                </button>
              </>
            )}
          </div>
        </>
      )}

      {editing && (
        <div style={{ marginTop: 12 }}>
          <div className="form-row cols2">
            <div>
              <label className="form-label">Başlanğıc</label>
              <input className="inp" type="date" value={startsOn} onChange={(e) => setStartsOn(e.target.value)} />
            </div>
            <div>
              <label className="form-label">Bitmə</label>
              <input className="inp" type="date" value={endsOn} onChange={(e) => setEndsOn(e.target.value)} />
            </div>
          </div>

          {/* Thresholds have sensible defaults and are almost never touched — kept out of the way so the
              card reads as "when is the vote", not as a settings screen. */}
          {!showAdvanced ? (
            <button className="btn-link" type="button" onClick={() => setShowAdvanced(true)}>
              Əlavə parametrlər
            </button>
          ) : (
            <div className="form-row cols2">
              <div>
                <label className="form-label">Minimum namizəd sayı</label>
                <input className="inp" type="number" min={2} value={minCandidates}
                  onChange={(e) => setMinCandidates(Number(e.target.value))} />
                <div className="muted" style={{ fontSize: 11, marginTop: 4 }}>
                  Bundan az işçisi olan filialda səsvermə keçirilmir — səs gizli qala bilmir.
                </div>
              </div>
              <div>
                <label className="form-label">Qalib üçün minimum səs</label>
                <input className="inp" type="number" min={1} value={minVotes}
                  onChange={(e) => setMinVotes(Number(e.target.value))} />
                <div className="muted" style={{ fontSize: 11, marginTop: 4 }}>
                  Bundan az səs olsa, o filial üçün qalib elan olunmur.
                </div>
              </div>
            </div>
          )}

          {err && <div className="fb fb-err" style={{ marginTop: 10 }}><IconX /><span>{err}</span></div>}

          <div style={{ display: 'flex', gap: 8, marginTop: 12 }}>
            <button className="btn btn-primary" disabled={busy} onClick={() => void save()}>
              {busy ? 'Yadda saxlanılır…' : campaign ? 'Yadda saxla' : 'Yarat'}
            </button>
            <button className="btn btn-sm" disabled={busy} onClick={() => setEditing(false)}>Ləğv et</button>
          </div>
        </div>
      )}

      {!editing && msg && (
        <div className="fb fb-ok" style={{ marginTop: 12 }}><IconCheck /><span>{msg}</span></div>
      )}
    </div>
  )
}
