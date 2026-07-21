import { useEffect, useState } from 'react'
import {
  createVoteCampaign,
  deleteVoteCampaign,
  getVoteCampaign,
  resetVoteCampaignVotes,
  updateVoteCampaign,
  type VoteCampaign,
  type VoteCampaignResult,
} from '../../api/vote'
import { getPositions } from '../../api/positions'
import { IconCheck, IconX } from '../../components/icons'

const fmt = (iso: string) => iso.split('-').reverse().join('.')
/** Date with its time of day, e.g. "29.07.2026 09:00". */
const fmtAt = (iso: string, time: string) => `${fmt(iso)} ${time}`

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
  onCampaign,
}: {
  period: string
  label: string
  onChanged: () => void
  /** Tells the page whether this month has a ballot, so it can hide results that belong to none. */
  onCampaign: (exists: boolean) => void
}) {
  const [campaign, setCampaign] = useState<VoteCampaign | null>(null)
  const [result, setResult] = useState<VoteCampaignResult | null>(null)
  const [loaded, setLoaded] = useState(false)
  const [editing, setEditing] = useState(false)
  const [busy, setBusy] = useState(false)
  const [msg, setMsg] = useState<string | null>(null)
  const [err, setErr] = useState<string | null>(null)
  const [showAdvanced, setShowAdvanced] = useState(false)

  // Form state
  const [startsOn, setStartsOn] = useState('')
  const [endsOn, setEndsOn] = useState('')
  const [startsAt, setStartsAt] = useState('00:00')
  const [endsAt, setEndsAt] = useState('23:59')
  const [minCandidates, setMinCandidates] = useState(3)
  const [minVotes, setMinVotes] = useState(5)
  const [excluded, setExcluded] = useState<string[]>([])
  const [positions, setPositions] = useState<{ name: string; count: number }[]>([])

  async function load() {
    setLoaded(false)
    const { status, data } = await getVoteCampaign(period)
    const found = status === 200 && data && 'campaign' in data ? data.campaign : null
    setCampaign(found)
    setResult(status === 200 && data && 'result' in data ? data.result ?? null : null)
    onCampaign(found !== null)
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

  useEffect(() => {
    void getPositions().then(({ status, data }) => {
      if (status === 200 && Array.isArray(data)) setPositions(data.map((p) => ({ name: p.name, count: p.count })))
    })
  }, [])

  function startCreate() {
    // The award is for the whole month, so voting belongs at the end of it — people can only judge a
    // month they've lived through. Three days is enough to catch everyone's shifts.
    const last = lastDayOf(period)
    setStartsOn(addDays(last, -2))
    setEndsOn(last)
    // Whole days by default — a time-of-day window is the exception, not the norm.
    setStartsAt('00:00')
    setEndsAt('23:59')
    setMinCandidates(3)
    setMinVotes(5)
    setExcluded([])
    setEditing(true)
    setErr(null)
    setMsg(null)
  }

  function startEdit(c: VoteCampaign) {
    setStartsOn(c.startsOn)
    setEndsOn(c.endsOn)
    setStartsAt(c.startsAt ?? '00:00')
    setEndsAt(c.endsAt ?? '23:59')
    setMinCandidates(c.minCandidates)
    setMinVotes(c.minVotesToDecide)
    setExcluded(c.excludedPositions ?? [])
    // A campaign that bars positions was configured deliberately — show that section straight away
    // rather than hiding a rule the admin is about to look for.
    setShowAdvanced((c.excludedPositions?.length ?? 0) > 0)
    setEditing(true)
    setErr(null)
    setMsg(null)
  }

  async function save() {
    setBusy(true)
    setErr(null)
    setMsg(null)
    const input = { startsOn, endsOn, startsAt, endsAt, minCandidates, minVotesToDecide: minVotes, excludedPositions: excluded }
    const { status, data } = campaign
      ? await updateVoteCampaign(campaign.id, input)
      : await createVoteCampaign(input)
    setBusy(false)
    if (status === 200 && data && 'campaign' in data) {
      setCampaign(data.campaign)
      onCampaign(data.campaign !== null)
      setEditing(false)
      // The admin creates a ballot and then watches their phone. Say plainly whether the notice has
      // already gone out or when it will — silence here is what made it look broken.
      const c = data.campaign
      setMsg(
        !c ? 'Yadda saxlanıldı'
          : c.notified ? 'Səsvermə açıldı və bütün işçilərə bildiriş göndərildi ✓'
          : c.state === 'scheduled'
            ? `Səsvermə yaradıldı — bildiriş ${fmtAt(c.startsOn, c.startsAt)} tarixində avtomatik göndəriləcək`
            : campaign ? 'Dəyişikliklər yadda saxlanıldı' : 'Səsvermə yaradıldı',
      )
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
    onCampaign(false)
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
                ? `${fmtAt(campaign.startsOn, campaign.startsAt)} – ${fmtAt(campaign.endsOn, campaign.endsAt)} · indiyədək ${campaign.votesCast} səs verilib`
                : campaign.state === 'scheduled'
                  ? `${fmtAt(campaign.startsOn, campaign.startsAt)} tarixində açılacaq, ${fmtAt(campaign.endsOn, campaign.endsAt)} bağlanacaq. Açılanda işçilərə bildiriş gedəcək.`
                  : `${fmtAt(campaign.startsOn, campaign.startsAt)} – ${fmtAt(campaign.endsOn, campaign.endsAt)} · cəmi ${campaign.votesCast} səs`}
          </div>

          {/* A closed ballot with no winner looks exactly like a broken one. Say which it is —
              withheld on purpose, or nobody voted — and how to get a result if that's wanted. */}
          {campaign?.state === 'finished' && result && (
            <div style={{ marginTop: 10 }}>
              {result.winners.length > 0 ? (
                <div className="fb fb-ok" style={{ display: 'block' }}>
                  <div style={{ fontWeight: 700, marginBottom: 4 }}>🏆 Qaliblər elan olundu</div>
                  {result.winners.map((w) => (
                    <div key={w.employeeId} style={{ fontSize: 13 }}>
                      {w.locationName}: <b>{w.fullName}</b> — {w.votes} səs
                    </div>
                  ))}
                </div>
              ) : result.noVotes ? (
                <div className="fb fb-info"><span>Heç kim səs vermədi — qalib elan olunmadı.</span></div>
              ) : (
                <div className="fb fb-info" style={{ display: 'block' }}>
                  <div style={{ fontWeight: 700, marginBottom: 4 }}>Qalib elan olunmadı</div>
                  <div style={{ fontSize: 13 }}>
                    Cəmi {result.votesCast} səs verilib, qalib üçün ən azı {result.minVotesToDecide} səs
                    tələb olunur. «Redaktə et» → «Əlavə parametrlər»dən bu həddi azaltsanız, qalib bir
                    neçə dəqiqə ərzində avtomatik elan olunacaq.
                  </div>
                </div>
              )}
            </div>
          )}

          {/* Whether employees actually heard about it — the ballot being "open" says nothing about
              that, and an unannounced ballot collects almost no votes. */}
          {campaign && campaign.state !== 'finished' && (
            <div className="muted" style={{ fontSize: 12, marginTop: 4 }}>
              {campaign.notified
                ? '✓ İşçilərə bildiriş göndərilib'
                : `Bildiriş ${fmtAt(campaign.startsOn, campaign.startsAt)} tarixində göndəriləcək`}
            </div>
          )}

          {/* A rule that changes who appears on the ballot shouldn't live only behind an edit form. */}
          {campaign && campaign.excludedPositions?.length > 0 && (
            <div className="muted" style={{ fontSize: 12, marginTop: 6 }}>
              Namizəd ola bilməz: {campaign.excludedPositions.join(', ')}
            </div>
          )}

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
          {/* Date and time together: "closes on the 31st" still leaves the ballot open during the
              shift the result is announced on, and a vote meant for one evening needed a whole day. */}
          <div className="form-row cols2">
            <div>
              <label className="form-label">Başlanğıc</label>
              <div style={{ display: 'flex', gap: 8 }}>
                <input className="inp" type="date" value={startsOn} onChange={(e) => setStartsOn(e.target.value)} />
                <input className="inp" type="time" value={startsAt} style={{ maxWidth: 120 }}
                  onChange={(e) => setStartsAt(e.target.value)} />
              </div>
            </div>
            <div>
              <label className="form-label">Bitmə</label>
              <div style={{ display: 'flex', gap: 8 }}>
                <input className="inp" type="date" value={endsOn} onChange={(e) => setEndsOn(e.target.value)} />
                <input className="inp" type="time" value={endsAt} style={{ maxWidth: 120 }}
                  onChange={(e) => setEndsAt(e.target.value)} />
              </div>
            </div>
          </div>

          {/* Thresholds have sensible defaults and are almost never touched — kept out of the way so the
              card reads as "when is the vote", not as a settings screen. */}
          {!showAdvanced ? (
            <button className="btn-link" type="button" onClick={() => setShowAdvanced(true)}>
              Əlavə parametrlər
            </button>
          ) : (
            <>
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

            {/* Named as who is OUT rather than who is IN. Position is optional free text, so an
                allow-list would quietly drop everyone whose position is blank. */}
            {positions.length > 0 && (
              <div style={{ marginTop: 4 }}>
                <label className="form-label">Namizəd ola bilməyən vəzifələr</label>
                <div className="chip-row" style={{ marginTop: 4 }}>
                  {positions.map((p) => {
                    const on = excluded.includes(p.name)
                    return (
                      <span
                        key={p.name}
                        className={`chip${on ? ' active' : ''}`}
                        onClick={() =>
                          setExcluded((prev) =>
                            on ? prev.filter((x) => x !== p.name) : [...prev, p.name])
                        }
                      >
                        {p.name} · {p.count}
                      </span>
                    )
                  })}
                </div>
                <div className="muted" style={{ fontSize: 11, marginTop: 6 }}>
                  Seçilən vəzifələr işçilərin siyahısında namizəd kimi görünməyəcək — məsələn layihə
                  rəhbərləri. Heç nə seçməsəniz, filialdakı hər kəs namizəddir. Vəzifəsi yazılmamış
                  işçilər həmişə namizəd qalır.
                </div>
              </div>
            )}
            </>
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
