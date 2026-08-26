import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { appLogin, login } from '../api/auth'
import { getMyProfile } from '../api/attendance'
import { useAuth } from '../auth/AuthContext'
import { isAppMode } from '../lib/host'
import { decodeJwt } from '../lib/jwt'
import { initials } from '../lib/att'
import { clearProfiles, listProfiles, MAX_PROFILES, saveProfile, removeProfile } from '../lib/profiles'
import { SubPageHeader } from '../components/SubPageHeader'
import { PinInput } from '../components/PinInput'
import { IconCheck, IconLogout, IconUser, IconX } from '../components/icons'

/**
 * Several accounts on one phone — for the crew who have no phone of their own.
 *
 * The workers this exists for water trees along a road with no gate and no poster, and the brigadier
 * who assigns their work does it by telephone from somewhere else. Whoever IS on site holds one
 * handset; each worker switches to their own profile, scans, and takes their own selfie. That is what
 * separates a record from a claim: the GPS is the phone's real position, and the face in the photo is
 * the worker's own. A list of ticks would have been quicker and would have proved nothing.
 */
export function ProfilesPage() {
  const { employeeId, switchProfile } = useAuth()
  const [profiles, setProfiles] = useState(() => listProfiles())
  const [adding, setAdding] = useState(false)
  const navigate = useNavigate()

  const full = profiles.length >= MAX_PROFILES

  function refresh() {
    setProfiles(listProfiles())
  }

  return (
    <div className="min-h-screen bg-slate-50">
      <SubPageHeader title="Telefondakı hesablar" />

      <div className="flex flex-col gap-4 p-4">
        <p className="text-sm text-slate-500">
          Bir telefonda bir neçə hesab saxlaya bilərsiniz. Telefonu olmayan işçi öz hesabına keçir,
          skan edir və öz şəklini çəkir — qeyd onun adına yazılır.
        </p>

        <div className="divide-y divide-slate-100 overflow-hidden rounded-3xl border border-slate-100 bg-white shadow-sm">
          {profiles.map((p) => {
            const active = p.employeeId === employeeId
            return (
              <div key={p.employeeId} className="flex items-center gap-3 p-3">
                <button
                  type="button"
                  /* Switching to yourself would reload the app for no reason, and on a crew phone a
                     reload that changes nothing reads as a crash. */
                  onClick={() => !active && switchProfile(p.employeeId)}
                  className="flex min-w-0 flex-1 items-center gap-3 text-left"
                >
                  <span
                    className={`flex h-11 w-11 shrink-0 items-center justify-center rounded-full text-sm font-bold ${
                      active ? 'bg-blue-600 text-white' : 'bg-slate-100 text-slate-600'
                    }`}
                  >
                    {initials(p.name)}
                  </span>
                  <span className="min-w-0">
                    <span className="block truncate font-semibold text-slate-900">{p.name}</span>
                    <span className="block text-xs text-slate-400">
                      {active ? 'Bu hesabdasınız' : 'Keçmək üçün toxunun'}
                    </span>
                  </span>
                </button>

                {active ? (
                  <span className="flex h-8 w-8 items-center justify-center rounded-full bg-blue-50 text-blue-600">
                    <IconCheck className="h-5 w-5" />
                  </span>
                ) : (
                  <button
                    type="button"
                    aria-label={`${p.name} hesabını sil`}
                    onClick={() => {
                      // No confirm dialog: removing a profile destroys nothing on the server — the
                      // account, its records and its device binding all survive, and it is re-added
                      // with the same phone and PIN. A modal here would be ceremony over a mistake
                      // that costs twenty seconds to undo.
                      removeProfile(p.employeeId)
                      refresh()
                    }}
                    className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full text-slate-300 transition active:bg-slate-100"
                  >
                    <IconX className="h-5 w-5" />
                  </button>
                )}
              </div>
            )
          })}

          {profiles.length === 0 && (
            <div className="p-4 text-sm text-slate-400">Hələ saxlanmış hesab yoxdur.</div>
          )}
        </div>

        {adding ? (
          <AddProfileForm
            onCancel={() => setAdding(false)}
            onAdded={(id) => {
              refresh()
              setAdding(false)
              switchProfile(id)
            }}
          />
        ) : (
          <button
            type="button"
            disabled={full}
            onClick={() => setAdding(true)}
            className="flex items-center gap-3 rounded-3xl border border-slate-100 bg-white p-4 font-semibold text-blue-700 shadow-sm transition active:scale-[0.99] disabled:text-slate-400"
          >
            <span className="flex h-9 w-9 items-center justify-center rounded-full bg-blue-50">
              <IconUser className="h-5 w-5" />
            </span>
            {full ? `Limit doldu (${MAX_PROFILES})` : 'Hesab əlavə et'}
          </button>
        )}

        {profiles.length > 1 && (
          <button
            type="button"
            onClick={() => {
              // This one DOES ask. Clearing the list on a crew phone means every worker signs in by
              // PIN again, one at a time, and the PINs are the thing the holder does not have.
              if (!window.confirm('Bütün saxlanmış hesablar silinsin? Hər kəs yenidən PIN ilə daxil olmalı olacaq.')) return
              clearProfiles()
              refresh()
              navigate('/menu')
            }}
            className="flex items-center gap-3 rounded-3xl border border-red-100 bg-white p-4 font-semibold text-red-600 shadow-sm transition active:scale-[0.99]"
          >
            <span className="flex h-9 w-9 items-center justify-center rounded-full bg-red-50">
              <IconLogout className="h-5 w-5" />
            </span>
            Bütün hesabları sil
          </button>
        )}
      </div>
    </div>
  )
}

/**
 * Adds one account without disturbing the session already signed in. The credentials go to the SAME
 * login endpoint as the front door — there is no back way into an account here — and the token it
 * returns is filed under that employee's own id.
 */
function AddProfileForm({
  onAdded,
  onCancel,
}: {
  onAdded: (employeeId: string) => void
  onCancel: () => void
}) {
  const [identifier, setIdentifier] = useState('')
  const [pin, setPin] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    setLoading(true)
    try {
      const { status, data } = await (isAppMode() ? appLogin : login)(identifier, pin)
      if (status !== 200 || !data || !('token' in data)) {
        if (status === 429) {
          const m = data && 'minutes' in data && data.minutes ? data.minutes : 5
          setError(`Çox sayda cəhd — ${m} dəqiqə sonra yenidən cəhd edin`)
        } else {
          setError('Nömrə və ya PIN yanlışdır')
        }
        return
      }

      const token = data.token
      const sub = decodeJwt(token)?.sub
      if (!sub) {
        setError('Giriş alınmadı — yenidən cəhd edin')
        return
      }

      // The name for the row, asked with the NEW token explicitly: the active session still belongs
      // to whoever is signed in, so an ordinary call would answer about THEM and label this row with
      // the wrong person's name. Re-adding somebody already listed is not an error — it is how a
      // holder refreshes a token that stopped working after a PIN change, and saveProfile replaces
      // the dead entry in place.
      const { status: st, data: me } = await getMyProfile(token)
      const name = st === 200 && me && 'fullName' in me ? me.fullName : 'İşçi'
      saveProfile({ employeeId: sub, name, token, addedAtMs: Date.now() })
      onAdded(sub)
    } catch {
      setError('Serverə qoşulmaq mümkün olmadı')
    } finally {
      setLoading(false)
    }
  }

  return (
    <form onSubmit={onSubmit} className="flex flex-col gap-3 rounded-3xl border border-slate-100 bg-white p-4 shadow-sm">
      <div className="font-semibold text-slate-900">Hesab əlavə et</div>

      {error && (
        <div className="fb fb-err">
          <IconX />
          <span>{error}</span>
        </div>
      )}

      <div>
        <label className="form-label">Telefon nömrəsi</label>
        <input
          className="inp"
          type="tel"
          inputMode="tel"
          placeholder="0XX XXX XX XX"
          required
          value={identifier}
          onChange={(e) => setIdentifier(e.target.value)}
        />
      </div>

      <div>
        <label className="form-label">PIN (4 rəqəm)</label>
        <PinInput value={pin} onChange={setPin} />
      </div>

      <div className="flex gap-2">
        <button type="button" onClick={onCancel} className="btn btn-bl flex-1">
          Ləğv et
        </button>
        <button type="submit" disabled={loading} className="btn btn-primary btn-bl flex-1">
          {loading ? 'Yoxlanılır…' : 'Əlavə et'}
        </button>
      </div>
    </form>
  )
}
